using System.Text.Json;
using Poe1.Infrastructure;
using Poe1.Poe;

namespace Poe1.Builds;

/// <summary>What a character is wearing right now, as pathofexile.com last saved it.</summary>
public sealed record CharacterGear(
    string Character,
    int? Level,
    string? Class,
    IReadOnlyList<PoeItem> Equipped,
    IReadOnlyList<PoeItem> Jewels,
    IReadOnlySet<int> Passives,
    DateTimeOffset FetchedAt,
    bool Demo = false);

/// <summary>
/// Reads equipment, socketed gems, tree jewels and allocated passives from pathofexile.com.
/// The game saves the character to the server when you change zone, so data follows the game with a short delay.
/// Results are cached briefly so zone changes don't hammer the API.
/// </summary>
public sealed class CharacterGearService
{
    public static readonly string[] EquipmentSlots =
    {
        "Weapon", "Offhand", "Weapon2", "Offhand2", "Helm", "BodyArmour", "Gloves", "Boots",
        "Amulet", "Ring", "Ring2", "Ring3", "Belt", "Flask",
    };

    private readonly PoeApiClient _api;
    private readonly Dictionary<string, CharacterGear> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<CharacterInfo>? _characters;
    private DateTimeOffset _charactersAt;

    public CharacterGearService(PoeApiClient api) => _api = api;

    public async Task<List<CharacterInfo>> CharactersAsync(CancellationToken ct, bool force = false)
    {
        if (!force && _characters is not null && DateTimeOffset.UtcNow - _charactersAt < TimeSpan.FromMinutes(5)) return _characters;
        _characters = await _api.GetCharactersAsync(ct);
        _charactersAt = DateTimeOffset.UtcNow;
        return _characters;
    }

    public async Task<CharacterGear> GetAsync(string character, bool force, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var minAge = force ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(60);
            if (_cache.TryGetValue(character, out var cached) && DateTimeOffset.UtcNow - cached.FetchedAt < minAge)
                return cached;

            var items = await _api.GetCharacterItemsAsync(character, ct);
            var passives = await _api.GetPassiveSkillsAsync(character, ct);
            CharacterInfo? info = null;
            try { info = (await CharactersAsync(ct)).FirstOrDefault(c => string.Equals(c.Name, character, StringComparison.OrdinalIgnoreCase)); }
            catch (PoeApiException) { /* level/class are optional */ }

            var gear = new CharacterGear(
                character, info?.Level, info?.Class,
                items.Where(i => EquipmentSlots.Contains(i.InventoryId)).ToList(),
                passives.Items,
                passives.Hashes.ToHashSet(),
                DateTimeOffset.UtcNow);
            _cache[character] = gear;
            return gear;
        }
        finally
        {
            _lock.Release();
        }
    }
}

public sealed record PassiveNode(int Id, string Name, string Kind, string? Ascendancy);

/// <summary>
/// Passive node names from GGG's official tree export (github.com/grindinggear/skilltree-export),
/// downloaded once and cached; refreshed weekly so a new league's tree shows up on its own.
/// </summary>
public sealed class PassiveTreeData
{
    public const string Url = "https://raw.githubusercontent.com/grindinggear/skilltree-export/master/data.json";
    private readonly HttpClient _http;
    private readonly AppPaths _paths;
    private readonly ILogger<PassiveTreeData> _log;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<int, PassiveNode>? _nodes;

    public PassiveTreeData(HttpClient http, AppPaths paths, ILogger<PassiveTreeData> log)
    {
        _http = http;
        _paths = paths;
        _log = log;
    }

    private string CacheFile => Path.Combine(_paths.DataDir, "passive-tree.json");

