using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Poe1.Builds;
using Poe1.Poe;

namespace Poe1.Tests;

public class PobParserTests
{
    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    private static string Encode(string xml)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal)) z.Write(Encoding.UTF8.GetBytes(xml));
        return Convert.ToBase64String(ms.ToArray()).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }

    [Fact]
    public void Parses_a_real_pob_export()
    {
        var b = PobParser.Parse(Fixture("pob-occvortex.xml"));
        Assert.Equal("Witch", b.ClassName);
        Assert.Equal("Occultist", b.Ascendancy);
        Assert.Equal(99, b.Level);

        var spec = Assert.Single(b.Specs);
        Assert.True(spec.Nodes.Count > 100);
        Assert.Equal(17, spec.Jewels[36634]);

        var set = Assert.Single(b.ItemSets);
        Assert.Equal(11, set.Slots["Helmet"]);
        Assert.DoesNotContain(set.Slots.Keys, k => k.Contains("Abyssal"));

        var wand = b.Items[3];
        Assert.Equal("RARE", wand.Rarity);
        Assert.Equal("Maelstrom Cry", wand.Name);
        Assert.Equal("Imbued Wand", wand.BaseType);
        Assert.Equal("B-B-B", wand.Sockets);
        Assert.Equal("37% increased Spell Damage", Assert.Single(wand.Implicits).Text);
        var crafted = wand.Explicits.Single(m => m.Crafted);
        Assert.Equal("+19% to Cold Damage over Time Multiplier", crafted.Text);

        var flask = b.Items[12];
        Assert.Equal("MAGIC", flask.Rarity);
        Assert.Equal("Perpetual Quicksilver Flask of Staunching", flask.Name);

        var gems = b.SkillSets.SelectMany(s => s.Groups).SelectMany(g => g.Gems).ToList();
        Assert.Contains(gems, g => g is { Name: "Flame Dash", Level: 20, IsSupport: false });
        Assert.Contains(gems, g => g is { Name: "Increased Area of Effect", IsSupport: true });
    }

    [Fact]
    public void Decodes_export_codes_and_links()
    {
        const string xml = "<PathOfBuilding><Build level=\"5\" className=\"Ranger\" ascendClassName=\"None\"/></PathOfBuilding>";
        Assert.Equal(xml, PobParser.DecodeCode(Encode(xml)));
        Assert.Throws<FormatException>(() => PobParser.DecodeCode("not a code!!"));

        Assert.Equal("https://pobb.in/AbC_12-x/raw", PobParser.RawUrlFor("https://pobb.in/AbC_12-x")!.ToString());
        Assert.Equal("https://pobb.in/xyz/raw", PobParser.RawUrlFor("pobb.in/u/someone/xyz")!.ToString());
        Assert.Equal("https://pastebin.com/raw/aB12", PobParser.RawUrlFor("https://pastebin.com/aB12")!.ToString());
        Assert.Equal("https://poe.ninja/pob/raw/1a2b", PobParser.RawUrlFor("https://poe.ninja/pob/1a2b")!.ToString());
        Assert.Null(PobParser.RawUrlFor(Encode(xml)));
        Assert.Null(PobParser.RawUrlFor("https://example.com/build"));
    }

    [Fact]
    public void Item_text_applies_ranges_variants_and_tags()
    {
        var item = PobItemParser.Parse("""
            Rarity: UNIQUE
            Watcher's Eye
            Prismatic Jewel
            Variant: Pre 3.10
            Variant: Current
            Selected Variant: 2
            Implicits: 0
            {range:0.5}(4-6)% increased maximum Energy Shield
            {variant:1}{range:1}(10-15)% increased maximum Life
            {variant:2}{range:0}(4-6)% increased maximum Life
            """);
        Assert.True(item.IsUnique);
        Assert.Equal("Prismatic Jewel", item.BaseType);
        Assert.Equal(new[] { "5% increased maximum Energy Shield", "4% increased maximum Life" }, item.Explicits.Select(m => m.Text));

        var rare = PobItemParser.Parse("""
            Rarity: RARE
            Storm Grip
            Spiked Gloves
            Hunter Item
            Item Level: 86
            Sockets: R-G-B-B
            Implicits: 1
            {enchant}Commandment of Spite
            {range:1}+(80-89) to maximum Life
            +38% to Fire Resistance (crafted)
            {fractured}{range:0.33}(1.5-2.5)% of Physical Attack Damage Leeched as Life
            Corrupted
            """);
        Assert.Equal(new[] { "Hunter" }, rare.Influences);
        Assert.True(rare.Corrupted);
        Assert.Equal(4, rare.MaxLinks);
        Assert.True(Assert.Single(rare.Implicits).Enchant);
        Assert.Equal("+89 to maximum Life", rare.Explicits[0].Text);
        Assert.True(rare.Explicits[1].Crafted);
        Assert.Equal("+38% to Fire Resistance", rare.Explicits[1].Text);
        Assert.True(rare.Explicits[2].Fractured);
        Assert.Equal("1.83% of Physical Attack Damage Leeched as Life", rare.Explicits[2].Text);
    }

    [Theory]
    [InlineData("Level 70", 70)]
    [InlineData("Lvl 30-60 leveling", 30)]
    [InlineData("90 Red maps", 90)]
    [InlineData("Endgame", null)]
    [InlineData("Act 5 (lv.45)", 45)]
    public void Reads_level_hints_from_titles(string title, int? level) => Assert.Equal(level, BuildComparer.LevelHint(title));
}

