using System.Text.RegularExpressions;
using Poe1.Poe;

namespace Poe1.Builds;

public sealed record StageOption(int Index, string Title, double Match, int? LevelHint);

public sealed record StageInfo(
    List<StageOption> Specs, List<StageOption> ItemSets, List<StageOption> SkillSets,
    int DetectedSpec, int DetectedItemSet, int DetectedSkillSet,
    int Spec, int ItemSet, int SkillSet);

public sealed record ItemView(string Name, string BaseType, string Rarity, int Links, string? Sockets, List<string> Implicits, List<string> Explicits, string? Icon);

public sealed record ModLine(string Text, string State, string? Current);

public sealed record StatLine(string Stat, double Build, double Current);

public sealed record SlotReport(
    string Key, string Slot, string Status, double Score, string? Ignored,
    ItemView Build, ItemView? Current, List<ModLine> Mods, List<StatLine> Stats, List<string> Notes);

public sealed record GemLine(string Name, bool Support, int BuildLevel, int BuildQuality, string State, int? CurrentLevel, string? Where);

public sealed record GemGroupReport(string Key, string Label, string? Slot, string Status, string? Ignored, bool Linked, List<GemLine> Gems);

public sealed record TreeReport(int Allocated, int Target, int Matched, List<string> MissingNotables, int MissingOther, List<string> ExtraNotables, int ExtraOther, string? Url);

public sealed record BuildSummary(int Ok, int Partial, int Missing, int Ignored, int GemGroupsOk, int GemGroupsTotal, double Score);

public sealed record CharacterView(string Name, int? Level, string? Class, string FetchedAt, bool Demo);

public sealed record BuildReport(
    CharacterView Character, string BuildClass, string? BuildAscendancy, int BuildLevel,
    StageInfo Stage, BuildSummary Summary, List<SlotReport> Slots, List<GemGroupReport> Gems, List<SlotReport> Jewels, TreeReport Tree);

/// <summary>Compares a PoB build with what a character has: which stage they are at and what is still missing.</summary>
public static partial class BuildComparer
{
    /// <summary>PoB slot → pathofexile.com inventoryId (+ flask position).</summary>
    private static readonly Dictionary<string, (string Inventory, int? X)> SlotMap = new()
    {
        ["Weapon 1"] = ("Weapon", null), ["Weapon 2"] = ("Offhand", null),
        ["Weapon 1 Swap"] = ("Weapon2", null), ["Weapon 2 Swap"] = ("Offhand2", null),
        ["Helmet"] = ("Helm", null), ["Body Armour"] = ("BodyArmour", null), ["Gloves"] = ("Gloves", null),
        ["Boots"] = ("Boots", null), ["Amulet"] = ("Amulet", null), ["Belt"] = ("Belt", null),
        ["Ring 1"] = ("Ring", null), ["Ring 2"] = ("Ring2", null), ["Ring 3"] = ("Ring3", null),
        ["Flask 1"] = ("Flask", 0), ["Flask 2"] = ("Flask", 1), ["Flask 3"] = ("Flask", 2), ["Flask 4"] = ("Flask", 3), ["Flask 5"] = ("Flask", 4),
    };

    private static readonly string[] SlotOrder =
    {
        "Weapon 1", "Weapon 2", "Helmet", "Body Armour", "Gloves", "Boots", "Amulet", "Ring 1", "Ring 2", "Ring 3", "Belt",
        "Flask 1", "Flask 2", "Flask 3", "Flask 4", "Flask 5", "Weapon 1 Swap", "Weapon 2 Swap",
    };

