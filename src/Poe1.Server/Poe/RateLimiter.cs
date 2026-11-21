using System.Net;

namespace Poe1.Poe;

/// <summary>
/// Client-side rate limiter for pathofexile.com. GGG reports its limits in response headers
/// (X-Rate-Limit-Rules, X-Rate-Limit-{Rule}: max:period:penalty, X-Rate-Limit-{Rule}-State: hits:period:restricted).
/// We learn the limits from those headers and keep our own request timestamps so we always stay one request
/// below every window. Starts conservatively until the first response teaches us the real limits.
/// </summary>
public sealed class RateLimiter
{
    private readonly object _lock = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<string, List<(int Max, int PeriodSec)>> _limits = new();
    private readonly List<DateTimeOffset> _history = new();
    private DateTimeOffset _blockedUntil = DateTimeOffset.MinValue;

    /// <summary>Used until GGG tells us the real limits.</summary>
    private static readonly List<(int, int)> DefaultLimits = new() { (10, 10), (30, 60) };

    public RateLimiter(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>How long to wait before the next request is allowed. Zero when it can go now.</summary>
    public TimeSpan DelayBeforeNext()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var wait = _blockedUntil > now ? _blockedUntil - now : TimeSpan.Zero;
            var windows = _limits.Count == 0 ? DefaultLimits : _limits.Values.SelectMany(x => x).ToList();
            foreach (var (max, period) in windows)
            {
                var allowed = Math.Max(1, max - 1);
                var windowStart = now - TimeSpan.FromSeconds(period);
                var inWindow = _history.Where(t => t > windowStart).OrderBy(t => t).ToList();
                if (inWindow.Count >= allowed)
                {
                    // Wait until the oldest request that keeps us at the limit leaves the window.
                    var release = inWindow[inWindow.Count - allowed] + TimeSpan.FromSeconds(period) - now;
                    if (release > wait) wait = release;
                }
            }
            return wait;
        }
    }

    public void RecordRequest()
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            _history.Add(now);
            var maxPeriod = Math.Max(3600, _limits.Values.SelectMany(x => x).Select(x => x.PeriodSec).DefaultIfEmpty(0).Max());
            _history.RemoveAll(t => t < now - TimeSpan.FromSeconds(maxPeriod));
        }
    }

    /// <summary>Learn limits and penalties from a response.</summary>
    public void Observe(HttpStatusCode status, Func<string, string?> header)
    {
        lock (_lock)
        {
            var now = _time.GetUtcNow();
            var rules = header("X-Rate-Limit-Rules");
            if (!string.IsNullOrEmpty(rules))
            {
                foreach (var rule in rules.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    var limits = Parse(header($"X-Rate-Limit-{rule}"));
                    if (limits.Count > 0) _limits[rule] = limits.Select(l => (l.A, l.B)).ToList();
                    var states = Parse(header($"X-Rate-Limit-{rule}-State"));
                    for (var i = 0; i < states.Count; i++)
                    {
                        var s = states[i];
                        // Third number: seconds we are restricted for after breaking the limit.
                        if (s.C > 0) Block(now + TimeSpan.FromSeconds(s.C));
                        // Requests made elsewhere (e.g. the browser) count too: if the server says we are
                        // at the edge of a window, sit that window out.
                        else if (i < limits.Count && s.A >= limits[i].A - 1) Block(now + TimeSpan.FromSeconds(limits[i].B));
                    }
                }
            }
            if (status == HttpStatusCode.TooManyRequests)
            {
                var retry = int.TryParse(header("Retry-After"), out var sec) ? sec : 60;
                Block(now + TimeSpan.FromSeconds(retry));
            }
        }
    }

    private void Block(DateTimeOffset until)
    {
        if (until > _blockedUntil) _blockedUntil = until;
    }

    private static List<(int A, int B, int C)> Parse(string? value)
    {
        var list = new List<(int, int, int)>();
        if (string.IsNullOrEmpty(value)) return list;
        foreach (var part in value.Split(','))
        {
            var n = part.Split(':');
            if (n.Length == 3 && int.TryParse(n[0], out var a) && int.TryParse(n[1], out var b) && int.TryParse(n[2], out var c))
                list.Add((a, b, c));
        }
        return list;
    }
}