public class ModMatchingTests
{
    [Fact]
    public void Matches_by_wording_and_compares_values()
    {
        var r = ModMatching.Compare(
            new[] { "+90 to maximum Life", "+40% to Fire Resistance", "Adds 10 to 20 Physical Damage to Attacks", "Cannot be Frozen" },
            new[] { "+70 to maximum Life", "Adds 12 to 20 Physical Damage to Attacks", "+20% to Cold Resistance" });
        Assert.Equal(ModMatching.ModState.Low, r[0].State);
        Assert.Equal("+70 to maximum Life", r[0].Current);
        Assert.Equal(ModMatching.ModState.Missing, r[1].State);
        Assert.Equal(ModMatching.ModState.Ok, r[2].State);
        Assert.Equal(ModMatching.ModState.Missing, r[3].State);
    }

    [Fact]
    public void Pseudo_stats_add_up_hybrid_mods()
    {
        var s = ModMatching.PseudoStats(new[]
        {
            "+50 to maximum Life", "+12% to all Elemental Resistances", "+20% to Fire and Cold Resistances",
            "+15% to Cold and Chaos Resistances", "+10 to all Attributes", "+20 to Strength and Dexterity", "30% increased Movement Speed",
        });
        Assert.Equal(50, s["Life"]);
        Assert.Equal(32, s["Fire Res"]);
        Assert.Equal(47, s["Cold Res"]);
        Assert.Equal(12, s["Lightning Res"]);
        Assert.Equal(15, s["Chaos Res"]);
        Assert.Equal(30, s["Str"]);
        Assert.Equal(10, s["Int"]);
        Assert.Equal(30, s["Move Speed"]);
    }
}

public class BuildComparerTests
{
    private static PobItem Rare(int id, string name, string baseType, params string[] mods)
    {
        var item = new PobItem { Id = id, Rarity = "RARE", Name = name, BaseType = baseType };
        item.Explicits.AddRange(mods.Select(m => new PobMod(m, false, false, false)));
        return item;
    }

    private static PobItem Unique(int id, string name, string baseType) => new() { Id = id, Rarity = "UNIQUE", Name = name, BaseType = baseType };

    private static PoeItem Have(string inventory, string name, string baseType, int frame, params string[] mods) => new()
    {
        InventoryId = inventory, Name = name, TypeLine = baseType, BaseType = baseType, FrameType = frame, ExplicitMods = mods.ToList(),
    };

    private static JsonElement Level(int level) => JsonSerializer.SerializeToElement(new object[] { level.ToString(), 0 });

    private static PoeItem Gem(string name, int socket, int level) => new()
    {
        TypeLine = name, FrameType = 4, Socket = socket,
        Properties = new() { new PoeProperty { Name = "Level", Values = new() { Level(level) } } },
    };

