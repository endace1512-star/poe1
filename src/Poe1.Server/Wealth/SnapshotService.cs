using Poe1.Infrastructure;
using Poe1.Poe;
using Poe1.Pricing;

namespace Poe1.Wealth;

public sealed record SnapshotStatus(
    bool Running,
    string? Progress,
    string? LastError,
    string? LastSnapshotAt,
    string? NextSnapshotAt,
    string? PricesUpdatedAt,
    string? PriceError,
    double DivineChaos);

/// <summary>Takes a snapshot: reads the selected stash tabs and characters, prices every item and stores the result.</summary>
public sealed class SnapshotService
{
    private readonly PoeApiClient _api;
    private readonly PriceService _prices;
    private readonly SnapshotRepository _repo;
    private readonly SettingsStore _settings;
    private readonly EventHub _events;
    private readonly ILogger<SnapshotService> _log;
    private readonly SemaphoreSlim _running = new(1, 1);

    public string? Progress { get; private set; }
    public string? LastError { get; private set; }
    public DateTimeOffset? NextScheduled { get; set; }
    public bool IsRunning => _running.CurrentCount == 0;

    public SnapshotService(PoeApiClient api, PriceService prices, SnapshotRepository repo, SettingsStore settings, EventHub events, ILogger<SnapshotService> log)
    {
        _api = api;
        _prices = prices;
        _repo = repo;
        _settings = settings;
        _events = events;
        _log = log;
    }

    public SnapshotStatus Status()
    {
        var s = _settings.Current;
        var latest = _repo.Latest(s.League);
        var book = _prices.Current;
        return new SnapshotStatus(
            IsRunning, Progress, LastError, latest?.Ts,
            NextScheduled is null ? null : SnapshotRepository.Iso(NextScheduled.Value),
            book is null || book.FetchedAt == DateTimeOffset.MinValue ? null : SnapshotRepository.Iso(book.FetchedAt),
            _prices.LastError,
            book?.DivineChaos ?? latest?.DivineChaos ?? 0);
    }

    /// <summary>Takes a snapshot now. Returns null when one is already running.</summary>
    public async Task<long?> TakeAsync(CancellationToken ct)
    {
        if (!await _running.WaitAsync(0, ct)) return null;
        try
        {
            LastError = null;
            var s = _settings.Current;
            Report("Đang tải giá từ poe.ninja…");
            var book = await _prices.GetAsync(ct);
            var pricer = new ItemPricer(book, s.MinListingCount);
            var sources = new List<SourceSnapshot>();

            Report("Đang đọc danh sách tab…");
            var tabs = await _api.GetStashTabsAsync(ct);
            var selected = s.SelectedTabIds.Count == 0 ? tabs : tabs.Where(t => s.SelectedTabIds.Contains(t.Id)).ToList();
            for (var i = 0; i < selected.Count; i++)
            {
                var tab = selected[i];
                Report($"Đang đọc tab \"{tab.Name}\" ({i + 1}/{selected.Count})…");
                var items = await _api.GetStashItemsAsync(tab.Index, ct);
                sources.Add(new SourceSnapshot($"tab:{tab.Id}", tab.Name, "tab", tab.Index, tab.Colour?.ToHex(),
                    WithSocketed(items).Select(pricer.Price).ToList()));
            }

            if (s.IncludeCharacters)
            {
                Report("Đang đọc nhân vật…");
                var chars = (await _api.GetCharactersAsync(ct))
                    .Where(c => string.Equals(c.League, s.League, StringComparison.OrdinalIgnoreCase))
                    .Where(c => s.SelectedCharacters.Count == 0 || s.SelectedCharacters.Contains(c.Name))
                    .ToList();
                for (var i = 0; i < chars.Count; i++)
                {
                    var ch = chars[i];
                    Report($"Đang đọc nhân vật {ch.Name} ({i + 1}/{chars.Count})…");
                    var items = await _api.GetCharacterItemsAsync(ch.Name, ct);
                    sources.Add(new SourceSnapshot($"char:{ch.Name}", ch.Name, "character", 1000 + i, null,
                        WithSocketed(items).Select(pricer.Price).ToList()));
                }
            }

            var id = _repo.Insert(s.League, DateTimeOffset.UtcNow, book.DivineChaos, sources);
            var pruned = _repo.PruneItems(s.League, TimeSpan.FromDays(8));
            var row = _repo.Get(id)!;
            _log.LogInformation("Snapshot {Id}: {Total:F0}c across {Sources} sources (pruned {Pruned})", id, row.TotalChaos, sources.Count, pruned);
            _events.Publish("snapshot", new { id, ts = row.Ts, totalChaos = row.TotalChaos, divineChaos = row.DivineChaos });
            return id;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            LastError = e.Message;
            _log.LogWarning("Snapshot failed: {Message}", e.Message);
            _events.Publish("snapshot-error", new { message = e.Message });
            throw;
        }
        finally
        {
            Progress = null;
            _events.Publish("snapshot-progress", new { running = false });
            _running.Release();
        }
    }

