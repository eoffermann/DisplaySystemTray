# DisplaySystemTray

Windows system-tray utility to switch display modes (Extend / Show only on 1 / Show only on 2) and save/restore named display configurations. See PLAN.md for the full design and milestone status.

## Stack

- C# / .NET 8 (LTS), WinForms (`NotifyIcon` tray app), Windows-only (`net8.0-windows`).
- Display control via P/Invoke to the CCD API (`QueryDisplayConfig` / `SetDisplayConfig` in user32.dll) — no shelling out to `DisplaySwitch.exe`.
- Saved configurations persist as JSON at `%APPDATA%\DisplaySystemTray\config.json`.

## Commands

```powershell
dotnet build src/DisplaySystemTray                     # build
dotnet run --project src/DisplaySystemTray             # run the tray app
dotnet publish src/DisplaySystemTray -c Release -r win-x64 --self-contained -p:PublishSingleFile=true   # ship
```

There is no test project yet; display-API behavior is verified manually on a multi-monitor machine (see PLAN.md § Verification).

## Layout

- `src/DisplaySystemTray/Program.cs` — entry point, single-instance mutex.
- `src/DisplaySystemTray/TrayApplicationContext.cs` — tray icon + context menu; menu is rebuilt from the config store on change.
- `src/DisplaySystemTray/Display/` — P/Invoke declarations (`DisplayApi.cs`), quick topology switching (`DisplayTopology.cs`), full-config snapshot/restore (`DisplayConfigSnapshot.cs`).
- `src/DisplaySystemTray/Config/` — JSON model + atomic-write store.
- `src/DisplaySystemTray/UI/SettingsForm.cs` — manage saved configurations (add/rename/update-from-current/delete).

## Conventions

- Left-click and right-click on the tray icon open the same menu.
- Failures applying a display config must surface as a tray notification, never an unhandled exception (the app has no console).
- P/Invoke structs mirror the Windows SDK layouts exactly; keep them all in `DisplayApi.cs`.
- Bump `schemaVersion` in the JSON model when changing the saved-config shape, and keep a migration path.