    public async Task<IReadOnlyDictionary<int, PassiveNode>> GetAsync(CancellationToken ct)
    {
        if (_nodes is not null) return _nodes;
        await _lock.WaitAsync(ct);
        try
        {
            if (_nodes is not null) return _nodes;
            var fresh = File.Exists(CacheFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(CacheFile) < TimeSpan.FromDays(7);
            if (!fresh)
            {
                try
                {
                    var json = await _http.GetStringAsync(Url, ct);
                    var nodes = Extract(json);
                    await File.WriteAllTextAsync(CacheFile, JsonSerializer.Serialize(nodes.Values), ct);
                    return _nodes = nodes;
                }
                catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
                {
                    _log.LogWarning("Passive tree download failed: {Message}", e.Message);
                }
            }
            if (File.Exists(CacheFile))
            {
                var list = JsonSerializer.Deserialize<List<PassiveNode>>(await File.ReadAllTextAsync(CacheFile, ct)) ?? new();
                return _nodes = list.ToDictionary(n => n.Id);
            }
            return new Dictionary<int, PassiveNode>();
        }
        finally
        {
            _lock.Release();
        }
    }

    internal static Dictionary<int, PassiveNode> Extract(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new Dictionary<int, PassiveNode>();
        foreach (var prop in doc.RootElement.GetProperty("nodes").EnumerateObject())
        {
            var n = prop.Value;
            if (!n.TryGetProperty("skill", out var skill) || skill.ValueKind != JsonValueKind.Number) continue;
            bool Flag(string name) => n.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
            var asc = n.TryGetProperty("ascendancyName", out var a) ? a.GetString() : null;
            var kind = Flag("isKeystone") ? "keystone"
                : Flag("isNotable") ? (asc is null ? "notable" : "ascendancy-notable")
                : Flag("isJewelSocket") ? "jewel"
                : Flag("isMastery") ? "mastery"
                : Flag("isAscendancyStart") || n.TryGetProperty("classStartIndex", out _) ? "start"
                : asc is not null ? "ascendancy"
                : "small";
            var name = n.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
            result[skill.GetInt32()] = new PassiveNode(skill.GetInt32(), name, kind, asc);
        }
        return result;
    }
}

/// <summary>Turns a PoB item set into fake "equipped" items so the Build tab can be tried with --demo.</summary>
public static class DemoGear
{
    private static readonly Dictionary<string, (string Inventory, int X)> SlotToInventory = new()
    {
        ["Weapon 1"] = ("Weapon", 0), ["Weapon 2"] = ("Offhand", 0), ["Helmet"] = ("Helm", 0),
        ["Body Armour"] = ("BodyArmour", 0), ["Gloves"] = ("Gloves", 0), ["Boots"] = ("Boots", 0),
        ["Amulet"] = ("Amulet", 0), ["Ring 1"] = ("Ring", 0), ["Ring 2"] = ("Ring2", 0), ["Belt"] = ("Belt", 0),
        ["Flask 1"] = ("Flask", 0), ["Flask 2"] = ("Flask", 1), ["Flask 3"] = ("Flask", 2), ["Flask 4"] = ("Flask", 3), ["Flask 5"] = ("Flask", 4),
    };

    /// <summary>
    /// The first item set, with every other explicit mod dropped and one slot left empty, and the
    /// first tree spec minus a few nodes – roughly "a character part-way through the build".
    /// </summary>
    public static CharacterGear From(PobBuild build)
    {
        var equipped = new List<PoeItem>();
        var set = build.ItemSets.FirstOrDefault();
        var skills = build.SkillSets.FirstOrDefault();
        var skip = set?.Slots.Keys.FirstOrDefault(k => k == "Gloves");
        foreach (var (slot, itemId) in set?.Slots ?? new Dictionary<string, int>())
        {
            if (slot == skip || !SlotToInventory.TryGetValue(slot, out var inv) || !build.Items.TryGetValue(itemId, out var p)) continue;
            var item = ToPoeItem(p, keepEvery: 2);
            item.InventoryId = inv.Inventory;
            item.X = inv.X;
            // Socket the build's gems for this slot, all linked, at a lower level.
            var gems = skills?.Groups.Where(g => g.Slot == slot).SelectMany(g => g.Gems).Take(Math.Max(1, p.MaxLinks)).ToList() ?? new();
            item.Sockets = Enumerable.Range(0, Math.Max(gems.Count, p.MaxLinks)).Select(_ => new PoeSocket { Group = 0 }).ToList();
            item.SocketedItems = gems.Select((g, i) => new PoeItem
            {
                TypeLine = g.IsSupport && !g.Name.EndsWith("Support") ? g.Name + " Support" : g.Name,
                FrameType = 4,
                Socket = i,
                Properties = new() { new PoeProperty { Name = "Level", Values = new() { JsonSerializer.SerializeToElement(new object[] { Math.Max(1, g.Level - 4).ToString(), 0 }) } } },
            }).ToList();
            equipped.Add(item);
        }
        var spec = build.Specs.FirstOrDefault();
        var passives = spec?.Nodes.Take(Math.Max(0, spec.Nodes.Count - 6)).ToHashSet() ?? new HashSet<int>();
        var jewels = spec?.Jewels.Values.Distinct().Take(1).Where(build.Items.ContainsKey).Select(id => ToPoeItem(build.Items[id], 1)).ToList() ?? new();
        return new CharacterGear("DemoWitch", Math.Max(1, build.Level - 10), build.ClassName, equipped, jewels, passives, DateTimeOffset.UtcNow, Demo: true);
    }

    private static PoeItem ToPoeItem(PobItem p, int keepEvery) => new()
    {
        Name = p.IsUnique || p.Rarity == "RARE" ? p.Name : "",
        TypeLine = p.BaseType,
        BaseType = p.BaseType,
        FrameType = p.Rarity switch { "NORMAL" => 0, "MAGIC" => 1, "RARE" => 2, _ => 3 },
        Ilvl = p.ItemLevel ?? 80,
        ImplicitMods = p.Implicits.Select(m => m.Text).ToList(),
        ExplicitMods = p.Explicits.Where((_, i) => i % keepEvery == 0).Select(m => m.Text).ToList(),
    };
}
