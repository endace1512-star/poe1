using System.Text.Json;
using Poe1.Infrastructure;

namespace Poe1.Pricing;

/// <summary>One price line from poe.ninja, normalised across its two API families.</summary>
public sealed record NinjaLine
{
    public required string Type { get; init; }
    public required string Name { get; init; }
    public string? BaseType { get; init; }
    public string? Variant { get; init; }
    public int? Links { get; init; }
    public int? GemLevel { get; init; }
    public int? GemQuality { get; init; }
    public bool Corrupted { get; init; }
    public int? LevelRequired { get; init; }
    public double ChaosValue { get; init; }
    public int ListingCount { get; init; }
    public string? Icon { get; init; }
    public string? Category { get; init; }
    /// <summary>True for currency-exchange prices (volume based, always trusted); false for stash listings.</summary>
    public bool Exchange { get; init; }
}

/// <summary>
/// poe.ninja client. Since 2025 poe.ninja serves PoE1 prices from two endpoints:
/// <c>/poe1/api/economy/exchange/current/overview</c> for currency-exchange items (currency, fragments, cards, …)
/// and <c>/poe1/api/economy/stash/current/item/overview</c> for everything listed in stashes (uniques, gems, bases, …).
/// Responses are cached on disk so the app keeps prices when poe.ninja is unreachable.
/// </summary>
public sealed class NinjaClient
{
    public const string Host = "https://poe.ninja";
    public const string ImageHost = "https://web.poecdn.com";

    /// <summary>Types served by the currency-exchange endpoint.</summary>
    public static readonly string[] ExchangeTypes =
    {
        "Currency", "Fragment", "DivinationCard", "Essence", "Fossil", "Resonator", "Oil", "DeliriumOrb",
        "Tattoo", "Omen", "Artifact", "AllflameEmber", "Runegraft", "Astrolabe", "Thread", "Scarab",
    };

    /// <summary>Types served by the stash-item endpoint.</summary>
    public static readonly string[] StashTypes =
    {
        "UniqueWeapon", "UniqueArmour", "UniqueAccessory", "UniqueFlask", "UniqueJewel", "UniqueTincture",
        "UniqueRelic", "UniqueMap", "SkillGem", "ClusterJewel", "Map", "BlightedMap", "BlightRavagedMap",
        "Invitation", "Beast", "Wombgift", "Incubator", "Vial", "Memory", "Coffin", "BaseType",
    };

    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly ILogger<NinjaClient> _log;

    public NinjaClient(HttpClient http, AppPaths paths, ILogger<NinjaClient> log)
    {
        _http = http;
        _paths = paths;
        _log = log;
    }

    /// <summary>Downloads every type and builds a <see cref="PriceBook"/>. Falls back to the disk cache per type.</summary>
    public async Task<PriceBook> FetchAsync(string league, CancellationToken ct)
    {
        var lines = new List<NinjaLine>();
        double? divine = null;
        var failures = 0;

        foreach (var type in ExchangeTypes)
        {
            var json = await GetCachedAsync(league, "exchange", type, ct);
            if (json is null) { failures++; continue; }
            var (parsed, div) = ParseExchange(type, json);
            lines.AddRange(parsed);
            divine ??= div;
        }
        foreach (var type in StashTypes)
        {
            var json = await GetCachedAsync(league, "stash", type, ct);
            if (json is null) { failures++; continue; }
            lines.AddRange(ParseStash(type, json));
        }

        if (lines.Count == 0)
            throw new InvalidOperationException($"Không lấy được giá từ poe.ninja cho league \"{league}\".");
        if (failures > 0) _log.LogWarning("poe.ninja: {Failures} types failed for {League}", failures, league);

        return new PriceBook(league, DateTimeOffset.UtcNow, divine ?? 0, lines);
    }

