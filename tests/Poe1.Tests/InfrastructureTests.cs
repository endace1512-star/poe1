using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Poe1.Infrastructure;
using Poe1.Leveling;
using Poe1.Poe;

namespace Poe1.Tests;

public class RateLimiterTests
{
    private sealed class FakeTime : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 11, 21, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Stays_one_below_learned_limit()
    {
        var time = new FakeTime();
        var limiter = new RateLimiter(time);
        var headers = new Dictionary<string, string>
        {
            ["X-Rate-Limit-Rules"] = "Account",
            ["X-Rate-Limit-Account"] = "3:10:60",
            ["X-Rate-Limit-Account-State"] = "1:10:0",
        };
        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(TimeSpan.Zero, limiter.DelayBeforeNext());
            limiter.RecordRequest();
            limiter.Observe(HttpStatusCode.OK, h => headers.GetValueOrDefault(h));
            time.Now += TimeSpan.FromSeconds(1);
        }
        // Two requests in the 10 s window; the limit is 3, so we wait for the first to leave the window.
        Assert.Equal(TimeSpan.FromSeconds(8), limiter.DelayBeforeNext());
    }

    [Fact]
    public void Honours_restriction_and_retry_after()
    {
        var time = new FakeTime();
        var limiter = new RateLimiter(time);
        limiter.Observe(HttpStatusCode.TooManyRequests, h => h == "Retry-After" ? "42" : null);
        Assert.Equal(TimeSpan.FromSeconds(42), limiter.DelayBeforeNext());

        limiter.Observe(HttpStatusCode.OK, h => h switch
        {
            "X-Rate-Limit-Rules" => "Ip",
            "X-Rate-Limit-Ip" => "45:60:120",
            "X-Rate-Limit-Ip-State" => "46:60:120",
            _ => null,
        });
        Assert.Equal(TimeSpan.FromSeconds(120), limiter.DelayBeforeNext());
    }
}

public class ClientLogTests
{
    [Fact]
    public void Parses_area_generation_entry_and_level_up()
    {
        Assert.Equal(("1_1_2", 2), ClientLogParser.ParseGenerating("""2026/11/20 18:27:43 123 abc [DEBUG Client 1234] Generating level 2 area "1_1_2" with seed 99"""));
        Assert.Equal("The Coast", ClientLogParser.ParseEntered("2026/11/20 18:27:44 124 abc [INFO Client 1234] : You have entered The Coast."));
        var lvl = ClientLogParser.ParseLevelUp("2026/11/20 18:30:00 125 abc [INFO Client 1234] : SomeWitch (Witch) is now level 12");
        Assert.Equal(new LevelEvent("SomeWitch", "Witch", 12, lvl!.Ts), lvl);
        Assert.Null(ClientLogParser.ParseEntered("2026/11/20 18:27:44 124 abc [INFO Client 1234] @From x: You have entered The Coast."));
    }

    [Fact]
    public void Watcher_combines_id_and_name()
    {
        var dir = Directory.CreateTempSubdirectory();
        var watcher = new ClientLogWatcher(new SettingsStore(new AppPaths(dir.FullName)), new EventHub(), NullLogger<ClientLogWatcher>.Instance);
        watcher.Handle("""2026/11/20 18:27:43 123 abc [DEBUG Client 1234] Generating level 13 area "1_1_7_1" with seed 1""", publish: false);
        watcher.Handle("2026/11/20 18:27:44 124 abc [INFO Client 1234] : You have entered The Lower Prison.", publish: false);
        Assert.Equal("1_1_7_1", watcher.State.Area!.AreaId);
        Assert.Equal(13, watcher.State.Area.AreaLevel);
        Assert.Equal("The Lower Prison", watcher.State.Area.AreaName);
    }
}

public class SettingsTests
{
    [Fact]
    public void Session_id_round_trips_and_is_not_stored_in_plain_text()
    {
        var dir = Directory.CreateTempSubdirectory();
        var paths = new AppPaths(dir.FullName);
        new SettingsStore(paths).Update(s => { s.SessionId = "abc123secret"; s.AccountName = "Someone#1234"; });
        Assert.DoesNotContain("abc123secret", File.ReadAllText(paths.SettingsFile));
        var reloaded = new SettingsStore(paths).Current;
        Assert.Equal("abc123secret", reloaded.SessionId);
        Assert.Equal("Someone#1234", reloaded.AccountName);
    }
}
