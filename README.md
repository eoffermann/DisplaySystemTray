# DisplaySystemTray

A lightweight Windows system-tray utility for switching display modes and saving/restoring full display configurations — without digging through Settings > System > Display every time.

> **Status:** early development. The plan is finalized (see [PLAN.md](PLAN.md)); implementation is underway. No usable release yet.

## What it does

Click the tray icon (left- or right-click, including from the hidden-icons overflow) to get a menu with:

- **Quick modes** — one-click switching between:
  - **Extend** — desktop spans all displays
  - **Show only on 1** — internal/primary display only
  - **Show only on 2** — external/secondary display only
- **Saved configurations** — your own named display setups, applied with one click
- **Settings…** — manage saved configurations
- **Exit**

### Saved configurations

Rather than reimplementing every display setting, DisplaySystemTray snapshots whatever you've already arranged in Windows Settings — active displays, resolutions, refresh rates, positions, orientation — and restores it on demand. Arrange your displays once, click **Save current as…**, name it (e.g. "Docked", "Couch mode", "Recording"), and it appears in the tray menu. The settings window lets you add, rename, update-from-current, and delete configurations.

## How it works

- C# / .NET 8 (LTS), WinForms `NotifyIcon` tray app — Windows-only.
- Display control talks directly to the Windows CCD API (`QueryDisplayConfig` / `SetDisplayConfig` via P/Invoke). No undocumented `DisplaySwitch.exe` arguments involved.
- Configurations are stored as human-readable JSON at `%APPDATA%\DisplaySystemTray\config.json`.

## Building

Requires the .NET 8 SDK.

```powershell
dotnet build src/DisplaySystemTray
dotnet run --project src/DisplaySystemTray
```

To produce a self-contained single-file exe:

```powershell
dotnet publish src/DisplaySystemTray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

## Roadmap

See [PLAN.md](PLAN.md) for milestones and design details. Post-MVP ideas include Duplicate/clone mode, per-configuration hotkeys, and auto-apply on dock/undock.

## License

TBD.
