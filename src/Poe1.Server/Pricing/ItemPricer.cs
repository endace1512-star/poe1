using System.Text.RegularExpressions;
using Poe1.Poe;

namespace Poe1.Pricing;

/// <summary>A stash or inventory item with its unit price. Items with the same <see cref="Key"/> are aggregated.</summary>
public sealed record PricedItem(
    string Key,
    string Name,
    string Category,
    string? Icon,
    int FrameType,
    double Quantity,
    double UnitChaos)
{
    public double TotalChaos => Quantity * UnitChaos;
}

/// <summary>Matches items from the PoE API to poe.ninja price lines.</summary>
public sealed partial class ItemPricer
{
    private static readonly string[] InfluenceOrder = { "shaper", "elder", "crusader", "redeemer", "warlord", "hunter" };
    private const string ClusterGrantPrefix = "Added Small Passive Skills grant: ";

    private readonly PriceBook _book;
    private readonly int _minListings;

    public ItemPricer(PriceBook book, int minListingCount = 3)
    {
        _book = book;
        _minListings = minListingCount;
    }

    public PricedItem Price(PoeItem item)
    {
        var qty = Math.Max(1, item.StackSize ?? 1);
        var name = item.CleanName;
        var typeLine = item.CleanTypeLine;
        var baseType = item.CleanBaseType;

        switch (item.FrameType)
        {
            case 3: // unique
            case 9: // foil / relic
                return PriceUnique(item, name, baseType);
            case 4: // gem
                return PriceGem(item, typeLine);
        }

        // Everything traded on the currency exchange: currency, fragments, cards, essences, threads, …
        var ex = _book.Exchange(typeLine) ?? _book.Exchange(baseType);
        if (ex is not null)
            return new PricedItem($"ex:{ex.Name}", ex.Name, CategoryOf(ex), item.Icon ?? ex.Icon, item.FrameType, qty, ex.ChaosValue);

        if (item.PropertyInt("Map Tier") is { } tier)
            return PriceMap(item, typeLine, tier);

        if (baseType.EndsWith("Cluster Jewel") && item.FrameType is 0 or 1 && PriceCluster(item, baseType) is { } cluster)
            return cluster;

        // Named stash items: invitations, incubators, vials, wombgifts, beasts, …
        var named = Trusted(_book.ByName(typeLine)).Where(l => l.Type is not ("SkillGem" or "ClusterJewel")).MinBy(l => l.ChaosValue);
        if (named is not null)
            return new PricedItem($"named:{typeLine}", typeLine, CategoryOf(named), item.Icon ?? named.Icon, item.FrameType, qty, named.ChaosValue);

        if (item.FrameType is 0 or 1 or 2 && item.Ilvl >= 82 && PriceBase(item, baseType) is { } priced)
            return priced;

        var display = string.IsNullOrEmpty(name) ? typeLine : $"{name} {typeLine}";
        return new PricedItem($"item:{item.FrameType}:{typeLine}", display, FallbackCategory(item), item.Icon, item.FrameType, qty, 0);
    }

