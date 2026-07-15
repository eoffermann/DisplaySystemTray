using System.Text.Json;

namespace DisplaySystemTray.Config;

/// <summary>
/// Loads and saves the app configuration at %APPDATA%\DisplaySystemTray\config.json.
/// Writes are atomic (temp file + rename) so a crash mid-save cannot corrupt the
/// file. A corrupt file on load is set aside (renamed) and a fresh config used.
/// </summary>
internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppConfig Config { get; private set; }

    /// <summary>Non-null when the previous file was unreadable and set aside.</summary>
    public string? LoadWarning { get; }

    /// <summary>Raised after any mutation is persisted.</summary>
    public event EventHandler? Changed;

    private ConfigStore(string filePath, AppConfig config, string? loadWarning)
    {
        _filePath = filePath;
        Config = config;
        LoadWarning = loadWarning;
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Program.AppName,
        "config.json");

    public static ConfigStore Load() => Load(DefaultPath);

    public static ConfigStore Load(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new ConfigStore(filePath, new AppConfig(), loadWarning: null);
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(filePath), JsonOptions);
            if (config is null)
            {
                throw new JsonException("Config deserialized to null.");
            }

            config.Normalize();
            return new ConfigStore(filePath, config, loadWarning: null);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Never crash-loop on a bad file: set it aside so the user can inspect
            // it, and start fresh.
            string quarantined = $"{filePath}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";
            try
            {
                File.Move(filePath, quarantined, overwrite: true);
            }
            catch (Exception moveEx) when (moveEx is IOException or UnauthorizedAccessException)
            {
                quarantined = "(could not be moved aside)";
            }

            return new ConfigStore(
                filePath,
                new AppConfig(),
                $"The configuration file could not be read ({ex.Message}). It was set aside as {quarantined} and a fresh configuration was started.");
        }
    }

    /// <summary>Re-reads the file if another process (e.g. the CLI) changed it.</summary>
    public void ReloadIfChangedExternally()
    {
        if (!File.Exists(_filePath))
        {
            return;
        }

        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_filePath), JsonOptions);
            if (config is not null)
            {
                config.Normalize();
                Config = config;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory config; the file may be mid-write by another process.
        }
    }

    public void Add(SavedConfiguration configuration)
    {
        Config.Configurations.Add(configuration);
        Persist();
    }

    public void Remove(Guid id)
    {
        Config.Configurations.RemoveAll(c => c.Id == id);
        Persist();
    }

    public void Update(SavedConfiguration configuration)
    {
        int index = Config.Configurations.FindIndex(c => c.Id == configuration.Id);
        if (index < 0)
        {
            throw new InvalidOperationException($"No saved configuration with id {configuration.Id}.");
        }

        Config.Configurations[index] = configuration;
        Persist();
    }

    private void Persist()
    {
        string dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        string temp = _filePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Config, JsonOptions));

        if (File.Exists(_filePath))
        {
            File.Replace(temp, _filePath, destinationBackupFileName: null);
        }
        else
        {
            File.Move(temp, _filePath);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
