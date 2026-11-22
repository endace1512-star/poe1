using Poe1.Builds;
using Poe1.Infrastructure;
using Poe1.Leveling;
using Poe1.Poe;
using Poe1.Pricing;
using Poe1.Wealth;

namespace Poe1.Api;

public sealed record SettingsDto(
    string AccountName, string Realm, string League, bool HasSessionId,
    bool AutoSnapshot, int SnapshotIntervalMinutes, List<string> SelectedTabIds,
    bool IncludeCharacters, List<string> SelectedCharacters,
    int PriceRefreshMinutes, int MinListingCount,
    double AlertMinChangePercent, double AlertMinHeldChaos, int AlertCompareHours, bool AlertDesktopNotify,
    string ClientLogPath, string GamePath, string DataDir);

/// <summary>All fields optional. SessionId: null keeps the stored one, "" clears it.</summary>
public sealed record SettingsUpdate(
    string? AccountName, string? Realm, string? League, string? SessionId,
    bool? AutoSnapshot, int? SnapshotIntervalMinutes, List<string>? SelectedTabIds,
    bool? IncludeCharacters, List<string>? SelectedCharacters,
    int? PriceRefreshMinutes, int? MinListingCount,
    double? AlertMinChangePercent, double? AlertMinHeldChaos, int? AlertCompareHours, bool? AlertDesktopNotify,
    string? ClientLogPath, string? GamePath);

public sealed record FarmStartRequest(string? Name);
public sealed record FarmUpdateRequest(string Name, string? Notes);
public sealed record SimulateAreaRequest(string AreaId, int? Level);
public sealed record BuildImportRequest(string Input);
public sealed record BuildUpdateRequest(string? Name, string? Character);
public sealed record BuildIgnoreRequest(string Key, string? Reason);

public static class Endpoints
{
    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/events", (HttpContext ctx, EventHub hub) => hub.StreamAsync(ctx, ctx.RequestAborted));

        // ---- Settings ----
        api.MapGet("/settings", (SettingsStore s, AppPaths paths) => ToDto(s.Current, paths));
        api.MapPut("/settings", (SettingsUpdate u, SettingsStore store, AppPaths paths) =>
        {
            store.Update(s =>
            {
                if (u.AccountName is not null) s.AccountName = u.AccountName.Trim();
                if (u.Realm is not null) s.Realm = u.Realm.Trim();
                if (u.League is not null) s.League = u.League.Trim();
                if (u.SessionId is not null) s.SessionId = u.SessionId.Trim();
                if (u.AutoSnapshot is not null) s.AutoSnapshot = u.AutoSnapshot.Value;
                if (u.SnapshotIntervalMinutes is not null) s.SnapshotIntervalMinutes = Math.Clamp(u.SnapshotIntervalMinutes.Value, 5, 1440);
                if (u.SelectedTabIds is not null) s.SelectedTabIds = u.SelectedTabIds;
                if (u.IncludeCharacters is not null) s.IncludeCharacters = u.IncludeCharacters.Value;
                if (u.SelectedCharacters is not null) s.SelectedCharacters = u.SelectedCharacters;
                if (u.PriceRefreshMinutes is not null) s.PriceRefreshMinutes = Math.Clamp(u.PriceRefreshMinutes.Value, 5, 1440);
                if (u.MinListingCount is not null) s.MinListingCount = Math.Clamp(u.MinListingCount.Value, 0, 100);
                if (u.AlertMinChangePercent is not null) s.AlertMinChangePercent = Math.Max(0, u.AlertMinChangePercent.Value);
                if (u.AlertMinHeldChaos is not null) s.AlertMinHeldChaos = Math.Max(0, u.AlertMinHeldChaos.Value);
                if (u.AlertCompareHours is not null) s.AlertCompareHours = Math.Clamp(u.AlertCompareHours.Value, 1, 24 * 30);
                if (u.AlertDesktopNotify is not null) s.AlertDesktopNotify = u.AlertDesktopNotify.Value;
                if (u.ClientLogPath is not null) s.ClientLogPath = u.ClientLogPath.Trim();
                if (u.GamePath is not null) s.GamePath = u.GamePath.Trim();
            });
            return ToDto(store.Current, paths);
        });