    private async Task<string?> GetCachedAsync(string league, string family, string type, CancellationToken ct)
    {
        var dir = Path.Combine(_paths.PriceCacheDir, SafeName(league));
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{family}-{type}.json");
        var url = family == "exchange"
            ? $"{Host}/poe1/api/economy/exchange/current/overview?league={Uri.EscapeDataString(league)}&type={type}"
            : $"{Host}/poe1/api/economy/stash/current/item/overview?league={Uri.EscapeDataString(league)}&type={type}";
        try
        {
            using var res = await _http.GetAsync(url, ct);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(ct);
                await File.WriteAllTextAsync(file, body, ct);
                return body;
            }
            _log.LogDebug("poe.ninja {Type}: HTTP {Status}", type, (int)res.StatusCode);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _log.LogWarning("poe.ninja {Type}: {Message}", type, e.Message);
        }
        return File.Exists(file) ? await File.ReadAllTextAsync(file, ct) : null;
    }

    private static string SafeName(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    internal static (List<NinjaLine> Lines, double? DivineChaos) ParseExchange(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var result = new List<NinjaLine>();
        if (!root.TryGetProperty("lines", out var lines) || !root.TryGetProperty("items", out var items))
            return (result, null);

        // Prices are in the "primary" currency; convert to chaos via the "rates" table when primary isn't chaos.
        var primary = root.GetProperty("core").TryGetProperty("primary", out var p) ? p.GetString() : "chaos";
        var rates = new Dictionary<string, double>();
        if (root.GetProperty("core").TryGetProperty("rates", out var r))
            foreach (var kv in r.EnumerateObject())
                if (kv.Value.ValueKind == JsonValueKind.Number) rates[kv.Name] = kv.Value.GetDouble();

        double toChaos = 1;
        if (primary != "chaos" && rates.TryGetValue("chaos", out var chaosPerPrimary) && chaosPerPrimary > 0)
            toChaos = chaosPerPrimary;

        double? divine = null;
        if (primary == "chaos" && rates.TryGetValue("divine", out var divPerChaos) && divPerChaos > 0)
            divine = 1 / divPerChaos;
        else if (primary == "divine") divine = toChaos;

        var meta = new Dictionary<string, (string Name, string? Image, string? Category)>();
        foreach (var it in items.EnumerateArray())
        {
            var id = it.GetProperty("id").GetString()!;
            meta[id] = (it.GetProperty("name").GetString()!,
                it.TryGetProperty("image", out var img) ? img.GetString() : null,
                it.TryGetProperty("category", out var cat) ? cat.GetString() : null);
        }

        foreach (var line in lines.EnumerateArray())
        {
            var id = line.GetProperty("id").GetString()!;
            if (!meta.TryGetValue(id, out var m)) continue;
            if (!line.TryGetProperty("primaryValue", out var pv) || pv.ValueKind != JsonValueKind.Number) continue;
            result.Add(new NinjaLine
            {
                Type = type,
                Name = m.Name,
                ChaosValue = pv.GetDouble() * toChaos,
                Exchange = true,
                Icon = m.Image is null ? null : m.Image.StartsWith("http") ? m.Image : ImageHost + m.Image,
                Category = m.Category,
            });
        }

        // Chaos is the unit and has no line of its own in some responses.
        if (type == "Currency" && result.All(l => l.Name != "Chaos Orb"))
            result.Add(new NinjaLine { Type = type, Name = "Chaos Orb", ChaosValue = 1, Exchange = true, Category = "Currency" });
        return (result, divine);
    }

    internal static List<NinjaLine> ParseStash(string type, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<NinjaLine>();
        if (!doc.RootElement.TryGetProperty("lines", out var lines)) return result;
        foreach (var l in lines.EnumerateArray())
        {
            result.Add(new NinjaLine
            {
                Type = type,
                Name = Str(l, "name") ?? "",
                BaseType = Str(l, "baseType"),
                Variant = Str(l, "variant"),
                Links = Int(l, "links"),
                GemLevel = Int(l, "gemLevel"),
                GemQuality = Int(l, "gemQuality"),
                Corrupted = l.TryGetProperty("corrupted", out var c) && c.ValueKind == JsonValueKind.True,
                LevelRequired = Int(l, "levelRequired"),
                ChaosValue = l.TryGetProperty("chaosValue", out var cv) && cv.ValueKind == JsonValueKind.Number ? cv.GetDouble() : 0,
                ListingCount = Int(l, "listingCount") ?? 0,
                Icon = Str(l, "icon"),
                Category = type,
            });
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? (int)v.GetDouble() : null;
}
