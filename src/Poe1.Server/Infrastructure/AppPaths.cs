namespace Poe1.Infrastructure;

/// <summary>Where the app keeps its settings, database and caches.</summary>
public sealed class AppPaths
{
    public string DataDir { get; }
    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string DatabaseFile => Path.Combine(DataDir, "poe1.db");
    public string PriceCacheDir => Path.Combine(DataDir, "prices");

    public AppPaths(string? overrideDir = null)
    {
        DataDir = overrideDir
            ?? Environment.GetEnvironmentVariable("POE1_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "poe1");
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(PriceCacheDir);
    }
}