        // ---- Account data (pass-through to pathofexile.com) ----
        api.MapGet("/leagues", async (PoeApiClient poe, CancellationToken ct) =>
        {
            try { return Results.Ok(await poe.GetLeaguesAsync(ct)); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
            {
                return Results.Ok(new[] { "Standard", "Hardcore" });
            }
        });
        api.MapGet("/stash/tabs", (PoeApiClient poe, CancellationToken ct) => Guard(async () =>
            (await poe.GetStashTabsAsync(ct)).Select(t => new { t.Id, t.Name, t.Index, t.Type, colour = t.Colour?.ToHex() })));
        api.MapGet("/characters", (PoeApiClient poe, CancellationToken ct) => Guard(async () => await poe.GetCharactersAsync(ct)));

        // ---- Snapshots & wealth ----
        api.MapGet("/status", (SnapshotService s) => s.Status());
        api.MapPost("/snapshots", (SnapshotService s, CancellationToken ct) => Guard(async () =>
        {
            var id = await s.TakeAsync(ct);
            return id is null ? (object)new { started = false, message = "Đang có snapshot chạy." } : new { started = true, id };
        }));
        api.MapDelete("/snapshots/{id:long}", (long id, SnapshotRepository repo) => { repo.Delete(id); return Results.NoContent(); });

        api.MapGet("/overview", (SettingsStore settings, SnapshotRepository repo, int? top) =>
        {
            var league = settings.Current.League;
            var latest = repo.Latest(league);
            if (latest is null) return Results.Ok(new { league, snapshot = (SnapshotRow?)null });
            var history = repo.History(league, null);
            double? perHour = null;
            if (history.Count >= 2)
            {
                var first = history[0];
                var hours = (DateTimeOffset.Parse(latest.Ts) - DateTimeOffset.Parse(first.Ts)).TotalHours;
                if (hours > 0.05) perHour = (latest.TotalChaos - first.TotalChaos) / hours;
            }
            return Results.Ok(new
            {
                league,
                snapshot = latest,
                perHourChaos = perHour,
                sources = repo.Sources(latest.Id),
                topItems = repo.Items(latest.Id).Take(top ?? 15),
            });
        });

        api.MapGet("/history", (SettingsStore settings, SnapshotRepository repo, double? hours) =>
            repo.History(settings.Current.League, hours is > 0 ? DateTimeOffset.UtcNow - TimeSpan.FromHours(hours.Value) : null));

        api.MapGet("/sources", (SettingsStore settings, SnapshotRepository repo) =>
        {
            var latest = repo.Latest(settings.Current.League);
            return latest is null ? new List<SourceRow>() : repo.Sources(latest.Id);
        });

        api.MapGet("/items", (SettingsStore settings, SnapshotRepository repo, string? source, string? search, string? category, double? minValue) =>
        {
            var latest = repo.Latest(settings.Current.League);
            if (latest is null) return new List<ItemRow>();
            IEnumerable<ItemRow> items = repo.Items(latest.Id, string.IsNullOrEmpty(source) ? null : source);
            if (!string.IsNullOrWhiteSpace(search)) items = items.Where(i => i.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(category)) items = items.Where(i => i.Category == category);
            if (minValue is > 0) items = items.Where(i => i.TotalChaos >= minValue);
            return items.ToList();
        });

        api.MapGet("/alerts", (SettingsStore settings, PriceAlerts alerts, double? pct, double? minHeld, int? hours) =>
        {
            var s = settings.Current;
            return alerts.Compute(s.League, pct ?? s.AlertMinChangePercent, minHeld ?? s.AlertMinHeldChaos, hours ?? s.AlertCompareHours);
        });

        // ---- Prices ----
        api.MapGet("/prices/search", async (string q, PriceService prices, CancellationToken ct) =>
        {
            var book = await prices.GetAsync(ct);
            return book.Search(q).Select(l => new { l.Name, l.Type, l.BaseType, l.Variant, l.ChaosValue, l.Icon });
        });
        api.MapPost("/prices/refresh", (PriceService prices, CancellationToken ct) => Guard(async () =>
        {
            var book = await prices.GetAsync(ct, force: true);
            return new { lines = book.Lines.Count, divineChaos = book.DivineChaos, fetchedAt = SnapshotRepository.Iso(book.FetchedAt) };
        }));

        // ---- Farm sessions ----
        api.MapGet("/farm", (SettingsStore settings, FarmService farm) => farm.List(settings.Current.League));
        api.MapGet("/farm/{id:long}", (long id, FarmService farm) => farm.Report(id) is { } r ? Results.Ok(r) : Results.NotFound());
        api.MapPost("/farm/start", (FarmStartRequest req, SettingsStore settings, FarmService farm, SnapshotService snaps, SnapshotRepository repo, CancellationToken ct) => Guard(async () =>
        {
            var league = settings.Current.League;
            if (farm.Active(league) is { } active) throw new InvalidOperationException($"Phiên \"{active.Name}\" đang chạy.");
            var snapshotId = await FreshSnapshot(snaps, repo, league, ct);
            var name = string.IsNullOrWhiteSpace(req.Name) ? $"Phiên {DateTime.Now:dd/MM HH:mm}" : req.Name.Trim();
            return new { id = farm.Start(name, league, snapshotId) };
        }));
        api.MapPost("/farm/{id:long}/stop", (long id, FarmService farm, SnapshotService snaps, SnapshotRepository repo, CancellationToken ct) => Guard(async () =>
        {
            var session = farm.Get(id) ?? throw new InvalidOperationException("Không tìm thấy phiên.");
            var snapshotId = await FreshSnapshot(snaps, repo, session.League, ct);
            farm.Stop(id, snapshotId);
            return farm.Report(id)!;
        }));
        api.MapPut("/farm/{id:long}", (long id, FarmUpdateRequest req, FarmService farm) => { farm.Update(id, req.Name, req.Notes); return Results.NoContent(); });
        api.MapDelete("/farm/{id:long}", (long id, FarmService farm) => { farm.Delete(id); return Results.NoContent(); });

        // ---- Build comparison ----
        api.MapGet("/builds", (BuildService b) => b.List());
        api.MapPost("/builds", (BuildImportRequest req, BuildService b, CancellationToken ct) => Guard(async () => await b.ImportAsync(req.Input, ct)));
        api.MapPut("/builds/{id:long}", (long id, BuildUpdateRequest req, BuildService b) => { b.Update(id, req.Name, req.Character); return Results.NoContent(); });
        api.MapDelete("/builds/{id:long}", (long id, BuildService b) => { b.Delete(id); return Results.NoContent(); });
        api.MapPut("/builds/{id:long}/ignore", (long id, BuildIgnoreRequest req, BuildService b) => { b.SetIgnore(id, req.Key, req.Reason); return Results.NoContent(); });
        api.MapGet("/builds/characters", (BuildService b, CancellationToken ct) => Guard(async () => await b.CharactersAsync(ct)));
        api.MapGet("/builds/{id:long}/report", (long id, string? character, int? spec, int? itemSet, int? skillSet, bool? refresh, BuildService b, CancellationToken ct) =>
            Guard(async () => await b.ReportAsync(id, character, spec, itemSet, skillSet, refresh ?? false, ct)));

        // ---- Leveling ----
        api.MapGet("/leveling/state", (ClientLogWatcher w) => w.State);
        // Lets the UI (and tests) move the guide without the game running.
        api.MapPost("/leveling/simulate", (SimulateAreaRequest req, EventHub hub) =>
        {
            hub.Publish("area", new AreaEvent(req.AreaId, req.Level ?? 1, null, SnapshotRepository.Iso(DateTimeOffset.UtcNow)));
            return Results.NoContent();
        });
    }

