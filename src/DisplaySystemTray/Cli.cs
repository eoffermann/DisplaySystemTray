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

                case "--validate" when args.Length > 1 && Enum.TryParse(args[1], ignoreCase: true, out DisplayMode mode):
                    bool ok = DisplayTopology.Validate(mode);
                    Console.WriteLine($"Validate {mode}: {(ok ? "OK" : "not applicable")}");
                    return ok ? 0 : 1;

                case "--apply" when args.Length > 1 && Enum.TryParse(args[1], ignoreCase: true, out DisplayMode applyMode):
                    Console.WriteLine($"Applying {applyMode}...");
                    DisplayTopology.Apply(applyMode);
                    Console.WriteLine($"Applied {applyMode}.");
                    return 0;

                case "--selftest":
                    return SelfTest();

                default:
                    Console.WriteLine("Usage: DisplaySystemTray [--current | --validate <mode> | --apply <mode> | --selftest]");
                    Console.WriteLine("Modes: extend, internal, external, clone. No arguments starts the tray app.");
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR: {ex.Message}");
            return 3;
        }
    }

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
