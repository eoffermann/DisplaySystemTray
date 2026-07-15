# DisplaySystemTray

A lightweight Windows system-tray utility for switching display modes and saving/restoring full display configurations — without digging through Settings > System > Display every time.

## Features

Click the tray icon (left- or right-click, including from the hidden-icons overflow) to get a menu with:

- **Quick modes** — one-click switching, equivalent to Win+P but two clicks closer:
  - **Extend** — desktop spans all displays
  - **Show only on 1** — internal/primary display only
  - **Show only on 2** — external/secondary display only
- **Saved configurations** — your own named display setups, applied with one click
- **Settings…** — manage saved configurations, toggle *Start with Windows*
- **Exit**

### Saved configurations

Rather than reimplementing every display setting, DisplaySystemTray snapshots whatever you've already arranged in Windows Settings — active displays, resolutions, refresh rates, positions, orientation — and restores it on demand. Arrange your displays once, open **Settings… → Save current as…**, name it (e.g. "Docked", "Couch mode", "Recording"), and it appears in the tray menu. Rename, re-capture (**Update from current**), or delete entries any time.

Applying a configuration whose monitors are unplugged fails with a message naming the missing displays; nothing changes.

## Installation

Download `DisplaySystemTray-vX.Y.Z-win-x64.zip` from [Releases](https://github.com/eoffermann/DisplaySystemTray/releases), unzip anywhere, run `DisplaySystemTray.exe`. It's a self-contained single file — no .NET install required. Tick *Start with Windows* in Settings to launch it at logon.

## Command-line use

The same exe doubles as a scriptable CLI (output appears when stdout is redirected; handy for hotkey tools and scripts):

```
DisplaySystemTray --apply <extend|internal|external|clone>   switch mode
DisplaySystemTray --current                                  print active topology
DisplaySystemTray --save <name>                              snapshot current config
DisplaySystemTray --restore <name>                           apply a saved config
DisplaySystemTray --list | --delete <name>                   manage saved configs
DisplaySystemTray --validate <mode> | --selftest             checks
DisplaySystemTray --settings                                 open just the settings window
```

Exit codes: `0` success, `1` operation failed/not applicable, `2` usage error, `3` unexpected error.

## How it works

- C# / .NET 8 (LTS), WinForms `NotifyIcon` tray app — Windows-only.
- Display control talks directly to the Windows CCD API (`QueryDisplayConfig` / `SetDisplayConfig` via P/Invoke). No undocumented `DisplaySwitch.exe` arguments involved.
- Configurations are stored as human-readable JSON at `%APPDATA%\DisplaySystemTray\config.json` (atomic writes, cross-process locked). Adapter IDs are re-mapped by monitor device path at restore time, so saved configs survive reboots.

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

See [PLAN.md](PLAN.md) for design details. Post-MVP ideas include Duplicate/clone in the menu, per-configuration hotkeys, and auto-apply on dock/undock.

## License

TBD.
