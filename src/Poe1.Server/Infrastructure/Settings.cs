using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Poe1.Infrastructure;

public sealed class AppSettings
{
    public int Port { get; set; } = 8787;

    // Path of Exile account
    public string AccountName { get; set; } = "";
    public string Realm { get; set; } = "pc";
    public string League { get; set; } = "Standard";

    /// <summary>Stored encrypted (DPAPI on Windows). Never returned by the API.</summary>
    [JsonIgnore] public string SessionId { get; set; } = "";
    public string? SessionIdProtected { get; set; }

    // Snapshots
    public bool AutoSnapshot { get; set; } = true;
    public int SnapshotIntervalMinutes { get; set; } = 15;
    /// <summary>Stash tab ids to include. Empty = all tabs.</summary>
    public List<string> SelectedTabIds { get; set; } = new();
    public bool IncludeCharacters { get; set; } = true;
    /// <summary>Characters to include. Empty = every character in the league.</summary>
    public List<string> SelectedCharacters { get; set; } = new();

    // Pricing
    public int PriceRefreshMinutes { get; set; } = 30;
    /// <summary>Ignore prices with fewer listings than this (poe.ninja stash items only).</summary>
    public int MinListingCount { get; set; } = 3;

    // Price alerts
    public double AlertMinChangePercent { get; set; } = 20;
    public double AlertMinHeldChaos { get; set; } = 20;
    public int AlertCompareHours { get; set; } = 24;
    public bool AlertDesktopNotify { get; set; } = true;

    // Leveling
    public string ClientLogPath { get; set; } = "";

    // Game install (Patch game)
    public string GamePath { get; set; } = "";
}

/// <summary>Loads and saves <see cref="AppSettings"/> as JSON next to the database.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly AppPaths _paths;
    private readonly object _lock = new();
    private AppSettings _current;

    public event Action<AppSettings>? Changed;

    public SettingsStore(AppPaths paths)
    {
        _paths = paths;
        _current = Load();
    }

    public AppSettings Current
    {
        get { lock (_lock) return Clone(_current); }
    }

    public void Update(Action<AppSettings> mutate)
    {
        AppSettings snapshot;
        lock (_lock)
        {
            var next = Clone(_current);
            mutate(next);
            next.SessionIdProtected = Protect(next.SessionId);
            File.WriteAllText(_paths.SettingsFile, JsonSerializer.Serialize(next, JsonOptions));
            _current = next;
            snapshot = Clone(next);
        }
        Changed?.Invoke(snapshot);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_paths.SettingsFile)) return new AppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.SettingsFile)) ?? new AppSettings();
            s.SessionId = Unprotect(s.SessionIdProtected);
            return s;
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    private static AppSettings Clone(AppSettings s)
    {
        var copy = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s))!;
        copy.SessionId = s.SessionId;
        return copy;
    }

    private static readonly byte[] Entropy = "poe1-sessionid"u8.ToArray();

    internal static string? Protect(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = Encoding.UTF8.GetBytes(plain);
        if (OperatingSystem.IsWindows())
            return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        return "plain:" + Convert.ToBase64String(bytes);
    }

    internal static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try
        {
            if (stored.StartsWith("dpapi:") && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored[6..]), Entropy, DataProtectionScope.CurrentUser));
            if (stored.StartsWith("plain:"))
                return Encoding.UTF8.GetString(Convert.FromBase64String(stored[6..]));
        }
        catch (CryptographicException) { }
        catch (FormatException) { }
        return "";
    }
}
