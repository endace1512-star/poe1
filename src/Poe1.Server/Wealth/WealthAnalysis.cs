using Dapper;
using Poe1.Infrastructure;

namespace Poe1.Wealth;

public sealed record PriceChange(
    string ItemKey, string Name, string? Icon, string Category, string Sources,
    double Quantity, double OldUnit, double NewUnit, double ChangePercent, double HeldChange);

public sealed record PriceChangeReport(string? CurrentTs, string? ReferenceTs, int Rising, int Falling, double NetHeldChange, List<PriceChange> Items);

public sealed record ItemDelta(string ItemKey, string Name, string? Icon, string Category, double StartQty, double EndQty, double DeltaQty, double UnitChaos, double DeltaValue);

public sealed record FarmSession(
    long Id, string Name, string League, string StartTs, string? EndTs, long StartSnapshotId, long? EndSnapshotId,
    double StartChaos, double? EndChaos, double DivineChaos, string? Notes);

public sealed record FarmReport(FarmSession Session, string ComparedTs, double DurationHours, double NetWorthChange, double LootValue, double PerHour, List<ItemDelta> Gained, List<ItemDelta> Spent);

/// <summary>Price-change alerts: items you hold whose unit price moved a lot since an earlier snapshot.</summary>
public sealed class PriceAlerts
{
    private readonly SnapshotRepository _repo;
    public PriceAlerts(SnapshotRepository repo) => _repo = repo;

    public PriceChangeReport Compute(string league, double minChangePercent, double minHeldChaos, int compareHours)
    {
        var current = _repo.Latest(league);
        if (current is null) return new PriceChangeReport(null, null, 0, 0, 0, new());
        var reference = _repo.ClosestWithItems(league, DateTimeOffset.Parse(current.Ts) - TimeSpan.FromHours(compareHours), current.Id);
        if (reference is null) return new PriceChangeReport(current.Ts, null, 0, 0, 0, new());

        var now = _repo.Items(current.Id);
        var before = _repo.Items(reference.Id).ToDictionary(i => i.ItemKey);
        var changes = new List<PriceChange>();
        foreach (var item in now)
        {
            if (!before.TryGetValue(item.ItemKey, out var old)) continue;
            if (old.UnitChaos <= 0 || item.UnitChaos <= 0) continue;
            var pct = (item.UnitChaos - old.UnitChaos) / old.UnitChaos * 100;
            var held = item.Quantity * (item.UnitChaos - old.UnitChaos);
            var heldValue = Math.Max(item.TotalChaos, item.Quantity * old.UnitChaos);
            if (Math.Abs(pct) < minChangePercent || heldValue < minHeldChaos) continue;
            changes.Add(new PriceChange(item.ItemKey, item.Name, item.Icon, item.Category, item.SourceName ?? "",
                item.Quantity, old.UnitChaos, item.UnitChaos, pct, held));
        }
        changes.Sort((a, b) => Math.Abs(b.HeldChange).CompareTo(Math.Abs(a.HeldChange)));
        return new PriceChangeReport(current.Ts, reference.Ts,
            changes.Count(c => c.ChangePercent > 0), changes.Count(c => c.ChangePercent < 0),
            changes.Sum(c => c.HeldChange), changes);
    }
}

/// <summary>Farm sessions: a start snapshot and an end snapshot; the difference is what the session earned.</summary>
public sealed class FarmService
{
    private readonly Database _db;
    private readonly SnapshotRepository _repo;

    public FarmService(Database db, SnapshotRepository repo)
    {
        _db = db;
        _repo = repo;
    }

    private const string SessionSelect = """
        SELECT f.id, f.name, f.league, f.start_ts AS StartTs, f.end_ts AS EndTs, f.start_snapshot_id AS StartSnapshotId, f.end_snapshot_id AS EndSnapshotId,
               s.total_chaos AS StartChaos, e.total_chaos AS EndChaos, COALESCE(e.divine_chaos, s.divine_chaos) AS DivineChaos, f.notes
        FROM farm_sessions f
        JOIN snapshots s ON s.id = f.start_snapshot_id
        LEFT JOIN snapshots e ON e.id = f.end_snapshot_id
        """;

