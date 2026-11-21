using Poe1.Infrastructure;
using Poe1.Pricing;
using Poe1.Wealth;

namespace Poe1.Tests;

public class WealthTests
{
    private static (SnapshotRepository Repo, Database Db) NewRepo()
    {
        var file = Path.Combine(Directory.CreateTempSubdirectory().FullName, "t.db");
        var db = new Database(file);
        return (new SnapshotRepository(db), db);
    }

    private static PricedItem P(string name, double qty, double unit) => new($"ex:{name}", name, "currency", null, 5, qty, unit);

    private static SourceSnapshot Tab(string key, params PricedItem[] items) => new(key, key, "tab", 0, null, items);

    [Fact]
    public void Insert_merges_stacks_and_totals_per_source()
    {
        var (repo, _) = NewRepo();
        var id = repo.Insert("L", DateTimeOffset.UtcNow, 200, new[]
        {
            Tab("tab:a", P("Chaos Orb", 10, 1), P("Chaos Orb", 5, 1), P("Divine Orb", 1, 200)),
            Tab("tab:b", P("Chaos Orb", 20, 1)),
        });
        var snap = repo.Get(id)!;
        Assert.Equal(235, snap.TotalChaos);
        var sources = repo.Sources(id);
        Assert.Equal(215, sources.Single(s => s.SourceKey == "tab:a").ValueChaos);
        Assert.Equal(15, repo.Items(id, "tab:a").Single(i => i.Name == "Chaos Orb").Quantity);
        var merged = repo.Items(id);
        Assert.Equal(35, merged.Single(i => i.Name == "Chaos Orb").Quantity);
        Assert.Equal("Divine Orb", merged.First().Name);
    }

    [Fact]
    public void Alerts_report_price_moves_on_held_items()
    {
        var (repo, _) = NewRepo();
        var t0 = DateTimeOffset.UtcNow - TimeSpan.FromHours(25);
        repo.Insert("L", t0, 200, new[] { Tab("tab:a", P("Abyss Thread", 49, 3.2), P("Blight Thread", 43, 2.0), P("Tiny", 1, 1)) });
        repo.Insert("L", t0 + TimeSpan.FromHours(25), 200, new[] { Tab("tab:a", P("Abyss Thread", 49, 4.0), P("Blight Thread", 43, 1.5), P("Tiny", 1, 3)) });

        var report = new PriceAlerts(repo).Compute("L", minChangePercent: 20, minHeldChaos: 20, compareHours: 24);
        Assert.Equal(2, report.Items.Count); // "Tiny" moved 200% but is worth too little
        var abyss = report.Items.Single(i => i.Name == "Abyss Thread");
        Assert.Equal(25, abyss.ChangePercent, 3);
        Assert.Equal(49 * 0.8, abyss.HeldChange, 3);
        Assert.Equal(1, report.Rising);
        Assert.Equal(1, report.Falling);
    }

    [Fact]
    public void Farm_report_counts_loot_at_end_prices()
    {
        var (repo, db) = NewRepo();
        var start = repo.Insert("L", DateTimeOffset.UtcNow - TimeSpan.FromHours(2), 200, new[] { Tab("tab:a", P("Chaos Orb", 100, 1), P("Divine Orb", 1, 200)) });
        var end = repo.Insert("L", DateTimeOffset.UtcNow, 210, new[] { Tab("tab:a", P("Chaos Orb", 150, 1), P("Divine Orb", 2, 210), P("Stacked Deck", 10, 3)) });
        var farm = new FarmService(db, repo);
        var id = farm.Start("Test", "L", start);
        farm.Stop(id, end);
        var report = farm.Report(id)!;
        Assert.Equal(50 + 210 + 30, report.LootValue, 3);
        Assert.Equal(150 + 420 + 30 - 300, report.NetWorthChange, 3);
        Assert.Equal(3, report.Gained.Count);
        Assert.Empty(report.Spent);
    }

    [Fact]
    public void Prune_keeps_daily_and_farm_snapshots()
    {
        var (repo, db) = NewRepo();
        var day = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var ids = Enumerable.Range(0, 4).Select(h => repo.Insert("L", day + TimeSpan.FromHours(h * 6), 200, new[] { Tab("tab:a", P("Chaos Orb", h, 1)) })).ToList();
        new FarmService(db, repo).Start("keep", "L", ids[2]);
        var pruned = repo.PruneItems("L", TimeSpan.FromDays(1));
        Assert.Equal(2, pruned); // ids[0] is the first of the day, ids[2] belongs to a farm session
        Assert.False(repo.Get(ids[0])!.ItemsPruned);
        Assert.True(repo.Get(ids[1])!.ItemsPruned);
        Assert.False(repo.Get(ids[2])!.ItemsPruned);
        Assert.Equal(4, repo.History("L", null).Count);
    }
}
