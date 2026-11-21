using Poe1.Infrastructure;

namespace Poe1.Pricing;

/// <summary>Keeps the current <see cref="PriceBook"/> and refreshes it from poe.ninja when it gets old.</summary>
public sealed class PriceService
{
    private readonly NinjaClient _ninja;
    private readonly SettingsStore _settings;
    private readonly ILogger<PriceService> _log;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private PriceBook? _book;

    public PriceService(NinjaClient ninja, SettingsStore settings, ILogger<PriceService> log)
    {
        _ninja = ninja;
        _settings = settings;
        _log = log;
    }

    public PriceBook? Current => _book;
    public string? LastError { get; private set; }

    /// <summary>Returns a price book for the configured league, refreshing it if it is older than the configured age.</summary>
    public async Task<PriceBook> GetAsync(CancellationToken ct, bool force = false)
    {
        var s = _settings.Current;
        var maxAge = TimeSpan.FromMinutes(Math.Max(5, s.PriceRefreshMinutes));
        var book = _book;
        if (!force && book is not null && book.League == s.League && DateTimeOffset.UtcNow - book.FetchedAt < maxAge)
            return book;

        await _refresh.WaitAsync(ct);
        try
        {
            book = _book;
            if (!force && book is not null && book.League == s.League && DateTimeOffset.UtcNow - book.FetchedAt < maxAge)
                return book;
            try
            {
                _book = await _ninja.FetchAsync(s.League, ct);
                LastError = null;
                _log.LogInformation("Prices for {League}: {Count} lines, divine = {Divine:F0}c", s.League, _book.Lines.Count, _book.DivineChaos);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                LastError = e.Message;
                _log.LogWarning("Price refresh failed: {Message}", e.Message);
                if (_book is null || _book.League != s.League) throw;
            }
            return _book;
        }
        finally
        {
            _refresh.Release();
        }
    }
}
