using System.ComponentModel;
using static DisplaySystemTray.Display.DisplayApi;

namespace DisplaySystemTray.Display;

/// <summary>User-facing quick display modes (the Win+P set).</summary>
internal enum DisplayMode
{
    Extend,
    Internal,
    External,
    Clone,
}

/// <summary>Quick topology switching via SetDisplayConfig SDC_TOPOLOGY_* flags.</summary>
internal static class DisplayTopology
{
    /// <summary>Switches to the given mode. Throws Win32Exception on failure.</summary>
    public static void Apply(DisplayMode mode)
    {
        int result = SetDisplayConfig(0, null, 0, null, ToFlag(mode) | SdcFlags.Apply);
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"Switching displays to {mode} failed (error {result}).");
        }
    }

    /// <summary>Checks whether the given mode can be applied, without applying it.</summary>
    public static bool Validate(DisplayMode mode)
    {
        return SetDisplayConfig(0, null, 0, null, ToFlag(mode) | SdcFlags.Validate) == ErrorSuccess;
    }

    /// <summary>Reads which topology is currently active. Returns null if Windows reports none.</summary>
    public static DisplayMode? GetCurrent()
    {
        (_, _, TopologyId topology) = QueryDisplayPaths(QdcDatabaseCurrent);

        return topology switch
        {
            TopologyId.Extend => DisplayMode.Extend,
            TopologyId.Internal => DisplayMode.Internal,
            TopologyId.External => DisplayMode.External,
            TopologyId.Clone => DisplayMode.Clone,
            _ => null,
        };
    }

    private static SdcFlags ToFlag(DisplayMode mode) => mode switch
    {
        DisplayMode.Extend => SdcFlags.TopologyExtend,
        DisplayMode.Internal => SdcFlags.TopologyInternal,
        DisplayMode.External => SdcFlags.TopologyExternal,
        DisplayMode.Clone => SdcFlags.TopologyClone,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };
}