    private PricedItem PriceUnique(PoeItem item, string name, string baseType)
    {
        var isMap = item.PropertyInt("Map Tier") is not null;
        var candidates = Trusted(_book.ByName(name))
            .Where(l => isMap ? l.Type == "UniqueMap" : l.Type is not ("UniqueMap" or "ClusterJewel" or "BaseType"))
            .Where(l => l.BaseType is null || string.Equals(l.BaseType, baseType, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var links = item.MaxLinks >= 5 ? item.MaxLinks : 0;
        var linked = candidates.Where(l => (l.Links ?? 0) == links).ToList();
        if (linked.Count == 0) linked = candidates.Where(l => l.Links is null).ToList();
        // Several variants (e.g. different implicit rolls) – stay conservative and take the cheapest.
        var best = linked.Where(l => l.Variant is null).MinBy(l => l.ChaosValue) ?? linked.MinBy(l => l.ChaosValue);

        var display = links > 0 ? $"{name} ({links}L)" : name;
        return new PricedItem($"unique:{name}:{baseType}:{links}", display, isMap ? "map" : "unique",
            item.Icon ?? best?.Icon, item.FrameType, 1, best?.ChaosValue ?? 0);
    }

    private PricedItem PriceGem(PoeItem item, string gemName)
    {
        var level = item.PropertyInt("Level") ?? 1;
        var quality = item.PropertyInt("Quality") ?? 0;
        var corrupted = item.Corrupted;

        // Highest level and quality that the gem meets, with the same corrupted state.
        var best = Trusted(_book.ByName(gemName))
            .Where(l => l.Type == "SkillGem" && l.Corrupted == corrupted)
            .Where(l => (l.GemLevel ?? 1) <= level && (l.GemQuality ?? 0) <= quality)
            .OrderByDescending(l => l.GemLevel ?? 1)
            .ThenByDescending(l => l.GemQuality ?? 0)
            .FirstOrDefault();

        var suffix = quality > 0 ? $"{level}/{quality}" : $"{level}";
        if (corrupted) suffix += "c";
        return new PricedItem($"gem:{gemName}:{level}:{quality}:{(corrupted ? "c" : "")}", $"{gemName} ({suffix})", "gem",
            item.Icon ?? best?.Icon, item.FrameType, 1, best?.ChaosValue ?? 0);
    }

    private PricedItem PriceMap(PoeItem item, string typeLine, int tier)
    {
        NinjaLine? line = null;
        if (typeLine.StartsWith("Blighted ", StringComparison.Ordinal))
            line = Trusted(_book.ByName($"Blighted Map (Tier {tier})")).FirstOrDefault();
        else if (typeLine.StartsWith("Blight-ravaged ", StringComparison.Ordinal))
            line = Trusted(_book.ByName($"Blight-ravaged Map (Tier {tier})")).FirstOrDefault();
        line ??= Trusted(_book.ByName(typeLine)).Where(l => l.Type == "Map").MinBy(l => l.ChaosValue);

        return new PricedItem($"map:{typeLine}:{tier}", $"{typeLine} (T{tier})", "map", item.Icon ?? line?.Icon, item.FrameType,
            Math.Max(1, item.StackSize ?? 1), line?.ChaosValue ?? 0);
    }

    private PricedItem? PriceCluster(PoeItem item, string baseType)
    {
        var enchants = item.EnchantMods ?? new List<string>();
        var grant = enchants.FirstOrDefault(e => e.StartsWith(ClusterGrantPrefix, StringComparison.Ordinal));
        var countText = enchants.Select(e => PassiveCountRegex().Match(e)).FirstOrDefault(m => m.Success);
        if (grant is null || countText is null) return null;

        var grantText = grant[ClusterGrantPrefix.Length..].Split('\n')[0].Trim();
        var passives = int.Parse(countText.Groups[1].Value);
        var line = Trusted(_book.ByName(grantText))
            .Where(l => l.Type == "ClusterJewel" && string.Equals(l.BaseType, baseType, StringComparison.OrdinalIgnoreCase))
            .Where(l => l.Variant == $"{passives} passives" && (l.LevelRequired ?? 1) <= item.Ilvl)
            .MaxBy(l => l.LevelRequired ?? 1);
        if (line is null) return null;

        return new PricedItem($"cluster:{baseType}:{grantText}:{passives}:{line.LevelRequired}",
            $"{baseType}: {grantText} ({passives}p, ilvl{line.LevelRequired})", "cluster", item.Icon ?? line.Icon, item.FrameType, 1, line.ChaosValue);
    }

    private PricedItem? PriceBase(PoeItem item, string baseType)
    {
        var influence = InfluenceVariant(item);
        var ilvl = Math.Min(item.Ilvl, 86);
        var line = Trusted(_book.BaseType(baseType))
            .FirstOrDefault(l => l.LevelRequired == ilvl && l.Variant == influence);
        if (line is null) return null;

        var label = influence is null ? $"{baseType} (ilvl{ilvl})" : $"{baseType} ({influence}, ilvl{ilvl})";
        return new PricedItem($"base:{baseType}:{ilvl}:{influence}", label, "base", item.Icon ?? line.Icon, item.FrameType, 1, line.ChaosValue);
    }

    private static string? InfluenceVariant(PoeItem item)
    {
        if (item.Influences is not { Count: > 0 } inf) return null;
        var names = InfluenceOrder.Where(i => inf.TryGetValue(i, out var on) && on)
            .Select(i => char.ToUpperInvariant(i[0]) + i[1..]).ToList();
        return names.Count == 0 ? null : string.Join('/', names);
    }

    private IEnumerable<NinjaLine> Trusted(IEnumerable<NinjaLine> lines) =>
        lines.Where(l => l.Exchange || l.ListingCount >= _minListings);

    private static string CategoryOf(NinjaLine l) => l.Type switch
    {
        "Currency" => l.Category == "Catalysts" ? "catalyst" : "currency",
        "Fragment" or "AllflameEmber" or "Astrolabe" or "Invitation" => "fragment",
        "DivinationCard" => "card",
        "Essence" => "essence",
        "Fossil" or "Resonator" => "delve",
        "Thread" or "Scarab" => "scarab",
        "Map" or "BlightedMap" or "BlightRavagedMap" or "UniqueMap" => "map",
        "Beast" => "beast",
        _ => "other",
    };

    private static string FallbackCategory(PoeItem item) => item.FrameType switch
    {
        3 or 9 => "unique",
        4 => "gem",
        5 => "currency",
        6 => "card",
        _ => item.PropertyInt("Map Tier") is not null ? "map" : "other",
    };

    [GeneratedRegex(@"^Adds (\d+) Passive Skills")]
    private static partial Regex PassiveCountRegex();
}
