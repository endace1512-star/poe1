using Dapper;
using Poe1.Infrastructure;
using Poe1.Pricing;

namespace Poe1.Wealth;

// Row types are mutable classes (not positional records) so Dapper maps by name and converts SQLite's
// INTEGER/NULL columns instead of requiring an exact constructor signature.
public sealed class SnapshotRow
{
    public long Id { get; set; }
    public string Ts { get; set; } = "";
    public string League { get; set; } = "";
    public double TotalChaos { get; set; }
    public double DivineChaos { get; set; }
    public long ItemCount { get; set; }
    public bool ItemsPruned { get; set; }
}

public sealed class SourceRow
{
    public string SourceKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public long Idx { get; set; }
    public string? Colour { get; set; }
    public long ItemCount { get; set; }
    public double ValueChaos { get; set; }
}

public sealed class ItemRow
{
    public string ItemKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string? Icon { get; set; }
    public long FrameType { get; set; }
    public double Quantity { get; set; }
    public double UnitChaos { get; set; }
    public double TotalChaos { get; set; }
    public string? SourceKey { get; set; }
    public string? SourceName { get; set; }
}

public sealed class HistoryPoint
{
    public string Ts { get; set; } = "";
    public double TotalChaos { get; set; }
    public double DivineChaos { get; set; }
}

/// <summary>A source (stash tab or character) and its priced items, ready to be stored.</summary>
public sealed record SourceSnapshot(string Key, string Name, string Kind, int Index, string? Colour, IReadOnlyList<PricedItem> Items);

public sealed class SnapshotRepository
{
    private readonly Database _db;

    public SnapshotRepository(Database db) => _db = db;

    public static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");

    public long Insert(string league, DateTimeOffset ts, double divineChaos, IReadOnlyList<SourceSnapshot> sources)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        // Merge identical items within a source (e.g. several stacks of Chaos Orbs).
        var merged = sources.Select(s => (Source: s, Items: s.Items
            .GroupBy(i => i.Key)
            .Select(g => g.First() with { Quantity = g.Sum(i => i.Quantity) })
            .ToList())).ToList();

        var total = merged.Sum(m => m.Items.Sum(i => i.TotalChaos));
        var count = merged.Sum(m => m.Items.Sum(i => (long)Math.Max(1, Math.Round(i.Quantity))));
        var id = c.ExecuteScalar<long>(
            "INSERT INTO snapshots(ts, league, total_chaos, divine_chaos, item_count) VALUES (@ts, @league, @total, @divine, @count); SELECT last_insert_rowid();",
            new { ts = Iso(ts), league, total, divine = divineChaos, count }, tx);

