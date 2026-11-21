using System.Net;
using System.Text.Json;
using Poe1.Infrastructure;

namespace Poe1.Poe;

public sealed class PoeApiException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
}

/// <summary>
/// Reads stash tabs and characters from pathofexile.com with the user's POESESSID cookie.
/// The cookie never leaves this machine except in requests to pathofexile.com itself.
/// </summary>
public sealed class PoeApiClient
{
    public const string BaseUrl = "https://www.pathofexile.com";
    private readonly HttpClient _http;
    private readonly SettingsStore _settings;
    private readonly RateLimiter _limiter;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public PoeApiClient(HttpClient http, SettingsStore settings, RateLimiter limiter)
    {
        _http = http;
        _settings = settings;
        _limiter = limiter;
    }

    /// <summary>Tab list (with folder children flattened) and the items of tab 0.</summary>
    public async Task<List<StashTabInfo>> GetStashTabsAsync(CancellationToken ct)
    {
        var s = RequireAccount();
        var res = await GetAsync<StashResponse>(StashUrl(s, 0, includeTabs: true), s, ct);
        return Flatten(res.Tabs ?? new()).Where(t => !t.Hidden).ToList();
    }

    public async Task<List<PoeItem>> GetStashItemsAsync(int tabIndex, CancellationToken ct)
    {
        var s = RequireAccount();
        var res = await GetAsync<StashResponse>(StashUrl(s, tabIndex, includeTabs: false), s, ct);
        return res.Items;
    }

    public async Task<List<CharacterInfo>> GetCharactersAsync(CancellationToken ct)
    {
        var s = RequireAccount();
        var url = $"{BaseUrl}/character-window/get-characters?accountName={Uri.EscapeDataString(s.AccountName)}&realm={Uri.EscapeDataString(s.Realm)}";
        return await GetAsync<List<CharacterInfo>>(url, s, ct);
    }

    public async Task<List<PoeItem>> GetCharacterItemsAsync(string character, CancellationToken ct)
    {
        var s = RequireAccount();
        var url = $"{BaseUrl}/character-window/get-items?accountName={Uri.EscapeDataString(s.AccountName)}&realm={Uri.EscapeDataString(s.Realm)}&character={Uri.EscapeDataString(character)}";
        var res = await GetAsync<CharacterItemsResponse>(url, s, ct);
        return res.Items;
    }

    public async Task<List<string>> GetLeaguesAsync(CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/leagues?type=main&realm=pc");
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToList();
    }

    private static string StashUrl(AppSettings s, int tabIndex, bool includeTabs) =>
        $"{BaseUrl}/character-window/get-stash-items?accountName={Uri.EscapeDataString(s.AccountName)}" +
        $"&realm={Uri.EscapeDataString(s.Realm)}&league={Uri.EscapeDataString(s.League)}&tabs={(includeTabs ? 1 : 0)}&tabIndex={tabIndex}";

    private AppSettings RequireAccount()
    {
        var s = _settings.Current;
        if (string.IsNullOrWhiteSpace(s.AccountName)) throw new PoeApiException("Chưa nhập tên tài khoản (Cài đặt).");
        if (string.IsNullOrWhiteSpace(s.SessionId)) throw new PoeApiException("Chưa nhập POESESSID (Cài đặt).");
        return s;
    }

    private static IEnumerable<StashTabInfo> Flatten(IEnumerable<StashTabInfo> tabs)
    {
        foreach (var t in tabs)
        {
            if (t.Children is { Count: > 0 })
            {
                foreach (var c in Flatten(t.Children)) yield return c;
            }
            else if (t.Type != "Folder")
            {
                yield return t;
            }
        }
    }

    private async Task<T> GetAsync<T>(string url, AppSettings s, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var delay = _limiter.DelayBeforeNext();
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.Add("Cookie", $"POESESSID={s.SessionId}");
                _limiter.RecordRequest();
                using var res = await _http.SendAsync(req, ct);
                _limiter.Observe(res.StatusCode, name =>
                    res.Headers.TryGetValues(name, out var v) ? string.Join(",", v) : null);

                if (res.StatusCode == HttpStatusCode.TooManyRequests && attempt < 2) continue;
                var body = await res.Content.ReadAsStringAsync(ct);
                switch (res.StatusCode)
                {
                    case HttpStatusCode.OK:
                        return JsonSerializer.Deserialize<T>(body) ?? throw new PoeApiException("Phản hồi rỗng từ pathofexile.com");
                    case HttpStatusCode.Unauthorized:
                    case HttpStatusCode.Forbidden:
                        throw new PoeApiException("pathofexile.com từ chối truy cập: POESESSID sai hoặc đã hết hạn, hoặc tên tài khoản không khớp (nhớ ghi cả phần #1234).", res.StatusCode);
                    case HttpStatusCode.NotFound:
                        throw new PoeApiException("Không tìm thấy dữ liệu: kiểm tra tên tài khoản, league và realm.", res.StatusCode);
                    case HttpStatusCode.TooManyRequests:
                        throw new PoeApiException("Bị giới hạn tốc độ (rate limit) bởi pathofexile.com, thử lại sau.", res.StatusCode);
                    default:
                        throw new PoeApiException($"pathofexile.com trả về {(int)res.StatusCode}.", res.StatusCode);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
