using Poe1.Pricing;

namespace Poe1.Wealth;

/// <summary>
/// Fills an empty database with two days of made-up snapshots so the UI can be tried without a PoE account
/// (run with <c>--demo</c>). Never used unless asked for.
/// </summary>
public static class DemoSeeder
{
    private sealed record DemoItem(string Name, string Category, int Frame, double Qty, double Price, string Icon);

    private const string Cdn = "https://web.poecdn.com/gen/image/";

    private static readonly (string Name, string Colour, DemoItem[] Items)[] Tabs =
    {
        ("Currency", "#7c5436", new DemoItem[]
        {
            new("Divine Orb", "currency", 5, 13, 220, Cdn + "WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lNb2RWYWx1ZXMiLCJzY2FsZSI6MX1d/ec48896769/CurrencyModValues.png"),
            new("Chaos Orb", "currency", 5, 290, 1, Cdn + "WzI1LDE0LHsiZiI6IjJESXRlbXMvQ3VycmVuY3kvQ3VycmVuY3lSZXJvbGxSYXJlIiwic2NhbGUiOjF9XQ/46a2347805/CurrencyRerollRare.png"),
            new("Orb of Alteration", "currency", 5, 499, 0.18, ""),
            new("Orb of Scouring", "currency", 5, 107, 0.8, ""),
            new("Gemcutter's Prism", "currency", 5, 37, 3.4, ""),
            new("Orb of Annulment", "currency", 5, 11, 6.7, ""),
            new("Vaal Orb", "currency", 5, 57, 0.9, ""),
            new("Orb of Alchemy", "currency", 5, 371, 0.1, ""),
        }),
        ("frags", "#3f6f9f", new DemoItem[]
        {
            new("Abyss Thread", "scarab", 5, 44, 4.1, ""),
            new("Legion Thread", "scarab", 5, 40, 3.2, ""),
            new("Ultimatum Thread", "scarab", 5, 56, 2.0, ""),
            new("Blight Thread", "scarab", 5, 33, 2.3, ""),
            new("Valdo's Puzzle Box", "fragment", 0, 1, 148, ""),
            new("Fragment of Knowledge", "fragment", 0, 1, 97, ""),
        }),
        ("8", "#2f7f4f", new DemoItem[]
        {
            new("Stacked Deck", "currency", 5, 37, 3.2, ""),
            new("Two-Stone Ring (ilvl85)", "base", 0, 5, 5, ""),
            new("Fated Heavy Belt (ilvl85)", "base", 0, 3, 5, ""),
        }),
        ("~price 1 chaos", "#9f3f3f", new DemoItem[]
        {
            new("Inspired Learning Crimson Jewel", "unique", 3, 1, 110, ""),
            new("Hivebrain Gland", "fragment", 0, 1, 77, ""),
        }),
    };

    public static void Seed(SnapshotRepository repo, string league)
    {
        if (repo.Count(league) > 0) return;
        var rng = new Random(42);
        var now = DateTimeOffset.UtcNow;
        var drift = new Dictionary<string, double>();
        for (var step = 48; step >= 0; step--)
        {
            var ts = now - TimeSpan.FromHours(step);
            var sources = new List<SourceSnapshot>();
            var tabIndex = 0;
            foreach (var (tabName, colour, items) in Tabs)
            {
                var priced = items.Select(i =>
                {
                    drift[i.Name] = Math.Clamp(drift.GetValueOrDefault(i.Name, 1) * (1 + (rng.NextDouble() - 0.48) * 0.08), 0.5, 1.8);
                    var qty = Math.Max(0, Math.Round(i.Qty * (1 - step / 96.0)));
                    var unit = i.Name == "Chaos Orb" ? 1 : i.Price * drift[i.Name];
                    return new PricedItem($"ex:{i.Name}", i.Name, i.Category, string.IsNullOrEmpty(i.Icon) ? null : i.Icon, i.Frame, qty, unit);
                }).Where(i => i.Quantity > 0).ToList();
                sources.Add(new SourceSnapshot($"tab:demo{tabIndex}", tabName, "tab", tabIndex, colour, priced));
                tabIndex++;
            }
            sources.Add(new SourceSnapshot("char:DemoWitch", "DemoWitch", "character", 1000, null, new List<PricedItem>
            {
                new("gem:Raise Spectre:21:20:c", "Raise Spectre (21/20c)", "gem", null, 4, 1, 60 + step),
            }));
            repo.Insert(league, ts, 220 + rng.NextDouble() * 6, sources);
        }
    }
}