        foreach (var (s, items) in merged)
        {
            c.Execute("""
                INSERT INTO snapshot_sources(snapshot_id, source_key, name, kind, idx, colour, item_count, value_chaos)
                VALUES (@id, @Key, @Name, @Kind, @Index, @Colour, @itemCount, @value)
                """,
                new { id, s.Key, s.Name, s.Kind, s.Index, s.Colour, itemCount = items.Sum(i => (long)Math.Max(1, Math.Round(i.Quantity))), value = items.Sum(i => i.TotalChaos) }, tx);
            c.Execute("""
                INSERT INTO snapshot_items(snapshot_id, source_key, item_key, name, category, icon, frame_type, quantity, unit_chaos, total_chaos)
                VALUES (@id, @source, @Key, @Name, @Category, @Icon, @FrameType, @Quantity, @UnitChaos, @TotalChaos)
                """,
                items.Select(i => new { id, source = s.Key, i.Key, i.Name, i.Category, i.Icon, i.FrameType, i.Quantity, i.UnitChaos, i.TotalChaos }), tx);
        }
        tx.Commit();
        return id;
    }

    public SnapshotRow? Latest(string league)
    {
        using var c = _db.Open();
        return c.QueryFirstOrDefault<SnapshotRow>(
            "SELECT id, ts, league, total_chaos AS TotalChaos, divine_chaos AS DivineChaos, item_count AS ItemCount, items_pruned AS ItemsPruned FROM snapshots WHERE league = @league ORDER BY ts DESC, id DESC LIMIT 1",
            new { league });
    }

    public SnapshotRow? Get(long id)
    {
        using var c = _db.Open();
        return c.QueryFirstOrDefault<SnapshotRow>(
            "SELECT id, ts, league, total_chaos AS TotalChaos, divine_chaos AS DivineChaos, item_count AS ItemCount, items_pruned AS ItemsPruned FROM snapshots WHERE id = @id",
            new { id });
    }

    /// <summary>The newest snapshot taken at or before <paramref name="at"/> that still has its items.</summary>
    public SnapshotRow? ClosestWithItems(string league, DateTimeOffset at, long excludeId)
    {
        using var c = _db.Open();
        return c.QueryFirstOrDefault<SnapshotRow>("""
            SELECT id, ts, league, total_chaos AS TotalChaos, divine_chaos AS DivineChaos, item_count AS ItemCount, items_pruned AS ItemsPruned
            FROM snapshots
            WHERE league = @league AND items_pruned = 0 AND id <> @excludeId AND ts <= @at
            ORDER BY ts DESC LIMIT 1
            """, new { league, at = Iso(at), excludeId })
            // Not enough history yet: fall back to the oldest snapshot we have.
            ?? c.QueryFirstOrDefault<SnapshotRow>("""
            SELECT id, ts, league, total_chaos AS TotalChaos, divine_chaos AS DivineChaos, item_count AS ItemCount, items_pruned AS ItemsPruned
            FROM snapshots
            WHERE league = @league AND items_pruned = 0 AND id <> @excludeId
            ORDER BY ts ASC LIMIT 1
            """, new { league, excludeId });
    }

    public List<HistoryPoint> History(string league, DateTimeOffset? since)
    {
        using var c = _db.Open();
        return c.Query<HistoryPoint>(
            "SELECT ts, total_chaos AS TotalChaos, divine_chaos AS DivineChaos FROM snapshots WHERE league = @league AND (@since IS NULL OR ts >= @since) ORDER BY ts",
            new { league, since = since is null ? null : Iso(since.Value) }).ToList();
    }

    public List<SourceRow> Sources(long snapshotId)
    {
        using var c = _db.Open();
        return c.Query<SourceRow>(
            "SELECT source_key AS SourceKey, name, kind, idx, colour, item_count AS ItemCount, value_chaos AS ValueChaos FROM snapshot_sources WHERE snapshot_id = @snapshotId ORDER BY value_chaos DESC",
            new { snapshotId }).ToList();
    }

    /// <summary>Items of one source, or of every source merged by item when <paramref name="sourceKey"/> is null.</summary>
    public List<ItemRow> Items(long snapshotId, string? sourceKey = null)
    {
        using var c = _db.Open();
        if (sourceKey is not null)
        {
            return c.Query<ItemRow>("""
                SELECT i.item_key AS ItemKey, i.name, i.category, i.icon, i.frame_type AS FrameType, i.quantity, i.unit_chaos AS UnitChaos, i.total_chaos AS TotalChaos,
                       i.source_key AS SourceKey, s.name AS SourceName
                FROM snapshot_items i JOIN snapshot_sources s ON s.snapshot_id = i.snapshot_id AND s.source_key = i.source_key
                WHERE i.snapshot_id = @snapshotId AND i.source_key = @sourceKey
                ORDER BY i.total_chaos DESC, i.name
                """, new { snapshotId, sourceKey }).ToList();
        }
        return c.Query<ItemRow>("""
            SELECT i.item_key AS ItemKey, MAX(i.name) AS Name, MAX(i.category) AS Category, MAX(i.icon) AS Icon, MAX(i.frame_type) AS FrameType,
                   SUM(i.quantity) AS Quantity, MAX(i.unit_chaos) AS UnitChaos, SUM(i.total_chaos) AS TotalChaos,
                   NULL AS SourceKey, group_concat(DISTINCT s.name) AS SourceName
            FROM snapshot_items i JOIN snapshot_sources s ON s.snapshot_id = i.snapshot_id AND s.source_key = i.source_key
            WHERE i.snapshot_id = @snapshotId
            GROUP BY i.item_key
            ORDER BY TotalChaos DESC, Name
            """, new { snapshotId }).ToList();
    }

    /// <summary>
    /// Drops per-item rows of old snapshots to keep the database small. Totals and per-tab values stay.
    /// Kept: the last <paramref name="keepRecent"/>, the first snapshot of every day, and farm-session snapshots.
    /// </summary>
    public int PruneItems(string league, TimeSpan keepRecent)
    {
        using var c = _db.Open();
        var cutoff = Iso(DateTimeOffset.UtcNow - keepRecent);
        var ids = c.Query<long>("""
            SELECT id FROM snapshots s
            WHERE league = @league AND items_pruned = 0 AND ts < @cutoff
              AND id NOT IN (SELECT MIN(id) FROM snapshots WHERE league = @league GROUP BY substr(ts, 1, 10))
              AND id NOT IN (SELECT start_snapshot_id FROM farm_sessions)
              AND id NOT IN (SELECT end_snapshot_id FROM farm_sessions WHERE end_snapshot_id IS NOT NULL)
            """, new { league, cutoff }).ToList();
        if (ids.Count == 0) return 0;
        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM snapshot_items WHERE snapshot_id IN @ids", new { ids }, tx);
        c.Execute("UPDATE snapshots SET items_pruned = 1 WHERE id IN @ids", new { ids }, tx);
        tx.Commit();
        return ids.Count;
    }

    public int Count(string league)
    {
        using var c = _db.Open();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM snapshots WHERE league = @league", new { league });
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM snapshots WHERE id = @id AND id NOT IN (SELECT start_snapshot_id FROM farm_sessions) AND id NOT IN (SELECT IFNULL(end_snapshot_id, -1) FROM farm_sessions)", new { id });
    }
}
