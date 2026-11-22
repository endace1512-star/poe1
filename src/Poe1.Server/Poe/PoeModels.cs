using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Poe1.Poe;

/// <summary>An item as returned by the pathofexile.com character-window / stash APIs.</summary>
public sealed partial class PoeItem
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("typeLine")] public string TypeLine { get; set; } = "";
    [JsonPropertyName("baseType")] public string? BaseType { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }
    [JsonPropertyName("frameType")] public int FrameType { get; set; }
    [JsonPropertyName("stackSize")] public int? StackSize { get; set; }
    [JsonPropertyName("ilvl")] public int Ilvl { get; set; }
    [JsonPropertyName("identified")] public bool Identified { get; set; } = true;
    [JsonPropertyName("corrupted")] public bool Corrupted { get; set; }
    [JsonPropertyName("synthesised")] public bool Synthesised { get; set; }
    [JsonPropertyName("fractured")] public bool Fractured { get; set; }
    [JsonPropertyName("inventoryId")] public string? InventoryId { get; set; }
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("note")] public string? Note { get; set; }
    [JsonPropertyName("properties")] public List<PoeProperty>? Properties { get; set; }
    [JsonPropertyName("sockets")] public List<PoeSocket>? Sockets { get; set; }
    [JsonPropertyName("socketedItems")] public List<PoeItem>? SocketedItems { get; set; }
    [JsonPropertyName("influences")] public Dictionary<string, bool>? Influences { get; set; }
    [JsonPropertyName("enchantMods")] public List<string>? EnchantMods { get; set; }
    [JsonPropertyName("explicitMods")] public List<string>? ExplicitMods { get; set; }
    [JsonPropertyName("implicitMods")] public List<string>? ImplicitMods { get; set; }
    [JsonPropertyName("craftedMods")] public List<string>? CraftedMods { get; set; }
    [JsonPropertyName("fracturedMods")] public List<string>? FracturedMods { get; set; }
    /// <summary>For an item socketed in another item: index into the parent's <see cref="Sockets"/>.</summary>
    [JsonPropertyName("socket")] public int? Socket { get; set; }
    [JsonPropertyName("support")] public bool? Support { get; set; }

    /// <summary>Every mod line on the item (implicit, enchant, explicit, crafted, fractured).</summary>
    public IEnumerable<string> AllMods =>
        (ImplicitMods ?? new()).Concat(EnchantMods ?? new()).Concat(FracturedMods ?? new())
            .Concat(ExplicitMods ?? new()).Concat(CraftedMods ?? new());

    [GeneratedRegex("<<[^>]*>>")]
    private static partial Regex MarkupRegex();

    /// <summary>Name without the legacy &lt;&lt;set:MS&gt;&gt; markup.</summary>
    public string CleanName => MarkupRegex().Replace(Name ?? "", "").Trim();
    public string CleanTypeLine => MarkupRegex().Replace(TypeLine ?? "", "").Trim();
    public string CleanBaseType => string.IsNullOrEmpty(BaseType) ? CleanTypeLine : MarkupRegex().Replace(BaseType, "").Trim();

    /// <summary>Text of the first value of a property, e.g. "Level" → "21 (Max)".</summary>
    public string? Property(string name)
    {
        var p = Properties?.FirstOrDefault(p => p.Name == name);
        if (p?.Values is not { Count: > 0 } values) return null;
        var first = values[0];
        return first.ValueKind == JsonValueKind.Array && first.GetArrayLength() > 0 ? first[0].GetString() : null;
    }

    public int? PropertyInt(string name)
    {
        var text = Property(name);
        if (text is null) return null;
        var m = Regex.Match(text, @"-?\d+");
        return m.Success ? int.Parse(m.Value) : null;
    }

    /// <summary>Size of the largest linked socket group.</summary>
    public int MaxLinks => Sockets is { Count: > 0 } s ? s.GroupBy(x => x.Group).Max(g => g.Count()) : 0;
}

public sealed class PoeProperty
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    /// <summary>Each value is [text, displayMode].</summary>
    [JsonPropertyName("values")] public List<JsonElement>? Values { get; set; }
}

public sealed class PoeSocket
{
    [JsonPropertyName("group")] public int Group { get; set; }
    [JsonPropertyName("sColour")] public string? Colour { get; set; }
}

public sealed class StashTabInfo
{
    [JsonPropertyName("n")] public string Name { get; set; } = "";
    [JsonPropertyName("i")] public int Index { get; set; }
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("hidden")] public bool Hidden { get; set; }
    [JsonPropertyName("colour")] public TabColour? Colour { get; set; }
    [JsonPropertyName("children")] public List<StashTabInfo>? Children { get; set; }
}

public sealed class TabColour
{
    [JsonPropertyName("r")] public int R { get; set; }
    [JsonPropertyName("g")] public int G { get; set; }
    [JsonPropertyName("b")] public int B { get; set; }
    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";
}

public sealed class StashResponse
{
    [JsonPropertyName("numTabs")] public int NumTabs { get; set; }
    [JsonPropertyName("tabs")] public List<StashTabInfo>? Tabs { get; set; }
    [JsonPropertyName("items")] public List<PoeItem> Items { get; set; } = new();
}

public sealed class CharacterInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("league")] public string? League { get; set; }
    [JsonPropertyName("class")] public string? Class { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
}

public sealed class CharacterItemsResponse
{
    [JsonPropertyName("items")] public List<PoeItem> Items { get; set; } = new();
}

public sealed class PassiveSkillsResponse
{
    /// <summary>Allocated passive node ids.</summary>
    [JsonPropertyName("hashes")] public List<int> Hashes { get; set; } = new();
    /// <summary>Jewels socketed in the passive tree.</summary>
    [JsonPropertyName("items")] public List<PoeItem> Items { get; set; } = new();
}