    private static (PobBuild Build, Dictionary<int, PassiveNode> Tree) Build()
    {
        var b = new PobBuild { ClassName = "Witch", Ascendancy = "Necromancer", Level = 90 };
        b.Items[1] = Unique(1, "Goldrim", "Leather Cap");
        b.Items[2] = Rare(2, "Doom Grip", "Titan Gauntlets", "+90 to maximum Life", "+40% to Fire Resistance", "+40% to Cold Resistance");
        b.Items[3] = Unique(3, "Tabula Rasa", "Simple Robe");
        b.Items[4] = Rare(4, "Ring A", "Two-Stone Ring", "+60 to maximum Life", "+40% to Lightning Resistance");
        b.Items[5] = Unique(5, "Praxis", "Paua Ring");
        b.Items[6] = Rare(6, "Cobalt", "Cobalt Jewel", "7% increased maximum Life");

        var early = new PobItemSet { Id = "1", Title = "Leveling" };
        early.Slots["Helmet"] = 1;
        early.Slots["Body Armour"] = 3;
        var end = new PobItemSet { Id = "2", Title = "Endgame" };
        end.Slots["Gloves"] = 2;
        end.Slots["Ring 1"] = 4;
        end.Slots["Ring 2"] = 5;
        end.Slots["Body Armour"] = 3;
        b.ItemSets.AddRange(new[] { early, end });

        var s1 = new PobTreeSpec { Title = "Level 30" };
        s1.Nodes.UnionWith(new[] { 1, 2, 3, 4 });
        var s2 = new PobTreeSpec { Title = "Level 60" };
        s2.Nodes.UnionWith(new[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        s2.Jewels[100] = 6;
        var s3 = new PobTreeSpec { Title = "Level 90" };
        s3.Nodes.UnionWith(Enumerable.Range(1, 16));
        b.Specs.AddRange(new[] { s1, s2, s3 });

        var skills = new PobSkillSet { Id = "1", Title = "Main" };
        var main = new PobSkillGroup { Slot = "Body Armour", Label = "Main skill" };
        main.Gems.Add(new PobGem("Raise Spectre", null, 20, 20, false));
        main.Gems.Add(new PobGem("Minion Damage", null, 20, 0, true));
        main.Gems.Add(new PobGem("Spell Echo", null, 20, 0, true));
        skills.Groups.Add(main);
        var aura = new PobSkillGroup { Slot = "Helmet" };
        aura.Gems.Add(new PobGem("Hatred", null, 20, 0, false));
        skills.Groups.Add(aura);
        b.SkillSets.Add(skills);

        var tree = new Dictionary<int, PassiveNode>
        {
            [7] = new(7, "Elemental Equilibrium", "keystone", null),
            [8] = new(8, "Heart of Thunder", "notable", null),
            [16] = new(16, "Bone Barrier", "ascendancy-notable", "Necromancer"),
            [99] = new(99, "Witch start", "start", null),
        };
        return (b, tree);
    }

    private static CharacterGear Gear(params PoeItem[] items) =>
        new("Me", 62, "Necromancer", items, new List<PoeItem>(), new HashSet<int> { 1, 2, 3, 4, 5, 6, 8, 99 }, DateTimeOffset.UtcNow);

    [Fact]
    public void Detects_stage_from_tree_and_lists_what_is_missing()
    {
        var (build, tree) = Build();
        var body = Have("BodyArmour", "Tabula Rasa", "Simple Robe", 3);
        body.Sockets = Enumerable.Range(0, 6).Select(i => new PoeSocket { Group = i < 3 ? 0 : 1 }).ToList();
        body.SocketedItems = new() { Gem("Raise Spectre", 0, 19), Gem("Minion Damage Support", 1, 20), Gem("Hatred", 3, 20) };
        var gear = Gear(
            body,
            Have("Gloves", "Storm Grip", "Titan Gauntlets", 2, "+70 to maximum Life", "+42% to Fire Resistance"),
            // Rings worn the "wrong" way round: Praxis in the left slot.
            Have("Ring", "Praxis", "Paua Ring", 3),
            Have("Ring2", "Gale Coil", "Two-Stone Ring", 2, "+65 to maximum Life", "+41% to Lightning Resistance"));

        var report = BuildComparer.Compare(build, gear, tree, null, null, null, new Dictionary<string, string>());

        Assert.Equal(1, report.Stage.DetectedSpec); // "Level 60": 7 of 8 nodes
        Assert.Equal(1, report.Stage.ItemSet);     // three sets of specs vs two item sets: best gear match
        Assert.Equal(7, report.Tree.Matched);
        Assert.Equal(new[] { "Elemental Equilibrium (keystone)" }, report.Tree.MissingNotables);
        Assert.Empty(report.Tree.ExtraNotables); // Heart of Thunder is in both, the start node doesn't count
        Assert.Equal(0, report.Tree.ExtraOther);

        var slots = report.Slots.ToDictionary(s => s.Slot);
        Assert.Equal("ok", slots["Body Armour"].Status);
        Assert.Equal("ok", slots["Ring 2"].Status);  // Praxis found in the other ring slot
        Assert.Equal("ok", slots["Ring 1"].Status);  // rare ring paired with the matching rare
        Assert.Equal("partial", slots["Gloves"].Status);
        Assert.Contains(slots["Gloves"].Mods, m => m.State == "low" && m.Current == "+70 to maximum Life");
        Assert.Contains(slots["Gloves"].Mods, m => m.State == "missing" && m.Text == "+40% to Cold Resistance");
        Assert.Contains(slots["Gloves"].Stats, s => s.Stat == "Cold Res" && s.Build == 40 && s.Current == 0);

        var main = report.Gems.Single(g => g.Label == "Main skill");
        Assert.Equal("partial", main.Status);
        Assert.Equal("ok", main.Gems.Single(g => g.Name == "Raise Spectre").State);
        Assert.Equal("missing", main.Gems.Single(g => g.Name == "Spell Echo").State);
        Assert.True(main.Linked);
        var aura = report.Gems.Single(g => g.Label == "Hatred");
        Assert.Equal("elsewhere", aura.Gems.Single().State); // in the body armour, build wants it in the helmet

        var jewel = Assert.Single(report.Jewels);
        Assert.Equal("empty", jewel.Status); // no jewels socketed at all
    }

    [Fact]
    public void Ignored_items_leave_the_summary()
    {
        var (build, tree) = Build();
        var gear = Gear();
        var all = BuildComparer.Compare(build, gear, tree, 2, 1, 0, new Dictionary<string, string>());
        var gloves = all.Slots.Single(s => s.Slot == "Gloves");
        Assert.Equal("empty", gloves.Status);

        var ignored = BuildComparer.Compare(build, gear, tree, 2, 1, 0, new Dictionary<string, string> { [gloves.Key] = "similar" });
        Assert.Equal("similar", ignored.Slots.Single(s => s.Slot == "Gloves").Ignored);
        Assert.Equal(all.Summary.Missing - 1, ignored.Summary.Missing);
        Assert.Equal(1, ignored.Summary.Ignored);
        Assert.Equal(2, ignored.Stage.Spec); // explicit choice wins over detection
    }

    [Fact]
    public void Without_passives_the_stage_follows_character_level()
    {
        var (build, tree) = Build();
        var gear = new CharacterGear("Me", 64, null, new List<PoeItem>(), new List<PoeItem>(), new HashSet<int>(), DateTimeOffset.UtcNow);
        Assert.Equal(1, BuildComparer.DetectStage(build, gear, tree, null, null, null).DetectedSpec);
    }

    [Fact]
    public void Demo_gear_is_built_from_the_first_item_set()
    {
        var build = PobParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pob-occvortex.xml")));
        var gear = DemoGear.From(build);
        Assert.NotEmpty(gear.Equipped);
        Assert.DoesNotContain(gear.Equipped, i => i.InventoryId == "Gloves");
        var report = BuildComparer.Compare(build, gear, new Dictionary<int, PassiveNode>(), null, null, null, new Dictionary<string, string>());
        Assert.Contains(report.Slots, s => s.Status == "ok");
        Assert.Contains(report.Slots, s => s.Status == "empty");
        Assert.True(report.Tree.Matched > 0 && report.Tree.Matched < report.Tree.Target);
    }
}