    private void Report(string text)
    {
        Progress = text;
        _events.Publish("snapshot-progress", new { running = true, text });
    }

    /// <summary>Gems socketed in gear are worth counting too.</summary>
    private static IEnumerable<PoeItem> WithSocketed(IEnumerable<PoeItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            if (item.SocketedItems is { Count: > 0 } socketed)
                foreach (var s in socketed.Where(s => s.FrameType == 4)) yield return s;
        }
    }
}

/// <summary>
/// Takes a snapshot every N minutes while auto-snapshot is on and the account is configured.
/// The schedule follows the latest stored snapshot, so restarting the app does not snapshot again right away.
/// </summary>
public sealed class SnapshotScheduler : BackgroundService
{
    private readonly SnapshotService _snapshots;
    private readonly SnapshotRepository _repo;
    private readonly SettingsStore _settings;
    private readonly ILogger<SnapshotScheduler> _log;

    public SnapshotScheduler(SnapshotService snapshots, SnapshotRepository repo, SettingsStore settings, ILogger<SnapshotScheduler> log)
    {
        _snapshots = snapshots;
        _repo = repo;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        var lastFailure = DateTimeOffset.MinValue;
        while (!ct.IsCancellationRequested)
        {
            var s = _settings.Current;
            var interval = TimeSpan.FromMinutes(Math.Max(5, s.SnapshotIntervalMinutes));
            var ready = s.AutoSnapshot && !string.IsNullOrWhiteSpace(s.AccountName) && !string.IsNullOrWhiteSpace(s.SessionId);
            if (!ready)
            {
                _snapshots.NextScheduled = null;
                await Task.Delay(TimeSpan.FromSeconds(15), ct);
                continue;
            }

            var latest = _repo.Latest(s.League);
            var due = latest is null ? DateTimeOffset.UtcNow : DateTimeOffset.Parse(latest.Ts) + interval;
            // After a failure, wait a few minutes instead of retrying every loop.
            var retryAt = lastFailure + TimeSpan.FromMinutes(Math.Min(5, interval.TotalMinutes));
            if (retryAt > due) due = retryAt;
            _snapshots.NextScheduled = due;

            if (DateTimeOffset.UtcNow >= due && !_snapshots.IsRunning)
            {
                try
                {
                    await _snapshots.TakeAsync(ct);
                    lastFailure = DateTimeOffset.MinValue;
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    lastFailure = DateTimeOffset.UtcNow;
                    _log.LogDebug("Scheduled snapshot failed: {Message}", e.Message);
                }
                continue;
            }

            var wait = due - DateTimeOffset.UtcNow;
            await Task.Delay(wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait > TimeSpan.FromSeconds(15) ? TimeSpan.FromSeconds(15) : wait, ct);
        }
    }
}
