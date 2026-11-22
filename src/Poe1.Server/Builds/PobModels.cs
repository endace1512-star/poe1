namespace Poe1.Builds;

/// <summary>A Path of Building build, read from its XML export.</summary>
public sealed class PobBuild
{
    public string ClassName { get; set; } = "";
    public string? Ascendancy { get; set; }
    public int Level { get; set; }
    public List<PobTreeSpec> Specs { get; } = new();
    public int ActiveSpecIndex { get; set; }
    public Dictionary<int, PobItem> Items { get; } = new();
    public List<PobItemSet> ItemSets { get; } = new();
    public int ActiveItemSetIndex { get; set; }
    public List<PobSkillSet> SkillSets { get; } = new();
    public int ActiveSkillSetIndex { get; set; }
}

public sealed class PobTreeSpec
{
    public string Title { get; set; } = "";
    public string? TreeVersion { get; set; }
    public HashSet<int> Nodes { get; } = new();
    /// <summary>Passive jewel socket node id → item id.</summary>
    public Dictionary<int, int> Jewels { get; } = new();
    public string? Url { get; set; }
}

public sealed class PobItemSet
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>PoB slot name ("Helmet", "Ring 1", "Flask 3", …) → item id.</summary>
    public Dictionary<string, int> Slots { get; } = new();
}

public sealed class PobSkillSet
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<PobSkillGroup> Groups { get; } = new();
}

public sealed class PobSkillGroup
{
    public string? Slot { get; set; }
    public string? Label { get; set; }
    public List<PobGem> Gems { get; } = new();
}

public sealed record PobGem(string Name, string? GemId, int Level, int Quality, bool IsSupport);

public sealed class PobItem
{
    public int Id { get; set; }
    /// <summary>NORMAL, MAGIC, RARE, UNIQUE (RELIC is treated as UNIQUE).</summary>
    public string Rarity { get; set; } = "RARE";
    public string Name { get; set; } = "";
    /// <summary>Base type for rares and uniques. For magic/normal items this is the full first line.</summary>
    public string BaseType { get; set; } = "";
    public int? ItemLevel { get; set; }
    public int? Quality { get; set; }
    public string? Sockets { get; set; }
    public bool Corrupted { get; set; }
    public List<string> Influences { get; } = new();
    public List<PobMod> Implicits { get; } = new();
    public List<PobMod> Explicits { get; } = new();
    public string RawText { get; set; } = "";

    public bool IsUnique => Rarity is "UNIQUE" or "RELIC";

    /// <summary>Largest linked group in "R-G-B B-B" style socket text.</summary>
    public int MaxLinks => string.IsNullOrEmpty(Sockets) ? 0 : Sockets.Split(' ').Max(g => g.Split('-').Length);

    public string DisplayName => IsUnique || Rarity == "RARE" ? (Name == BaseType ? Name : $"{Name}, {BaseType}") : Name;
}

public sealed record PobMod(string Text, bool Crafted, bool Fractured, bool Enchant);
