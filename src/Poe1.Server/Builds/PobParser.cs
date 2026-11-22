using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Poe1.Builds;

/// <summary>Decodes Path of Building export codes and parses the build XML.</summary>
public static partial class PobParser
{
    /// <summary>PoB export code (url-safe base64 of zlib-compressed XML) → XML text.</summary>
    public static string DecodeCode(string code)
    {
        var b64 = Regex.Replace(code.Trim(), @"\s", "").Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        byte[] bytes;
        try { bytes = Convert.FromBase64String(b64); }
        catch (FormatException) { throw new FormatException("Mã PoB không hợp lệ (không phải base64)."); }
        try
        {
            using var input = new MemoryStream(bytes);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var reader = new StreamReader(z, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (InvalidDataException) { throw new FormatException("Mã PoB không hợp lệ (giải nén thất bại)."); }
    }

    /// <summary>
    /// Where to download the code from when the user pasted a link instead of a code.
    /// Returns null when the input is not a supported link.
    /// </summary>
    public static Uri? RawUrlFor(string input)
    {
        var s = input.Trim();
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri) || !uri.Host.Contains('.')) return null;
        var host = uri.Host.ToLowerInvariant().Replace("www.", "");
        var parts = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        return host switch
        {
            // pobb.in/<id>, pobb.in/u/<user>/<id>
            "pobb.in" => new Uri($"https://pobb.in/{parts[^1]}/raw"),
            "pastebin.com" => new Uri($"https://pastebin.com/raw/{parts[^1]}"),
            // poe.ninja/pob/<id>
            "poe.ninja" when parts[0] == "pob" => new Uri($"https://poe.ninja/pob/raw/{parts[^1]}"),
            _ => null,
        };
    }

    public static PobBuild Parse(string xml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException e) { throw new FormatException($"XML của build không đọc được: {e.Message}"); }
        var root = doc.Root ?? throw new FormatException("Build rỗng.");
        var build = new PobBuild();

        var b = root.Element("Build");
        if (b is not null)
        {
            build.ClassName = (string?)b.Attribute("className") ?? "";
            build.Ascendancy = NullIfNone((string?)b.Attribute("ascendClassName"));
            build.Level = Int(b.Attribute("level")) ?? 1;
        }

