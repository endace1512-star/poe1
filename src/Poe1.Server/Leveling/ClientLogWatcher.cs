using System.Text;
using System.Text.RegularExpressions;
using Poe1.Infrastructure;

namespace Poe1.Leveling;

public sealed record AreaEvent(string AreaId, int AreaLevel, string? AreaName, string Ts);
public sealed record LevelEvent(string Character, string? Class, int Level, string Ts);

public sealed record LevelingState(
    bool Watching, string? LogPath, string? Error,
    AreaEvent? Area, LevelEvent? Character);

/// <summary>Parses lines of the game's Client.txt.</summary>
public static partial class ClientLogParser
{
    // 2026/11/20 18:27:43 123456 cff945b9 [DEBUG Client 1234] Generating level 2 area "1_1_2" with seed 1234
    [GeneratedRegex(@"Generating level (\d+) area ""([^""]+)""")]
    private static partial Regex GeneratingRegex();

    // ... [INFO Client 1234] : You have entered The Coast.
    [GeneratedRegex(@"\] : You have entered (.+?)\.\s*$")]
    private static partial Regex EnteredRegex();

    // ... [INFO Client 1234] : Name (Witch) is now level 12
    [GeneratedRegex(@"\] : (.+?) \((\w+)\) is now level (\d+)")]
    private static partial Regex LevelUpRegex();

    [GeneratedRegex(@"^(\d{4}/\d{2}/\d{2} \d{2}:\d{2}:\d{2})")]
    private static partial Regex TimestampRegex();

    public static string Timestamp(string line)
    {
        var m = TimestampRegex().Match(line);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy/MM/dd HH:mm:ss", null, System.Globalization.DateTimeStyles.AssumeLocal, out var t)
            ? SnapshotTime(t)
            : SnapshotTime(DateTime.Now);
    }

    private static string SnapshotTime(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");

    public static (string AreaId, int Level)? ParseGenerating(string line)
    {
        var m = GeneratingRegex().Match(line);
        return m.Success ? (m.Groups[2].Value, int.Parse(m.Groups[1].Value)) : null;
    }

    public static string? ParseEntered(string line)
    {
        var m = EnteredRegex().Match(line);
        return m.Success ? m.Groups[1].Value : null;
    }

    public static LevelEvent? ParseLevelUp(string line)
    {
        var m = LevelUpRegex().Match(line);
        return m.Success ? new LevelEvent(m.Groups[1].Value, m.Groups[2].Value, int.Parse(m.Groups[3].Value), Timestamp(line)) : null;
    }
}

/// <summary>
/// Follows Client.txt and publishes "area" events when you change zone and "level" events on level up.
/// "Generating level N area "ID"" gives the internal area id the leveling route uses; the following
/// "You have entered X." line adds the display name.
/// </summary>
public sealed class ClientLogWatcher : BackgroundService
{
    public static readonly string[] DefaultPaths =
    {
        @"C:\Program Files (x86)\Grinding Gear Games\Path of Exile\logs\Client.txt",
        @"C:\Program Files\Grinding Gear Games\Path of Exile\logs\Client.txt",
        @"C:\Program Files (x86)\Steam\steamapps\common\Path of Exile\logs\Client.txt",
        @"C:\Program Files (x86)\Steam\steamapps\common\Path of Exile\logs\LatestClient.txt",
        @"C:\Program Files\Epic Games\PathOfExile\logs\Client.txt",
    };

    private readonly SettingsStore _settings;
    private readonly EventHub _events;
    private readonly ILogger<ClientLogWatcher> _log;
    private AreaEvent? _area;
    private (string AreaId, int Level, string Ts)? _pending;
    private LevelEvent? _character;
    private string? _path;
    private string? _error;

    public ClientLogWatcher(SettingsStore settings, EventHub events, ILogger<ClientLogWatcher> log)
    {
        _settings = settings;
        _events = events;
        _log = log;
    }

    public LevelingState State => new(_path is not null && _error is null, _path, _error, _area, _character);

    public static string? ResolvePath(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var p = configured.Trim().Trim('"');
            if (Directory.Exists(p)) p = Path.Combine(p, "logs", "Client.txt");
            return File.Exists(p) ? p : null;
        }
        return DefaultPaths.FirstOrDefault(File.Exists);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var configured = _settings.Current.ClientLogPath;
            var path = ResolvePath(configured);
            if (path is null)
            {
                _path = null;
                _error = string.IsNullOrWhiteSpace(configured)
                    ? "Không tìm thấy Client.txt. Nhập đường dẫn trong Cài đặt."
                    : $"Không tìm thấy file: {configured}";
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                continue;
            }

            _path = path;
            _error = null;
            try
            {
                await FollowAsync(path, () => _settings.Current.ClientLogPath != configured, ct);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _error = e.Message;
                _log.LogWarning("Client.txt: {Message}", e.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task FollowAsync(string path, Func<bool> stop, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Replay the tail so we know the current zone right after starting.
        var start = Math.Max(0, fs.Length - 512 * 1024);
        fs.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024);
        if (start > 0) await reader.ReadLineAsync(ct); // partial line
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null) Handle(line, publish: false);
        if (_area is not null) _events.Publish("area", _area);

        var pending = new StringBuilder();
        while (!ct.IsCancellationRequested && !stop())
        {
            if (fs.Length < fs.Position) return; // log was truncated/rotated: reopen
            var chunk = await reader.ReadToEndAsync(ct);
            if (chunk.Length == 0)
            {
                await Task.Delay(250, ct);
                continue;
            }
            pending.Append(chunk);
            var text = pending.ToString();
            var lastNewline = text.LastIndexOf('\n');
            if (lastNewline < 0) continue;
            foreach (var l in text[..lastNewline].Split('\n')) Handle(l.TrimEnd('\r'), publish: true);
            pending.Clear().Append(text[(lastNewline + 1)..]);
        }
    }

    internal void Handle(string line, bool publish)
    {
        if (ClientLogParser.ParseGenerating(line) is { } gen)
        {
            _pending = (gen.AreaId, gen.Level, ClientLogParser.Timestamp(line));
            _area = new AreaEvent(gen.AreaId, gen.Level, null, _pending.Value.Ts);
            if (publish) _events.Publish("area", _area);
            return;
        }
        if (ClientLogParser.ParseEntered(line) is { } name && _pending is { } p)
        {
            _area = new AreaEvent(p.AreaId, p.Level, name, p.Ts);
            _pending = null;
            if (publish) _events.Publish("area", _area);
            return;
        }
        if (ClientLogParser.ParseLevelUp(line) is { } lvl)
        {
            _character = lvl;
            if (publish) _events.Publish("level", lvl);
        }
    }
}
