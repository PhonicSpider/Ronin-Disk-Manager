using System.IO;
using System.Text.Json;

namespace RoninDiskManager.Services;

// ── Persisted application settings ──────────────────────────────────────────
// Stored as JSON under %AppData%\RoninDiskManager\settings.json. Loading never
// throws: a missing or corrupt file yields defaults so the app always starts.
public sealed class AppSettings
{
    /// <summary>How many files the Largest Files tab surfaces.</summary>
    public int LargestFilesCount { get; set; } = 200;

    /// <summary>
    /// True = binary sizes (1 KB = 1024 bytes, the default). False = decimal
    /// (1 KB = 1000 bytes). Controls every size shown in the app.
    /// </summary>
    public bool UseBinaryUnits { get; set; } = true;

    /// <summary>Free-space target in GB used by the Largest Files cumulative view.</summary>
    public double FreeSpaceTargetGb { get; set; } = 10.0;

    /// <summary>Pinned roots for one-click rescans.</summary>
    public List<string> PinnedPaths { get; set; } = [];
}

public static class SettingsService
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoninDiskManager");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private static AppSettings? _current;

    /// <summary>The live settings instance, loaded from disk on first access.</summary>
    public static AppSettings Current => _current ??= Load();

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch { /* fall through to defaults */ }

        return new AppSettings();
    }

    public static void Save()
    {
        if (_current == null) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_current, JsonOpts));
        }
        catch { /* non-fatal: settings are best-effort */ }
    }
}
