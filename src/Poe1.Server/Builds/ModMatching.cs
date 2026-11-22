using System.Globalization;
using System.Text.RegularExpressions;

namespace Poe1.Builds;

/// <summary>Compares mod lines by their wording with the numbers taken out ("+# to maximum life").</summary>
public static partial class ModMatching
{
    public sealed record Normalized(string Template, double[] Values)
    {
        /// <summary>One number to compare rolls by: the average of the values ("Adds 8 to 12" → 10).</summary>
        public double Magnitude => Values.Length == 0 ? 0 : Values.Select(Math.Abs).Average();
    }

    public static Normalized Normalize(string mod)
    {
        var values = new List<double>();
        var template = NumberRegex().Replace(mod.ToLowerInvariant(), m =>
        {
            values.Add(double.Parse(m.Value.TrimStart('+'), CultureInfo.InvariantCulture));
            return "#";
        });
        template = SpaceRegex().Replace(template, " ").Trim();
        return new Normalized(template, values.ToArray());
    }

    public enum ModState { Ok, Low, Missing }

    public sealed record ModResult(string Text, ModState State, string? Current);

    /// <summary>
    /// For each wanted mod: Ok when the item has the same mod at ≥ 90% of the value, Low when it is lower,
    /// Missing when the item has no mod with that wording.
    /// </summary>
    public static List<ModResult> Compare(IEnumerable<string> wanted, IEnumerable<string> have)
    {
        var haveList = have.Select(h => (Text: h, N: Normalize(h))).ToList();
        var used = new HashSet<int>();
        var results = new List<ModResult>();
        foreach (var w in wanted)
        {
            var n = Normalize(w);
            var best = -1;
            for (var i = 0; i < haveList.Count; i++)
            {
                if (used.Contains(i) || haveList[i].N.Template != n.Template) continue;
                if (best < 0 || haveList[i].N.Magnitude > haveList[best].N.Magnitude) best = i;
            }
            if (best < 0)
            {
                results.Add(new ModResult(w, ModState.Missing, null));
                continue;
            }
            used.Add(best);
            var ok = n.Values.Length == 0 || haveList[best].N.Magnitude >= n.Magnitude * 0.9;
            results.Add(new ModResult(w, ok ? ModState.Ok : ModState.Low, haveList[best].Text));
        }
        return results;
    }

    public static readonly string[] StatOrder =
        { "Life", "Energy Shield", "Mana", "Fire Res", "Cold Res", "Lightning Res", "Chaos Res", "Str", "Dex", "Int", "Move Speed", "Suppress" };

    /// <summary>Sums the stats players compare first (life, resistances, attributes…), across single and hybrid mods.</summary>
    public static Dictionary<string, double> PseudoStats(IEnumerable<string> mods)
    {
        var s = new Dictionary<string, double>();
        void Add(string k, double v) => s[k] = s.GetValueOrDefault(k) + v;
        foreach (var raw in mods)
        {
            var mod = raw.Trim();
            Match m;
            if ((m = FlatRegex().Match(mod)).Success)
            {
                var v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (m.Groups[2].Value.ToLowerInvariant())
                {
                    case "maximum life": Add("Life", v); break;
                    case "maximum energy shield": Add("Energy Shield", v); break;
                    case "maximum mana": Add("Mana", v); break;
                    case "strength": Add("Str", v); break;
                    case "dexterity": Add("Dex", v); break;
                    case "intelligence": Add("Int", v); break;
                    case "all attributes": Add("Str", v); Add("Dex", v); Add("Int", v); break;
                    case "strength and dexterity": Add("Str", v); Add("Dex", v); break;
                    case "strength and intelligence": Add("Str", v); Add("Int", v); break;
                    case "dexterity and intelligence": Add("Dex", v); Add("Int", v); break;
                }
            }
            else if ((m = ResistRegex().Match(mod)).Success)
            {
                var v = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                var what = m.Groups[2].Value.ToLowerInvariant();
                if (what.Contains("all elemental")) { Add("Fire Res", v); Add("Cold Res", v); Add("Lightning Res", v); continue; }
                if (what.Contains("fire")) Add("Fire Res", v);
                if (what.Contains("cold")) Add("Cold Res", v);
                if (what.Contains("lightning")) Add("Lightning Res", v);
                if (what.Contains("chaos")) Add("Chaos Res", v);
            }
            else if ((m = MoveSpeedRegex().Match(mod)).Success)
                Add("Move Speed", double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
            else if ((m = SuppressRegex().Match(mod)).Success)
                Add("Suppress", double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        }
        return s;
    }

    [GeneratedRegex(@"[+-]?\d+(?:\.\d+)?")]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpaceRegex();

    [GeneratedRegex(@"^\+(\d+) to (maximum life|maximum energy shield|maximum mana|strength|dexterity|intelligence|all attributes|strength and dexterity|strength and intelligence|dexterity and intelligence)$", RegexOptions.IgnoreCase)]
    private static partial Regex FlatRegex();

    [GeneratedRegex(@"^\+(\d+)% to (.+?) resistances?$", RegexOptions.IgnoreCase)]
    private static partial Regex ResistRegex();

    [GeneratedRegex(@"^(\d+)% increased movement speed$", RegexOptions.IgnoreCase)]
    private static partial Regex MoveSpeedRegex();

    [GeneratedRegex(@"^\+(\d+)% chance to suppress spell damage$", RegexOptions.IgnoreCase)]
    private static partial Regex SuppressRegex();
}