    public List<FarmSession> List(string league)
    {
        using var c = _db.Open();
        return c.Query<FarmSessionRow>(SessionSelect + " WHERE f.league = @league ORDER BY f.start_ts DESC", new { league })
            .Select(r => r.ToRecord()).ToList();
    }

    public FarmSession? Get(long id)
    {
        using var c = _db.Open();
        return c.QueryFirstOrDefault<FarmSessionRow>(SessionSelect + " WHERE f.id = @id", new { id })?.ToRecord();
    }

    public FarmSession? Active(string league) => List(league).FirstOrDefault(s => s.EndTs is null);

    public long Start(string name, string league, long snapshotId)
    {
        var snap = _repo.Get(snapshotId) ?? throw new InvalidOperationException("Snapshot không tồn tại.");
        using var c = _db.Open();
        return c.ExecuteScalar<long>(
            "INSERT INTO farm_sessions(name, league, start_ts, start_snapshot_id) VALUES (@name, @league, @ts, @snapshotId); SELECT last_insert_rowid();",
            new { name, league, ts = SnapshotRepository.Iso(DateTimeOffset.UtcNow), snapshotId });
    }

    public void Stop(long id, long snapshotId)
    {
        using var c = _db.Open();
        c.Execute("UPDATE farm_sessions SET end_ts = @ts, end_snapshot_id = @snapshotId WHERE id = @id AND end_ts IS NULL",
            new { id, snapshotId, ts = SnapshotRepository.Iso(DateTimeOffset.UtcNow) });
    }

    public void Update(long id, string name, string? notes)
    {
        using var c = _db.Open();
        c.Execute("UPDATE farm_sessions SET name = @name, notes = @notes WHERE id = @id", new { id, name, notes });
    }

    public void Delete(long id)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM farm_sessions WHERE id = @id", new { id });
    }

    /// <summary>
    /// Compares the session's start snapshot with its end snapshot (or the latest snapshot while running).
    /// Loot value counts only quantity changes at current prices, so price swings don't look like farming.
    /// </summary>
    public FarmReport? Report(long id)
    {
        var session = Get(id);
        if (session is null) return null;
        var endId = session.EndSnapshotId ?? _repo.Latest(session.League)?.Id ?? session.StartSnapshotId;
        var start = _repo.Get(session.StartSnapshotId)!;
        var end = _repo.Get(endId)!;

        var startItems = _repo.Items(start.Id).ToDictionary(i => i.ItemKey);
        var endItems = _repo.Items(end.Id).ToDictionary(i => i.ItemKey);
        var deltas = new List<ItemDelta>();
        foreach (var key in startItems.Keys.Union(endItems.Keys))
        {
            startItems.TryGetValue(key, out var a);
            endItems.TryGetValue(key, out var b);
            var dq = (b?.Quantity ?? 0) - (a?.Quantity ?? 0);
            if (Math.Abs(dq) < 1e-9) continue;
            var unit = b?.UnitChaos ?? a!.UnitChaos;
            var any = b ?? a!;
            deltas.Add(new ItemDelta(key, any.Name, any.Icon, any.Category, a?.Quantity ?? 0, b?.Quantity ?? 0, dq, unit, dq * unit));
        }

        var endTs = session.EndTs is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(session.EndTs);
        var hours = Math.Max((endTs - DateTimeOffset.Parse(session.StartTs)).TotalHours, 1.0 / 60);
        var loot = deltas.Sum(d => d.DeltaValue);
        return new FarmReport(session, end.Ts, hours, end.TotalChaos - start.TotalChaos, loot, loot / hours,
            deltas.Where(d => d.DeltaQty > 0).OrderByDescending(d => d.DeltaValue).ToList(),
            deltas.Where(d => d.DeltaQty < 0).OrderBy(d => d.DeltaValue).ToList());
    }

    private sealed class FarmSessionRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = "";
        public string League { get; set; } = "";
        public string StartTs { get; set; } = "";
        public string? EndTs { get; set; }
        public long StartSnapshotId { get; set; }
        public long? EndSnapshotId { get; set; }
        public double StartChaos { get; set; }
        public double? EndChaos { get; set; }
        public double DivineChaos { get; set; }
        public string? Notes { get; set; }

        public FarmSession ToRecord() => new(Id, Name, League, StartTs, EndTs, StartSnapshotId, EndSnapshotId, StartChaos, EndChaos, DivineChaos, Notes);
    }
}
