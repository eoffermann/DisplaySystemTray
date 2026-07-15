using System.Text.Json;

namespace DisplaySystemTray.Config;

/// <summary>
/// Loads and saves the app configuration at %APPDATA%\DisplaySystemTray\config.json.
/// Writes are atomic (unique temp file + rename) so a crash mid-save cannot corrupt
/// the file. File access is serialized across processes with a named mutex where
/// possible - mutations require it and re-read the file under the lock before
/// applying (so concurrent writers cannot lose each other's updates), while reads
/// and startup degrade to best-effort unlocked access rather than fail. A corrupt
/// file on load is set aside (renamed) and a fresh config used; an unreadable one
/// is left in place.
/// </summary>
internal sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    // Best-effort reads run on the UI thread (menu Opening); they must never
    // stall the shell behind a slow lock holder.
    private static readonly TimeSpan ReadLockTimeout = TimeSpan.FromMilliseconds(250);

    // Per-user-SID name for isolation between different users' sessions (e.g.
    // runas); squatting itself is mitigated by the degrade-to-unlocked path in
    // WithFileLock, not by the name.
    private static readonly string LockName = BuildLockName();

    private static string BuildLockName()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string sid = identity.User?.Value ?? identity.Name.Replace('\\', '_');
        return $@"Local\{Program.AppName}_ConfigLock_{sid}";
    }

    private readonly string _filePath;
    private DateTime _lastSeenWriteTimeUtc;

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
        _lastSeenWriteTimeUtc = GetWriteTimeUtc();
    }

    private DateTime GetWriteTimeUtc() =>
        File.Exists(_filePath) ? File.GetLastWriteTimeUtc(_filePath) : default;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Program.AppName,
        "config.json");

    public static ConfigStore Load() => Load(DefaultPath);

    public static ConfigStore Load(string filePath)
    {
        // bestEffort: startup must never die on a contended/squatted lock.
        return WithFileLock(bestEffort: true, action: () =>
        {
            if (!File.Exists(filePath))
            {
                return new ConfigStore(filePath, new AppConfig(), loadWarning: null);
            }

            try
            {
                return new ConfigStore(filePath, ReadConfigNoLock(filePath), loadWarning: null);
            }
            catch (JsonException ex)
            {
                // Genuinely corrupt content: never crash-loop on it - set it aside
                // so the user can inspect it, and start fresh.
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Possibly transient (another process mid-write since best-effort
                // reads may run unlocked, an AV scan, a permission hiccup): the
                // file may be perfectly healthy, so leave it in place. Mutations
                // re-read the disk state under the lock before writing, so the
                // empty in-memory start cannot clobber saved configurations.
                return new ConfigStore(
                    filePath,
                    new AppConfig(),
                    $"The configuration file could not be read ({ex.Message}). It was left in place; saved configurations will reappear once it is readable.");
            }
        });
    }

    /// <summary>Re-reads the file if another process (e.g. the CLI) changed it.</summary>
    public void ReloadIfChangedExternally()
    {
        try
        {
            // Timestamp short-circuit: don't touch the lock at all on the common
            // nothing-changed path (this runs on every tray-menu open).
            if (GetWriteTimeUtc() == _lastSeenWriteTimeUtc)
            {
                return;
            }

            WithFileLock(bestEffort: true, action: () =>
            {
                if (File.Exists(_filePath))
                {
                    Config = ReadConfigNoLock(_filePath);
                    _lastSeenWriteTimeUtc = GetWriteTimeUtc();
                }

                return true;
            });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Keep the in-memory config; the file may be mid-write or briefly
            // locked. (Best-effort locking never throws TimeoutException.)
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
            _lastSeenWriteTimeUtc = GetWriteTimeUtc();
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

    /// <summary>
    /// Serializes config-file access across the tray app and CLI processes.
    /// With <paramref name="bestEffort"/> (reads, startup paths) an unavailable
    /// lock degrades to running unlocked - atomic writes keep the file itself
    /// consistent, only lost-update protection is reduced - so a squatted or
    /// contended lock can never turn every launch into a fatal error. Mutations
    /// pass false and get a TimeoutException the UI/CLI surfaces instead.
    /// </summary>
    private static T WithFileLock<T>(Func<T> action, bool bestEffort = false)
    {
        Mutex? mutex = null;
        bool owned = false;
        bool creatable = true;
        try
        {
            try
            {
                mutex = new Mutex(initiallyOwned: false, LockName);
                try
                {
                    owned = mutex.WaitOne(bestEffort ? ReadLockTimeout : LockTimeout);
                }
                catch (AbandonedMutexException)
                {
                    owned = true; // previous holder died; state on disk is still atomic
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
            {
                creatable = false; // squatted or wrong object type; degrade below
            }

            if (!owned && creatable && !bestEffort)
            {
                throw new TimeoutException("Another process is holding the configuration file lock.");
            }

            return action();
        }
        finally
        {
            if (owned)
            {
                mutex!.ReleaseMutex();
            }

            mutex?.Dispose();
        }
    }
}
