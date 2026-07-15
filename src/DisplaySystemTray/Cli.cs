using DisplaySystemTray.Config;
using DisplaySystemTray.Display;

namespace DisplaySystemTray;

/// <summary>
/// Minimal command-line surface, used for scripted verification of the display
/// layer without the tray UI. Output goes to stdout, which is only visible when
/// the parent process redirects it (this is a WinExe with no console of its own).
/// </summary>
internal static class Cli
{
    public static int Run(string[] args)
    {
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "--current":
                    Console.WriteLine($"Current topology: {DisplayTopology.GetCurrent()?.ToString() ?? "(none/custom)"}");
                    return 0;

                case "--validate" when args.Length > 1 && Enum.TryParse(args[1], ignoreCase: true, out DisplayMode mode) && Enum.IsDefined(mode):
                    bool ok = DisplayTopology.Validate(mode);
                    Console.WriteLine($"Validate {mode}: {(ok ? "OK" : "not applicable")}");
                    return ok ? 0 : 1;

                case "--apply" when args.Length > 1 && Enum.TryParse(args[1], ignoreCase: true, out DisplayMode applyMode) && Enum.IsDefined(applyMode):
                    Console.WriteLine($"Applying {applyMode}...");
                    DisplayTopology.Apply(applyMode);
                    Console.WriteLine($"Applied {applyMode}.");
                    return 0;

                case "--selftest":
                    return SelfTest();

                case "--list":
                    return ListSaved();

                case "--save" when args.Length > 1:
                    return SaveCurrent(args[1]);

                case "--restore" when args.Length > 1:
                    return Restore(args[1]);

                case "--delete" when args.Length > 1:
                    return Delete(args[1]);

                default:
                    Console.Error.WriteLine("Usage: DisplaySystemTray [--current | --validate <mode> | --apply <mode> | --selftest");
                    Console.Error.WriteLine("                         | --list | --save <name> | --restore <name> | --delete <name>]");
                    Console.Error.WriteLine("Modes: extend, internal, external, clone. No arguments starts the tray app.");
                    Console.Error.WriteLine("Exit codes: 0 success, 1 operation failed or not applicable, 2 usage error, 3 unexpected error.");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ERROR: {ex.Message}");
            return 3;
        }
    }

    private static int ListSaved()
    {
        var store = ConfigStore.Load();
        if (store.Config.Configurations.Count == 0)
        {
            Console.WriteLine("No saved configurations.");
            return 0;
        }

        foreach (SavedConfiguration saved in store.Config.Configurations)
        {
            Console.WriteLine($"{saved.Name}  [{string.Join(", ", saved.MonitorNames)}]  saved {saved.CapturedAt:u}  id={saved.Id}");
        }

        return 0;
    }

    private static int SaveCurrent(string name)
    {
        var store = ConfigStore.Load();
        SavedConfiguration saved = DisplayConfigSnapshot.Capture(name);
        store.Add(saved);
        Console.WriteLine($"Saved \"{name}\": {saved.Paths.Count} path(s), {saved.Modes.Count} mode(s), monitors: {string.Join(", ", saved.MonitorNames)}");
        return 0;
    }

    private static int Restore(string name)
    {
        var store = ConfigStore.Load();
        SavedConfiguration? saved = FindByName(store, name);
        if (saved is null)
        {
            Console.Error.WriteLine($"No saved configuration named \"{name}\".");
            return 1;
        }

        Console.WriteLine($"Applying \"{saved.Name}\"...");
        DisplayConfigSnapshot.Apply(saved);
        Console.WriteLine("Applied.");
        return 0;
    }

    private static int Delete(string name)
    {
        var store = ConfigStore.Load();
        SavedConfiguration? saved = FindByName(store, name);
        if (saved is null)
        {
            Console.Error.WriteLine($"No saved configuration named \"{name}\".");
            return 1;
        }

        store.Remove(saved.Id);
        Console.WriteLine($"Deleted \"{saved.Name}\".");
        return 0;
    }

    private static SavedConfiguration? FindByName(ConfigStore store, string name) =>
        store.Config.Configurations.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Exercises the display layer end-to-end with minimal disruption: validates
    /// every topology, then re-applies the topology that is already active (a
    /// visual no-op that still runs the full SetDisplayConfig apply path).
    /// </summary>
    private static int SelfTest()
    {
        bool failed = false;

        foreach (DisplayMode mode in Enum.GetValues<DisplayMode>())
        {
            bool ok = DisplayTopology.Validate(mode);
            Console.WriteLine($"validate {mode,-8}: {(ok ? "OK" : "not applicable")}");
        }

        DisplayMode? current = DisplayTopology.GetCurrent();
        Console.WriteLine($"current topology: {current?.ToString() ?? "(none/custom)"}");

        if (current is { } topology)
        {
            try
            {
                DisplayTopology.Apply(topology);
                Console.WriteLine($"re-apply {topology}: OK (no-op)");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"re-apply {topology}: FAILED - {ex.Message}");
                failed = true;
            }
        }
        else
        {
            Console.WriteLine("re-apply: skipped (custom topology, applying could change the layout)");
        }

        Console.WriteLine(failed ? "SELFTEST FAILED" : "SELFTEST PASSED");
        return failed ? 1 : 0;
    }
}
