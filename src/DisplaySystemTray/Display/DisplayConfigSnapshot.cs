using System.ComponentModel;
using DisplaySystemTray.Config;
using static DisplaySystemTray.Display.DisplayApi;

namespace DisplaySystemTray.Display;

/// <summary>
/// Captures the current display configuration (all active paths and modes) into
/// a <see cref="SavedConfiguration"/>, and applies a saved one back. Adapter
/// LUIDs are not stable across reboots, so targets are identified by their
/// monitor device path and every LUID is re-mapped at apply time.
/// </summary>
internal static class DisplayConfigSnapshot
{
    /// <summary>Captures the currently active display configuration.</summary>
    public static SavedConfiguration Capture(string name)
    {
        (PathInfo[] paths, ModeInfo[] modes) = QueryPaths(QdcOnlyActivePaths);

        var saved = new SavedConfiguration
        {
            Name = name,
            CapturedAt = DateTimeOffset.Now,
        };

        foreach (PathInfo path in paths)
        {
            (string friendlyName, string devicePath) = GetTargetNames(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            if (!string.IsNullOrEmpty(friendlyName))
            {
                saved.MonitorNames.Add(friendlyName);
            }

            saved.Paths.Add(ToDto(path, devicePath));
        }

        foreach (ModeInfo mode in modes)
        {
            saved.Modes.Add(ToDto(mode));
        }

        return saved;
    }

    /// <summary>
    /// Applies a saved configuration. Throws InvalidOperationException when a
    /// saved monitor is not currently connected, Win32Exception when Windows
    /// rejects the configuration.
    /// </summary>
    public static void Apply(SavedConfiguration saved)
    {
        Dictionary<(uint Low, int High), Luid> luidMap = BuildLuidRemap(saved);

        var paths = new PathInfo[saved.Paths.Count];
        for (int i = 0; i < paths.Length; i++)
        {
            paths[i] = FromDto(saved.Paths[i], luidMap);
        }

        var modes = new ModeInfo[saved.Modes.Count];
        for (int i = 0; i < modes.Length; i++)
        {
            modes[i] = FromDto(saved.Modes[i], luidMap);
        }

        int result = SetDisplayConfig(
            (uint)paths.Length,
            paths,
            (uint)modes.Length,
            modes,
            SdcFlags.Apply | SdcFlags.UseSuppliedDisplayConfig | SdcFlags.AllowChanges | SdcFlags.SaveToDatabase);

        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, $"Windows rejected the configuration \"{saved.Name}\" (error {result}).");
        }
    }

    /// <summary>
    /// Maps each adapter LUID stored in the saved configuration to the LUID the
    /// same physical monitor has right now, keyed by monitor device path.
    /// </summary>
    private static Dictionary<(uint Low, int High), Luid> BuildLuidRemap(SavedConfiguration saved)
    {
        (PathInfo[] currentPaths, _) = QueryPaths(QdcAllPaths);

        // Current device path -> current adapter LUID (first match wins; a
        // monitor appears once per source it could attach to, all same adapter).
        var byDevicePath = new Dictionary<string, Luid>(StringComparer.OrdinalIgnoreCase);
        foreach (PathInfo path in currentPaths)
        {
            (_, string devicePath) = GetTargetNames(path.TargetInfo.AdapterId, path.TargetInfo.Id);
            if (!string.IsNullOrEmpty(devicePath) && !byDevicePath.ContainsKey(devicePath))
            {
                byDevicePath[devicePath] = path.TargetInfo.AdapterId;
            }
        }

        var map = new Dictionary<(uint, int), Luid>();
        var missing = new List<string>();
        for (int i = 0; i < saved.Paths.Count; i++)
        {
            PathDto dto = saved.Paths[i];
            if (byDevicePath.TryGetValue(dto.TargetDevicePath, out Luid current))
            {
                map[(dto.TargetAdapterLow, dto.TargetAdapterHigh)] = current;
                // Source sits on the same adapter as its target in every real
                // configuration, so the same LUID mapping covers it.
                map.TryAdd((dto.SourceAdapterLow, dto.SourceAdapterHigh), current);
            }
            else
            {
                missing.Add(saved.MonitorNames.Count > i ? saved.MonitorNames[i] : dto.TargetDevicePath);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"Cannot apply \"{saved.Name}\": these displays are not connected: {string.Join(", ", missing)}.");
        }

        return map;
    }

    /// <summary>
    /// QueryDisplayConfig with the buffer-size retry loop: the display set can
    /// change between sizing and querying, which surfaces as
    /// ERROR_INSUFFICIENT_BUFFER and warrants a fresh attempt.
    /// </summary>
    private static (PathInfo[] Paths, ModeInfo[] Modes) QueryPaths(uint flags)
    {
        for (int attempt = 0; ; attempt++)
        {
            int result = GetDisplayConfigBufferSizes(flags, out uint numPaths, out uint numModes);
            if (result != ErrorSuccess)
            {
                throw new Win32Exception(result, $"GetDisplayConfigBufferSizes failed (error {result}).");
            }

            var paths = new PathInfo[numPaths];
            var modes = new ModeInfo[numModes];
            result = QueryDisplayConfig(flags, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);

            if (result == ErrorInsufficientBuffer && attempt < 3)
            {
                continue; // displays changed between the two calls; retry
            }

            if (result != ErrorSuccess)
            {
                throw new Win32Exception(result, $"QueryDisplayConfig failed (error {result}).");
            }

            // The API may return fewer elements than it sized for; trim.
            Array.Resize(ref paths, (int)numPaths);
            Array.Resize(ref modes, (int)numModes);
            return (paths, modes);
        }
    }

    /// <summary>Friendly name and device path for a target, or empty strings on failure.</summary>
    public static (string FriendlyName, string DevicePath) GetTargetNames(Luid adapterId, uint targetId)
    {
        var request = new TargetDeviceName
        {
            Header = new DeviceInfoHeader
            {
                Type = DeviceInfoType.GetTargetName,
                Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<TargetDeviceName>(),
                AdapterId = adapterId,
                Id = targetId,
            },
        };

        return DisplayConfigGetDeviceInfo(ref request) == ErrorSuccess
            ? (request.MonitorFriendlyDeviceName, request.MonitorDevicePath)
            : (string.Empty, string.Empty);
    }

    private static PathDto ToDto(PathInfo path, string targetDevicePath) => new()
    {
        SourceAdapterLow = path.SourceInfo.AdapterId.LowPart,
        SourceAdapterHigh = path.SourceInfo.AdapterId.HighPart,
        SourceId = path.SourceInfo.Id,
        SourceModeIdx = path.SourceInfo.ModeInfoIdx,
        SourceStatusFlags = path.SourceInfo.StatusFlags,
        TargetAdapterLow = path.TargetInfo.AdapterId.LowPart,
        TargetAdapterHigh = path.TargetInfo.AdapterId.HighPart,
        TargetId = path.TargetInfo.Id,
        TargetModeIdx = path.TargetInfo.ModeInfoIdx,
        OutputTechnology = path.TargetInfo.OutputTechnology,
        Rotation = path.TargetInfo.Rotation,
        Scaling = path.TargetInfo.Scaling,
        RefreshRateNumerator = path.TargetInfo.RefreshRate.Numerator,
        RefreshRateDenominator = path.TargetInfo.RefreshRate.Denominator,
        ScanLineOrdering = path.TargetInfo.ScanLineOrdering,
        TargetAvailable = path.TargetInfo.TargetAvailable,
        TargetStatusFlags = path.TargetInfo.StatusFlags,
        Flags = path.Flags,
        TargetDevicePath = targetDevicePath,
    };

    private static PathInfo FromDto(PathDto dto, Dictionary<(uint, int), Luid> luidMap) => new()
    {
        SourceInfo = new PathSourceInfo
        {
            AdapterId = Remap(dto.SourceAdapterLow, dto.SourceAdapterHigh, luidMap),
            Id = dto.SourceId,
            ModeInfoIdx = dto.SourceModeIdx,
            StatusFlags = dto.SourceStatusFlags,
        },
        TargetInfo = new PathTargetInfo
        {
            AdapterId = Remap(dto.TargetAdapterLow, dto.TargetAdapterHigh, luidMap),
            Id = dto.TargetId,
            ModeInfoIdx = dto.TargetModeIdx,
            OutputTechnology = dto.OutputTechnology,
            Rotation = dto.Rotation,
            Scaling = dto.Scaling,
            RefreshRate = new Rational { Numerator = dto.RefreshRateNumerator, Denominator = dto.RefreshRateDenominator },
            ScanLineOrdering = dto.ScanLineOrdering,
            TargetAvailable = dto.TargetAvailable,
            StatusFlags = dto.TargetStatusFlags,
        },
        Flags = dto.Flags,
    };

    private static ModeDto ToDto(ModeInfo mode)
    {
        var dto = new ModeDto
        {
            InfoType = (uint)mode.InfoType,
            Id = mode.Id,
            AdapterLow = mode.AdapterId.LowPart,
            AdapterHigh = mode.AdapterId.HighPart,
        };

        if (mode.InfoType == ModeInfoType.Source)
        {
            dto.SourceWidth = mode.SourceMode.Width;
            dto.SourceHeight = mode.SourceMode.Height;
            dto.SourcePixelFormat = mode.SourceMode.PixelFormat;
            dto.SourcePositionX = mode.SourceMode.Position.X;
            dto.SourcePositionY = mode.SourceMode.Position.Y;
        }
        else if (mode.InfoType == ModeInfoType.Target)
        {
            VideoSignalInfo signal = mode.TargetMode.TargetVideoSignalInfo;
            dto.TargetPixelRate = signal.PixelRate;
            dto.TargetHSyncNumerator = signal.HSyncFreq.Numerator;
            dto.TargetHSyncDenominator = signal.HSyncFreq.Denominator;
            dto.TargetVSyncNumerator = signal.VSyncFreq.Numerator;
            dto.TargetVSyncDenominator = signal.VSyncFreq.Denominator;
            dto.TargetActiveCx = signal.ActiveSize.Cx;
            dto.TargetActiveCy = signal.ActiveSize.Cy;
            dto.TargetTotalCx = signal.TotalSize.Cx;
            dto.TargetTotalCy = signal.TotalSize.Cy;
            dto.TargetVideoStandard = signal.VideoStandard;
            dto.TargetScanLineOrdering = signal.ScanLineOrdering;
        }
        else
        {
            // QDC_ONLY_ACTIVE_PATHS without virtual-mode awareness only yields
            // source and target entries; anything else means our assumptions broke.
            throw new NotSupportedException($"Unsupported mode info type {mode.InfoType}; cannot capture this configuration.");
        }

        return dto;
    }

    private static ModeInfo FromDto(ModeDto dto, Dictionary<(uint, int), Luid> luidMap)
    {
        var mode = new ModeInfo
        {
            InfoType = (ModeInfoType)dto.InfoType,
            Id = dto.Id,
            AdapterId = Remap(dto.AdapterLow, dto.AdapterHigh, luidMap),
        };

        if (mode.InfoType == ModeInfoType.Source)
        {
            mode.SourceMode = new SourceMode
            {
                Width = dto.SourceWidth,
                Height = dto.SourceHeight,
                PixelFormat = dto.SourcePixelFormat,
                Position = new PointL { X = dto.SourcePositionX, Y = dto.SourcePositionY },
            };
        }
        else
        {
            mode.TargetMode = new TargetMode
            {
                TargetVideoSignalInfo = new VideoSignalInfo
                {
                    PixelRate = dto.TargetPixelRate,
                    HSyncFreq = new Rational { Numerator = dto.TargetHSyncNumerator, Denominator = dto.TargetHSyncDenominator },
                    VSyncFreq = new Rational { Numerator = dto.TargetVSyncNumerator, Denominator = dto.TargetVSyncDenominator },
                    ActiveSize = new Region2D { Cx = dto.TargetActiveCx, Cy = dto.TargetActiveCy },
                    TotalSize = new Region2D { Cx = dto.TargetTotalCx, Cy = dto.TargetTotalCy },
                    VideoStandard = dto.TargetVideoStandard,
                    ScanLineOrdering = dto.TargetScanLineOrdering,
                },
            };
        }

        return mode;
    }

    private static Luid Remap(uint low, int high, Dictionary<(uint, int), Luid> luidMap)
    {
        return luidMap.TryGetValue((low, high), out Luid current)
            ? current
            : new Luid { LowPart = low, HighPart = high };
    }
}