    public static BuildReport Compare(
        PobBuild build, CharacterGear gear, IReadOnlyDictionary<int, PassiveNode> tree,
        int? spec, int? itemSet, int? skillSet, IReadOnlyDictionary<string, string> ignores)
    {
        var stage = DetectStage(build, gear, tree, spec, itemSet, skillSet);

        var slots = build.ItemSets.Count > 0 ? CompareSlots(build, build.ItemSets[stage.ItemSet], gear, ignores) : new();
        var gems = build.SkillSets.Count > 0 ? CompareGems(build.SkillSets[stage.SkillSet], gear, ignores) : new();
        var jewels = build.Specs.Count > 0 ? CompareJewels(build, build.Specs[stage.Spec], gear, ignores) : new();
        var treeReport = build.Specs.Count > 0 ? CompareTree(build.Specs[stage.Spec], gear, tree) : new TreeReport(gear.Passives.Count, 0, 0, new(), 0, new(), 0, null);

        var counted = slots.Concat(jewels).Where(s => s.Ignored is null).ToList();
        var gemCounted = gems.Where(g => g.Ignored is null).ToList();
        var parts = counted.Select(s => s.Score).Concat(gemCounted.Select(g => g.Status == "ok" ? 1.0 : g.Status == "partial" ? 0.5 : 0)).ToList();
        if (treeReport.Target > 0) parts.Add((double)treeReport.Matched / treeReport.Target);
        var summary = new BuildSummary(
            counted.Count(s => s.Status == "ok"),
            counted.Count(s => s.Status == "partial"),
            counted.Count(s => s.Status is "missing" or "empty"),
            slots.Concat(jewels).Count(s => s.Ignored is not null) + gems.Count(g => g.Ignored is not null),
            gemCounted.Count(g => g.Status == "ok"), gemCounted.Count,
            parts.Count == 0 ? 0 : parts.Average());

        return new BuildReport(
            new CharacterView(gear.Character, gear.Level, gear.Class, gear.FetchedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ"), gear.Demo),
            build.ClassName, build.Ascendancy, build.Level, stage, summary, slots, gems, jewels, treeReport);
    }

    // ---------------------------------------------------------------- stage

    public static StageInfo DetectStage(PobBuild build, CharacterGear gear, IReadOnlyDictionary<int, PassiveNode> tree, int? spec, int? itemSet, int? skillSet)
    {
        var have = Countable(gear.Passives, tree);
        var specs = build.Specs.Select((s, i) =>
        {
            var want = Countable(s.Nodes, tree);
            var union = want.Union(have).Count();
            return new StageOption(i, s.Title, union == 0 ? 0 : (double)want.Intersect(have).Count() / union, LevelHint(s.Title));
        }).ToList();

        int detectedSpec;
        if (have.Count > 0 && specs.Count > 0)
            detectedSpec = specs.OrderByDescending(s => s.Match).ThenBy(s => s.Index).First().Index;
        else
            detectedSpec = ByLevel(specs, gear.Level) ?? build.ActiveSpecIndex;

        var itemSets = build.ItemSets.Select((set, i) =>
        {
            var scores = CompareSlots(build, set, gear, new Dictionary<string, string>()).Select(r => r.Score).ToList();
            return new StageOption(i, set.Title, scores.Count == 0 ? 0 : scores.Average(), LevelHint(set.Title));
        }).ToList();
        var skillSets = build.SkillSets.Select((set, i) =>
        {
            var groups = CompareGems(set, gear, new Dictionary<string, string>());
            var gems = groups.SelectMany(g => g.Gems).ToList();
            return new StageOption(i, set.Title, gems.Count == 0 ? 0 : (double)gems.Count(g => g.State != "missing") / gems.Count, LevelHint(set.Title));
        }).ToList();

        // Guides usually keep tree, item and skill sets in step; follow the tree when the counts line up.
        int Pick(List<StageOption> options, int fallback)
        {
            if (options.Count == 0) return 0;
            if (options.Count == specs.Count && specs.Count > 1) return detectedSpec;
            var best = options.OrderByDescending(o => o.Match).ThenBy(o => Math.Abs(o.Index - detectedSpec)).First();
            return best.Match > 0 ? best.Index : Math.Clamp(fallback, 0, options.Count - 1);
        }

        var detectedItem = Pick(itemSets, build.ActiveItemSetIndex);
        var detectedSkill = Pick(skillSets, build.ActiveSkillSetIndex);
        return new StageInfo(specs, itemSets, skillSets, detectedSpec, detectedItem, detectedSkill,
            Clamp(spec, specs.Count, detectedSpec), Clamp(itemSet, itemSets.Count, detectedItem), Clamp(skillSet, skillSets.Count, detectedSkill));
    }

    private static int Clamp(int? chosen, int count, int fallback) =>
        count == 0 ? 0 : chosen is { } c && c >= 0 && c < count ? c : Math.Clamp(fallback, 0, count - 1);

    private static int? ByLevel(List<StageOption> specs, int? level)
    {
        if (level is null) return null;
        var withHints = specs.Where(s => s.LevelHint is not null).ToList();
        if (withHints.Count == 0) return null;
        return (withHints.Where(s => s.LevelHint <= level).MaxBy(s => s.LevelHint) ?? withHints.MinBy(s => s.LevelHint))!.Index;
    }

    [GeneratedRegex(@"(?:lv\.?|lvl\.?|level|cấp)\s*(\d{1,3})|^\s*(\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex LevelRegex();

    internal static int? LevelHint(string title)
    {
        var m = LevelRegex().Match(title);
        if (!m.Success) return null;
        var v = int.Parse(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        return v is >= 1 and <= 100 ? v : null;
    }

    /// <summary>Allocated nodes that matter for "how close is the tree": class/ascendancy starts are free.</summary>
    private static HashSet<int> Countable(IEnumerable<int> nodes, IReadOnlyDictionary<int, PassiveNode> tree) =>
        nodes.Where(n => !(tree.TryGetValue(n, out var info) && info.Kind == "start")).ToHashSet();

    // ---------------------------------------------------------------- items

    private static List<SlotReport> CompareSlots(PobBuild build, PobItemSet set, CharacterGear gear, IReadOnlyDictionary<string, string> ignores)
    {
        var wanted = set.Slots
            .Where(s => SlotMap.ContainsKey(s.Key) && build.Items.ContainsKey(s.Value))
            .OrderBy(s => Array.IndexOf(SlotOrder, s.Key))
            .ToList();

        PoeItem? Current(string slot)
        {
            var (inv, x) = SlotMap[slot];
            return gear.Equipped.FirstOrDefault(i => i.InventoryId == inv && (x is null || i.X == x));
        }

        var assigned = wanted.ToDictionary(w => w.Key, w => Current(w.Key));

        // Rings and flasks can sit in any of their slots: pair them to the best match.
        Repair(wanted.Where(w => w.Key.StartsWith("Ring ")).ToList(), assigned, build, gear, "Ring", "Ring2", "Ring3");
        Repair(wanted.Where(w => w.Key.StartsWith("Flask ")).ToList(), assigned, build, gear, "Flask");

        return wanted.Select(w =>
        {
            var key = $"slot:{w.Key}:{w.Value}";
            return CompareItem(key, w.Key, build.Items[w.Value], assigned[w.Key], ignores.GetValueOrDefault(key), isJewel: false);
        }).ToList();
    }

    private static void Repair(List<KeyValuePair<string, int>> wanted, Dictionary<string, PoeItem?> assigned, PobBuild build, CharacterGear gear, params string[] inventories)
    {
        if (wanted.Count < 2) return;
        var pool = gear.Equipped.Where(i => inventories.Contains(i.InventoryId)).ToList();
        var order = wanted.OrderByDescending(w => pool.Max(p => (double?)CompareItem("", w.Key, build.Items[w.Value], p, null, false).Score) ?? 0).ToList();
        foreach (var w in order)
        {
            var best = pool.MaxBy(p => CompareItem("", w.Key, build.Items[w.Value], p, null, false).Score);
            assigned[w.Key] = best;
            if (best is not null) pool.Remove(best);
        }
    }

    private static List<SlotReport> CompareJewels(PobBuild build, PobTreeSpec spec, CharacterGear gear, IReadOnlyDictionary<string, string> ignores)
    {
        var pool = gear.Jewels.ToList();
        var result = new List<SlotReport>();
        var wanted = spec.Jewels.Values.Distinct().Where(build.Items.ContainsKey).Select(id => build.Items[id])
            .OrderByDescending(i => i.IsUnique).ToList();
        foreach (var jewel in wanted)
        {
            var best = pool.MaxBy(p => CompareItem("", "Jewel", jewel, p, null, true).Score);
            var report = CompareItem($"jewel:{jewel.Id}", "Jewel", jewel, best, ignores.GetValueOrDefault($"jewel:{jewel.Id}"), true);
            if (best is not null && report.Score > 0) pool.Remove(best);
            else if (report.Status != "empty") report = report with { Current = null, Status = "missing", Score = 0, Mods = report.Mods.Select(m => m with { State = "missing", Current = null }).ToList(), Notes = new() };
            result.Add(report);
        }
        return result;
    }

    internal static SlotReport CompareItem(string key, string slot, PobItem want, PoeItem? have, string? ignored, bool isJewel)
    {
        var buildView = new ItemView(want.Name, want.BaseType, want.Rarity, want.MaxLinks, want.Sockets,
            want.Implicits.Select(m => m.Text).ToList(), want.Explicits.Select(m => m.Text).ToList(), null);
        if (have is null)
            return new SlotReport(key, slot, "empty", 0, ignored, buildView, null,
                want.Explicits.Select(m => new ModLine(m.Text, "missing", null)).ToList(), new(), new());

        var haveName = have.CleanName;
        var haveBase = have.CleanBaseType;
        var haveMods = have.AllMods.ToList();
        var currentView = new ItemView(
            string.IsNullOrEmpty(haveName) ? have.CleanTypeLine : haveName, haveBase,
            have.FrameType switch { 0 => "NORMAL", 1 => "MAGIC", 2 => "RARE", 3 or 9 => "UNIQUE", _ => "OTHER" },
            have.MaxLinks, null, (have.ImplicitMods ?? new()).Concat(have.EnchantMods ?? new()).ToList(),
            (have.FracturedMods ?? new()).Concat(have.ExplicitMods ?? new()).Concat(have.CraftedMods ?? new()).ToList(), have.Icon);

        var notes = new List<string>();
        double score;
        List<ModLine> mods;
        var stats = new List<StatLine>();

        if (want.IsUnique)
        {
            var same = string.Equals(haveName, want.Name, StringComparison.OrdinalIgnoreCase);
            score = same ? 1 : 0;
            mods = new();
            if (!same) notes.Add($"Build dùng unique {want.Name}.");
        }
        else
        {
            var wantedMods = (isJewel ? want.Implicits.Concat(want.Explicits) : want.Explicits).Select(m => m.Text).ToList();
            var compared = ModMatching.Compare(wantedMods, haveMods);
            mods = compared.Select(c => new ModLine(c.Text, c.State.ToString().ToLowerInvariant(), c.Current)).ToList();
            var modScore = compared.Count == 0 ? 1 : compared.Sum(c => c.State switch { ModMatching.ModState.Ok => 1.0, ModMatching.ModState.Low => 0.5, _ => 0 }) / compared.Count;

            var wantStats = ModMatching.PseudoStats(want.Implicits.Concat(want.Explicits).Select(m => m.Text));
            var haveStats = ModMatching.PseudoStats(haveMods);
            foreach (var stat in ModMatching.StatOrder.Where(s => wantStats.GetValueOrDefault(s) > 0))
                stats.Add(new StatLine(stat, wantStats[stat], haveStats.GetValueOrDefault(stat)));
            double? statScore = stats.Count == 0 ? null : stats.Average(s => Math.Min(1, s.Current / s.Build));
            score = statScore is null ? modScore : Math.Max(modScore, (modScore + statScore.Value) / 2);

            // Magic/normal items carry the base inside the name ("Experimenter's Quicksilver Flask of Staunching").
            var baseMatches = want.Rarity is "MAGIC" or "NORMAL"
                ? want.Name.Contains(haveBase, StringComparison.OrdinalIgnoreCase)
                : string.Equals(want.BaseType, haveBase, StringComparison.OrdinalIgnoreCase);
            if (!baseMatches)
            {
                notes.Add($"Base khác: build dùng {(want.Rarity is "MAGIC" or "NORMAL" ? want.Name : want.BaseType)}.");
                if (want.Rarity is "MAGIC" or "NORMAL") score *= 0.5;
            }
            if (have.FrameType == 3 && !want.IsUnique) notes.Add("Đang dùng unique thay cho đồ rare.");
        }

        if (want.MaxLinks >= 4 && have.MaxLinks < want.MaxLinks)
        {
            notes.Add($"Link {have.MaxLinks}/{want.MaxLinks}.");
            score = Math.Min(score, 0.8);
        }

        var status = score >= 0.85 ? "ok" : score >= 0.4 ? "partial" : "missing";
        return new SlotReport(key, slot, status, score, ignored, buildView, currentView, mods, stats, notes);
    }

    // ---------------------------------------------------------------- gems

    private static string GemKey(string name)
    {
        var n = name.Trim().ToLowerInvariant();
        return n.EndsWith(" support") ? n[..^" support".Length] : n;
    }

    private sealed record HaveGem(string Inventory, PoeItem Item, int Group, int Level);

    private static List<GemGroupReport> CompareGems(PobSkillSet set, CharacterGear gear, IReadOnlyDictionary<string, string> ignores)
    {
        var have = new List<(string Key, HaveGem Gem)>();
        foreach (var item in gear.Equipped)
        {
            foreach (var g in item.SocketedItems ?? new())
            {
                if (g.FrameType != 4) continue;
                var group = g.Socket is { } s && item.Sockets is { } sockets && s < sockets.Count ? sockets[s].Group : -1;
                have.Add((GemKey(g.CleanTypeLine), new HaveGem(item.InventoryId ?? "", item, group, g.PropertyInt("Level") ?? 1)));
            }
        }

        var result = new List<GemGroupReport>();
        for (var gi = 0; gi < set.Groups.Count; gi++)
        {
            var group = set.Groups[gi];
            var target = group.Slot is not null && SlotMap.TryGetValue(group.Slot, out var t) ? t.Inventory : null;
            var lines = new List<GemLine>();
            var foundInTarget = new List<HaveGem>();
            foreach (var gem in group.Gems)
            {
                var matches = have.Where(h => h.Key == GemKey(gem.Name)).Select(h => h.Gem).ToList();
                var inTarget = matches.Where(m => target is null || m.Inventory == target).MaxBy(m => m.Level);
                if (inTarget is not null)
                {
                    foundInTarget.Add(inTarget);
                    lines.Add(new GemLine(gem.Name, gem.IsSupport, gem.Level, gem.Quality,
                        inTarget.Level >= gem.Level - 2 ? "ok" : "low", inTarget.Level, SlotLabel(inTarget.Inventory)));
                }
                else if (matches.Count > 0)
                {
                    var m = matches.MaxBy(x => x.Level)!;
                    lines.Add(new GemLine(gem.Name, gem.IsSupport, gem.Level, gem.Quality, "elsewhere", m.Level, SlotLabel(m.Inventory)));
                }
                else
                {
                    lines.Add(new GemLine(gem.Name, gem.IsSupport, gem.Level, gem.Quality, "missing", null, null));
                }
            }

            // All found gems should share one link group in one item.
            var linked = foundInTarget.Count <= 1 || foundInTarget.Select(f => (f.Inventory, f.Group)).Distinct().Count() == 1;
            var okCount = lines.Count(l => l.State is "ok" or "low");
            var status = okCount == lines.Count && linked ? "ok" : okCount > 0 || lines.Any(l => l.State == "elsewhere") ? "partial" : "missing";
            var label = group.Label is { Length: > 0 } l ? l : group.Gems.FirstOrDefault(g => !g.IsSupport)?.Name ?? group.Gems[0].Name;
            var key = $"gems:{set.Id}:{gi}";
            result.Add(new GemGroupReport(key, label, group.Slot, status, ignores.GetValueOrDefault(key), linked, lines));
        }
        return result;
    }

    private static string SlotLabel(string inventory) => inventory switch
    {
        "Weapon" => "Weapon 1", "Offhand" => "Weapon 2", "Weapon2" => "Weapon 1 Swap", "Offhand2" => "Weapon 2 Swap",
        "Helm" => "Helmet", "BodyArmour" => "Body Armour", "Ring" => "Ring 1", "Ring2" => "Ring 2", "Ring3" => "Ring 3",
        _ => inventory,
    };

    // ---------------------------------------------------------------- tree

    private static TreeReport CompareTree(PobTreeSpec spec, CharacterGear gear, IReadOnlyDictionary<int, PassiveNode> tree)
    {
        var want = Countable(spec.Nodes, tree);
        var have = Countable(gear.Passives, tree);
        var missing = want.Except(have).ToList();
        var extra = have.Except(want).ToList();

        static bool Important(PassiveNode? n) => n is { Kind: "keystone" or "notable" or "ascendancy-notable" };
        List<string> Names(IEnumerable<int> ids) => ids
            .Select(id => tree.GetValueOrDefault(id))
            .Where(Important)
            .OrderBy(n => n!.Kind == "keystone" ? 0 : n.Kind == "ascendancy-notable" ? 1 : 2)
            .Select(n => n!.Kind == "keystone" ? $"{n.Name} (keystone)" : n.Kind == "ascendancy-notable" ? $"{n.Name} (ascendancy)" : n.Name)
            .ToList();

        var missingNames = Names(missing);
        var extraNames = Names(extra);
        return new TreeReport(have.Count, want.Count, want.Intersect(have).Count(),
            missingNames, missing.Count - missingNames.Count, extraNames, extra.Count - extraNames.Count, spec.Url);
    }
}