    /// <summary>A snapshot no older than two minutes, taking one if needed.</summary>
    private static async Task<long> FreshSnapshot(SnapshotService snaps, SnapshotRepository repo, string league, CancellationToken ct)
    {
        var latest = repo.Latest(league);
        if (latest is not null && DateTimeOffset.UtcNow - DateTimeOffset.Parse(latest.Ts) < TimeSpan.FromMinutes(2))
            return latest.Id;
        var id = await snaps.TakeAsync(ct);
        if (id is null)
        {
            // Another snapshot is running: wait for it.
            while (snaps.IsRunning) await Task.Delay(500, ct);
            id = repo.Latest(league)?.Id;
        }
        return id ?? throw new InvalidOperationException("Không chụp được snapshot.");
    }

    private static async Task<IResult> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return Results.Ok(await action());
        }
        catch (PoeApiException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (InvalidOperationException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status409Conflict);
        }
        catch (FormatException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (KeyNotFoundException e)
        {
            return Results.Json(new { error = e.Message }, statusCode: StatusCodes.Status404NotFound);
        }
        catch (HttpRequestException e)
        {
            return Results.Json(new { error = $"Lỗi mạng: {e.Message}" }, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static SettingsDto ToDto(AppSettings s, AppPaths paths) => new(
        s.AccountName, s.Realm, s.League, !string.IsNullOrEmpty(s.SessionId),
        s.AutoSnapshot, s.SnapshotIntervalMinutes, s.SelectedTabIds,
        s.IncludeCharacters, s.SelectedCharacters,
        s.PriceRefreshMinutes, s.MinListingCount,
        s.AlertMinChangePercent, s.AlertMinHeldChaos, s.AlertCompareHours, s.AlertDesktopNotify,
        s.ClientLogPath, s.GamePath, paths.DataDir);
}
