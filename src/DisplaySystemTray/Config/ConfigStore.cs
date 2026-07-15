using System.Text.Json;

namespace DisplaySystemTray.Config;

/// <summary>
/// Loads and saves the app configuration at %APPDATA%\DisplaySystemTray\config.json.
/// Writes are atomic (unique temp file + rename) so a crash mid-save cannot corrupt
/// the file, and every file access is serialized across processes with a named
/// mutex - the tray app and CLI invocations share this file. Mutations re-read the
/// file under the lock before applying, so concurrent writers cannot lose each
/// other's updates. A corrupt file on load is set aside (renamed) and a fresh
/// config used.
/// </summary>
internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private const string LockName = $@"Local\{Program.AppName}_ConfigLock";

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
        return WithFileLock(() =>
        {
            if (!File.Exists(filePath))
            {
                return new ConfigStore(filePath, new AppConfig(), loadWarning: null);
            }

            try
            {
                return new ConfigStore(filePath, ReadConfigNoLock(filePath), loadWarning: null);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Never crash-loop on a bad file: set it aside so the user can
                // inspect it, and start fresh.
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
        });
    }

    /// <summary>Re-reads the file if another process (e.g. the CLI) changed it.</summary>
    public void ReloadIfChangedExternally()
    {
        try
        {
            WithFileLock(() =>
            {
                if (File.Exists(_filePath))
                {
                    Config = ReadConfigNoLock(_filePath);
                }

                return true;
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            // Keep the in-memory config; the file may be mid-write or briefly locked.
        }
    }

    public void Add(SavedConfiguration configuration) =>
        Mutate(config => config.Configurations.Add(configuration));

    public void Remove(Guid id) =>
        Mutate(config => config.Configurations.RemoveAll(c => c.Id == id));

    public void Update(SavedConfiguration configuration) =>
        Mutate(config =>
        {
            int index = config.Configurations.FindIndex(c => c.Id == configuration.Id);
            if (index < 0)
            {
                throw new InvalidOperationException($"No saved configuration with id {configuration.Id}.");
            }

            config.Configurations[index] = configuration;
        });

    /// <summary>
    /// Applies a mutation with read-modify-write semantics under the cross-process
    /// lock: the on-disk state is re-read first so concurrent processes cannot
    /// overwrite each other's saved configurations.
    /// </summary>
    private void Mutate(Action<AppConfig> mutation)
    {
        WithFileLock(() =>
        {
            if (File.Exists(_filePath))
            {
                try
                {
                    Config = ReadConfigNoLock(_filePath);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // Unreadable disk state: proceed with the in-memory config, the
                    // write below re-establishes a good file.
                }
            }

            mutation(Config);
            PersistNoLock();
            return true;
        });

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static AppConfig ReadConfigNoLock(string filePath)
    {
        var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(filePath), JsonOptions)
            ?? throw new JsonException("Config deserialized to null.");
        config.Normalize();
        return config;
    }

    private void PersistNoLock()
    {
        string dir = Path.GetDirectoryName(_filePath)!;
        Directory.CreateDirectory(dir);

        // Unique temp name: a fixed one would collide between concurrent processes.
        string temp = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(Config, JsonOptions));

            if (File.Exists(_filePath))
            {
                File.Replace(temp, _filePath, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temp, _filePath);
            }
        }
        catch
        {
            // Don't leave temp droppings behind on a failed write.
            try
            {
                File.Delete(temp);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort only.
            }

            throw;
        }
    }

    /// <summary>Serializes config-file access across the tray app and CLI processes.</summary>
    private static T WithFileLock<T>(Func<T> action)
    {
        using var mutex = new Mutex(initiallyOwned: false, LockName);
        bool owned = false;
        try
        {
            try
            {
                owned = mutex.WaitOne(LockTimeout);
            }
            catch (AbandonedMutexException)
            {
                owned = true; // previous holder died; state on disk is still atomic
            }

            if (!owned)
            {
                throw new TimeoutException("Another process is holding the configuration file lock.");
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