        ParseTree(root.Element("Tree"), build);
        ParseItems(root.Element("Items"), build);
        ParseSkills(root.Element("Skills"), build);
        return build;
    }

    private static void ParseTree(XElement? tree, PobBuild build)
    {
        if (tree is null) return;
        var i = 0;
        foreach (var spec in tree.Elements("Spec"))
        {
            i++;
            var s = new PobTreeSpec
            {
                Title = Clean((string?)spec.Attribute("title")) is { Length: > 0 } t ? t : $"Cây {i}",
                TreeVersion = (string?)spec.Attribute("treeVersion"),
                Url = spec.Element("URL")?.Value.Trim(),
            };
            foreach (var n in ((string?)spec.Attribute("nodes") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
                if (int.TryParse(n, out var id)) s.Nodes.Add(id);
            foreach (var socket in spec.Element("Sockets")?.Elements("Socket") ?? Enumerable.Empty<XElement>())
            {
                if (Int(socket.Attribute("nodeId")) is { } node && Int(socket.Attribute("itemId")) is { } item && item > 0)
                    s.Jewels[node] = item;
            }
            build.Specs.Add(s);
        }
        build.ActiveSpecIndex = Math.Clamp((Int(tree.Attribute("activeSpec")) ?? 1) - 1, 0, Math.Max(0, build.Specs.Count - 1));
    }

    private static void ParseItems(XElement? items, PobBuild build)
    {
        if (items is null) return;
        foreach (var item in items.Elements("Item"))
        {
            if (Int(item.Attribute("id")) is not { } id) continue;
            var parsed = PobItemParser.Parse(item.Value, Int(item.Attribute("variant")));
            parsed.Id = id;
            build.Items[id] = parsed;
        }

        var sets = items.Elements("ItemSet").ToList();
        if (sets.Count == 0)
        {
            // Old exports: the slots sit directly under <Items>.
            var set = new PobItemSet { Id = "1", Title = "Mặc định" };
            AddSlots(items, set);
            build.ItemSets.Add(set);
        }
        var n = 0;
        foreach (var setEl in sets)
        {
            n++;
            var set = new PobItemSet
            {
                Id = (string?)setEl.Attribute("id") ?? n.ToString(CultureInfo.InvariantCulture),
                Title = Clean((string?)setEl.Attribute("title")) is { Length: > 0 } t ? t : $"Bộ đồ {n}",
            };
            AddSlots(setEl, set);
            build.ItemSets.Add(set);
        }
        var active = (string?)items.Attribute("activeItemSet");
        build.ActiveItemSetIndex = Math.Max(0, build.ItemSets.FindIndex(s => s.Id == active));
    }

    private static void AddSlots(XElement parent, PobItemSet set)
    {
        foreach (var slot in parent.Elements("Slot"))
        {
            var name = (string?)slot.Attribute("name");
            if (string.IsNullOrEmpty(name) || Int(slot.Attribute("itemId")) is not { } id || id <= 0) continue;
            // Abyssal sockets and swap-weapon abyssal sockets are left out for now.
            if (name.Contains("Abyssal Socket")) continue;
            set.Slots[name] = id;
        }
    }

    private static void ParseSkills(XElement? skills, PobBuild build)
    {
        if (skills is null) return;
        var sets = skills.Elements("SkillSet").ToList();
        if (sets.Count == 0)
        {
            var set = new PobSkillSet { Id = "1", Title = "Mặc định" };
            AddGroups(skills, set);
            build.SkillSets.Add(set);
        }
        var n = 0;
        foreach (var setEl in sets)
        {
            n++;
            var set = new PobSkillSet
            {
                Id = (string?)setEl.Attribute("id") ?? n.ToString(CultureInfo.InvariantCulture),
                Title = Clean((string?)setEl.Attribute("title")) is { Length: > 0 } t ? t : $"Bộ skill {n}",
            };
            AddGroups(setEl, set);
            build.SkillSets.Add(set);
        }
        var active = (string?)skills.Attribute("activeSkillSet");
        build.ActiveSkillSetIndex = Math.Max(0, build.SkillSets.FindIndex(s => s.Id == active));
    }

    private static void AddGroups(XElement parent, PobSkillSet set)
    {
        foreach (var skill in parent.Elements("Skill"))
        {
            if ((string?)skill.Attribute("enabled") == "false") continue;
            var group = new PobSkillGroup
            {
                Slot = NullIfNone((string?)skill.Attribute("slot")),
                Label = Clean((string?)skill.Attribute("label")),
            };
            foreach (var gem in skill.Elements("Gem"))
            {
                if ((string?)gem.Attribute("enabled") == "false") continue;
                var gemId = (string?)gem.Attribute("gemId");
                var name = (string?)gem.Attribute("nameSpec");
                if (string.IsNullOrWhiteSpace(name)) name = (string?)gem.Attribute("skillId");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var support = gemId?.Contains("/SupportGem", StringComparison.Ordinal) == true
                              || ((string?)gem.Attribute("skillId"))?.StartsWith("Support", StringComparison.Ordinal) == true;
                group.Gems.Add(new PobGem(name.Trim(), gemId, Int(gem.Attribute("level")) ?? 1, Int(gem.Attribute("quality")) ?? 0, support));
            }
            // Item-granted skills (no gems) don't need anything from the player.
            if (group.Gems.Count > 0) set.Groups.Add(group);
        }
    }

    private static int? Int(XAttribute? a) =>
        a is not null && int.TryParse(a.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static string? NullIfNone(string? s) => string.IsNullOrWhiteSpace(s) || s == "None" ? null : s;

    [GeneratedRegex(@"\^(x[0-9A-Fa-f]{6}|[0-9])")]
    private static partial Regex ColourCode();

    /// <summary>Strips PoB colour codes (^1, ^xFF0000) from titles.</summary>
    public static string Clean(string? s) => s is null ? "" : ColourCode().Replace(s, "").Trim();
}

/// <summary>Parses the item text PoB stores inside &lt;Item&gt; elements.</summary>
public static partial class PobItemParser
{
    private static readonly HashSet<string> Properties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unique ID", "Item Level", "Quality", "Sockets", "LevelReq", "Implicits", "Variant", "Selected Variant",
        "Selected Alt Variant", "Selected Alt Variant Two", "Selected Alt Variant Three", "Selected Alt Variant Four",
        "Selected Alt Variant Five", "Has Alt Variant", "Has Alt Variant Two", "Has Alt Variant Three", "Has Alt Variant Four",
        "Has Alt Variant Five", "Has Variants", "Selected Variants", "Selected Variant Group", "Allow Duplicate Variants",
        "Version", "Selected Version", "League", "Source", "Upgrade", "Crafted", "Scourge", "Crucible", "Implicit",
        "Prefix", "Suffix", "Catalyst", "CatalystQuality", "Radius", "Limited to", "Cluster Jewel Skill",
        "Cluster Jewel Node Count", "Note", "Talisman Tier", "Requires Level", "Requires Class", "Level", "Armour",
        "Evasion", "Evasion Rating", "Energy Shield", "Ward", "ArmourBasePercentile", "EvasionBasePercentile",
        "EnergyShieldBasePercentile", "WardBasePercentile", "Unreleased", "Intangibility", "Memory Strands", "Class",
        "Str", "Dex", "Int", "Strength", "Dexterity", "Intelligence", "Item Class",
    };

    private static readonly string[] InfluenceNames = { "Shaper", "Elder", "Crusader", "Redeemer", "Hunter", "Warlord", "Searing Exarch", "Eater of Worlds" };

    public static PobItem Parse(string text, int? variantAttr = null)
    {
        var item = new PobItem { RawText = text.Trim() };
        var lines = text.Replace("\r", "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var i = 0;
        if (i < lines.Count && lines[i].StartsWith("Rarity:", StringComparison.OrdinalIgnoreCase))
        {
            item.Rarity = lines[i]["Rarity:".Length..].Trim().ToUpperInvariant();
            i++;
        }
        if (i < lines.Count) item.Name = lines[i++];
        item.BaseType = item.Name;
        if (item.Rarity is "RARE" or "UNIQUE" or "RELIC" && i < lines.Count && !IsProperty(lines[i], out _, out _))
            item.BaseType = lines[i++];

        int? selectedVariant = variantAttr;
        int implicitsLeft = -1;
        var mods = new List<(string Line, HashSet<int>? Variants, double? Range, bool Crafted, bool Fractured, bool Enchant, bool Implicit)>();

        for (; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsProperty(line, out var key, out var value))
            {
                switch (key.ToLowerInvariant())
                {
                    case "item level": item.ItemLevel = ParseInt(value); break;
                    case "quality": item.Quality = ParseInt(value); break;
                    case "sockets": item.Sockets = value; break;
                    case "implicits": implicitsLeft = ParseInt(value) ?? 0; break;
                    case "selected variant": selectedVariant ??= ParseInt(value); break;
                }
                continue;
            }
            if (line.Equals("Corrupted", StringComparison.OrdinalIgnoreCase)) { item.Corrupted = true; continue; }
            if (line is "Mirrored" or "Split" or "Synthesised Item" or "Fractured Item" or "Unidentified") continue;
            if (line.StartsWith("Requires ", StringComparison.Ordinal) || line.StartsWith("Foil Unique", StringComparison.Ordinal)) continue;
            var influence = InfluenceNames.FirstOrDefault(n => line.Equals($"{n} Item", StringComparison.OrdinalIgnoreCase));
            if (influence is not null) { item.Influences.Add(influence); continue; }

            // Mod line: optional {tags} prefix and (crafted)/(implicit)/… suffixes.
            HashSet<int>? variants = null;
            double? range = null;
            bool crafted = false, fractured = false, enchant = false, implicitTag = false;
            var mod = TagRegex().Replace(line, m =>
            {
                var k = m.Groups[1].Value.ToLowerInvariant();
                var v = m.Groups[2].Value;
                switch (k)
                {
                    case "variant": variants = v.Split(',').Select(x => ParseInt(x)).Where(x => x is not null).Select(x => x!.Value).ToHashSet(); break;
                    case "range": range = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : null; break;
                    case "crafted": crafted = true; break;
                    case "fractured": fractured = true; break;
                    case "enchant": enchant = true; break;
                }
                return "";
            });
            mod = SuffixRegex().Replace(mod, m =>
            {
                switch (m.Groups[1].Value)
                {
                    case "crafted": crafted = true; break;
                    case "fractured": fractured = true; break;
                    case "enchant": enchant = true; break;
                    case "implicit": implicitTag = true; break;
                }
                return "";
            }).Trim();
            if (mod.Length == 0) continue;
            var isImplicit = implicitTag || enchant || implicitsLeft > 0;
            if (implicitsLeft > 0) implicitsLeft--;
            mods.Add((mod, variants, range, crafted, fractured, enchant, isImplicit));
        }

        foreach (var m in mods)
        {
            if (m.Variants is not null && selectedVariant is not null && !m.Variants.Contains(selectedVariant.Value)) continue;
            var text2 = ApplyRange(m.Line, m.Range ?? 0.5);
            var pm = new PobMod(text2, m.Crafted, m.Fractured, m.Enchant);
            (m.Implicit ? item.Implicits : item.Explicits).Add(pm);
        }
        return item;
    }

    /// <summary>"+(80-89) to maximum Life" with range 0.5 → "+85 to maximum Life" (PoB keeps rolls as ranges plus a position).</summary>
    public static string ApplyRange(string line, double range) =>
        RangeRegex().Replace(line, m =>
        {
            var sign = m.Groups[1].Value;
            var min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            var max = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            var value = min + range * (max - min);
            var decimals = m.Groups[2].Value.Contains('.') || m.Groups[3].Value.Contains('.');
            var rounded = decimals ? Math.Round(value, 2) : Math.Round(value, MidpointRounding.AwayFromZero);
            if (sign == "-") rounded = -rounded;
            var text = rounded.ToString(CultureInfo.InvariantCulture);
            return sign == "+" && rounded > 0 ? "+" + text : text;
        });

    private static bool IsProperty(string line, out string key, out string value)
    {
        key = value = "";
        var idx = line.IndexOf(": ", StringComparison.Ordinal);
        if (idx <= 0) return false;
        key = line[..idx];
        value = line[(idx + 2)..].Trim();
        return Properties.Contains(key);
    }

    private static int? ParseInt(string s)
    {
        var m = Regex.Match(s, @"-?\d+");
        return m.Success ? int.Parse(m.Value, CultureInfo.InvariantCulture) : null;
    }

    [GeneratedRegex(@"\{(\w+):?([^}]*)\}")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@" \((crafted|implicit|enchant|fractured|scourge|crucible|synthesis|exarch|eater)\)")]
    private static partial Regex SuffixRegex();

    [GeneratedRegex(@"([+-]?)\((-?\d+(?:\.\d+)?)-(-?\d+(?:\.\d+)?)\)")]
    private static partial Regex RangeRegex();
}
