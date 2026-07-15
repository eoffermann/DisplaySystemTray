namespace DisplaySystemTray.Config;

/// <summary>Root of the persisted JSON document.</summary>
internal sealed class AppConfig
{
    /// <summary>Bump when the shape changes; keep a migration path (see CLAUDE.md).</summary>
    public int SchemaVersion { get; set; } = 1;

    public List<SavedConfiguration> Configurations { get; set; } = [];
}

/// <summary>
/// A captured display topology: everything QueryDisplayConfig reported for the
/// active paths, flattened into JSON-friendly DTOs, plus display metadata for
/// the UI and for re-mapping adapter LUIDs at restore time (LUIDs are not
/// stable across reboots).
/// </summary>
internal sealed class SavedConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CapturedAt { get; set; }
    public List<string> MonitorNames { get; set; } = [];
    public List<PathDto> Paths { get; set; } = [];
    public List<ModeDto> Modes { get; set; } = [];
}

/// <summary>Flattened DISPLAYCONFIG_PATH_INFO.</summary>
internal sealed class PathDto
{
    public uint SourceAdapterLow { get; set; }
    public int SourceAdapterHigh { get; set; }
    public uint SourceId { get; set; }
    public uint SourceModeIdx { get; set; }
    public uint SourceStatusFlags { get; set; }

    public uint TargetAdapterLow { get; set; }
    public int TargetAdapterHigh { get; set; }
    public uint TargetId { get; set; }
    public uint TargetModeIdx { get; set; }
    public uint OutputTechnology { get; set; }
    public uint Rotation { get; set; }
    public uint Scaling { get; set; }
    public uint RefreshRateNumerator { get; set; }
    public uint RefreshRateDenominator { get; set; }
    public uint ScanLineOrdering { get; set; }
    public int TargetAvailable { get; set; }
    public uint TargetStatusFlags { get; set; }

    public uint Flags { get; set; }

    /// <summary>Stable monitor identity used to re-map adapter LUIDs at restore time.</summary>
    public string TargetDevicePath { get; set; } = string.Empty;
}

/// <summary>
/// Flattened DISPLAYCONFIG_MODE_INFO. Exactly one of the Source*/Target* groups
/// is meaningful, selected by <see cref="InfoType"/> (1 = source, 2 = target).
/// </summary>
internal sealed class ModeDto
{
    public uint InfoType { get; set; }
    public uint Id { get; set; }
    public uint AdapterLow { get; set; }
    public int AdapterHigh { get; set; }

    // Source mode
    public uint SourceWidth { get; set; }
    public uint SourceHeight { get; set; }
    public uint SourcePixelFormat { get; set; }
    public int SourcePositionX { get; set; }
    public int SourcePositionY { get; set; }

    // Target mode (video signal)
    public ulong TargetPixelRate { get; set; }
    public uint TargetHSyncNumerator { get; set; }
    public uint TargetHSyncDenominator { get; set; }
    public uint TargetVSyncNumerator { get; set; }
    public uint TargetVSyncDenominator { get; set; }
    public uint TargetActiveCx { get; set; }
    public uint TargetActiveCy { get; set; }
    public uint TargetTotalCx { get; set; }
    public uint TargetTotalCy { get; set; }
    public uint TargetVideoStandard { get; set; }
    public uint TargetScanLineOrdering { get; set; }
}
