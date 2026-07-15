# DisplaySystemTray — Plan

A Windows system-tray utility for quickly switching display modes (Extend, Show only on 1, Show only on 2) and for saving/restoring named display configurations — without reimplementing the Windows Settings > System > Display UI.

## Goals (MVP)

1. **Tray icon** that lives in the taskbar / hidden-icons overflow.
   - Left-click *or* right-click opens the same context menu.
   - Menu contents:
     - Quick modes: **Extend**, **Show only on 1**, **Show only on 2**
     - Separator
     - All user-saved display configurations (click to apply)
     - Separator
     - **Settings…** (opens the settings window)
     - **Exit**
2. **Quick mode switching** — equivalent to Win+P / `DisplaySwitch.exe`, implemented natively via the CCD API (`SetDisplayConfig` with `SDC_TOPOLOGY_EXTEND` / `SDC_TOPOLOGY_INTERNAL` / `SDC_TOPOLOGY_EXTERNAL`).
3. **Saved configurations** — instead of duplicating every display setting in our UI, the user arranges displays in Windows Settings however they like, then clicks "Save current as…" in our app. Applying a saved configuration restores the full topology: which displays are active, resolution, refresh rate, positions, orientation.
   - Capture: `QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS)` → serialize path/mode arrays to JSON.
   - Restore: `SetDisplayConfig(paths, modes, SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG | SDC_ALLOW_CHANGES | SDC_SAVE_TO_DATABASE)`.
4. **Settings window** to manage configurations:
   - **Save current as…** (add)
   - **Rename**
   - **Update from current** (modify — re-capture the current display state into an existing entry)
   - **Delete**
   - Configurations appear in the tray menu immediately after changes.

## Non-goals (MVP)

- Re-implementing individual display settings (resolution pickers, HDR, scaling, etc.) — Windows Settings already does this; we snapshot/restore instead.
- Duplicate / clone mode quick-switch (easy to add later: `SDC_TOPOLOGY_CLONE`).
- Multi-user or per-monitor-DPI-aware fancy UI.
- Installer/MSI. MVP ships as a self-contained single-file exe.

## Tech stack

| Choice | Rationale |
|---|---|
| **C# / .NET 8 (LTS), WinForms** | First-class `NotifyIcon` tray support, easy P/Invoke to the Win32 CCD display API, single-file publish. .NET 8 SDK will be installed via `winget` (only .NET 5 is currently present, which is EOL). |
| **CCD API via P/Invoke** (`QueryDisplayConfig`, `SetDisplayConfig`, `DisplayConfigGetDeviceInfo`) | Native, reliable topology switching and full-state snapshot/restore. No dependency on shelling out to `DisplaySwitch.exe` (whose CLI args are undocumented and changed in Win11). |
| **JSON storage** at `%APPDATA%\DisplaySystemTray\config.json` | Human-readable, easy to back up, trivially versioned with a `schemaVersion` field. |

## Architecture

```
src/DisplaySystemTray/
  Program.cs               // entry point, single-instance mutex, ApplicationContext
  Cli.cs                   // hidden CLI verbs for scripted verification (--selftest etc.)
  TrayApplicationContext.cs// NotifyIcon, menu construction, menu event handlers
  Display/
    DisplayApi.cs          // P/Invoke declarations for CCD API (structs, enums, externs)
    DisplayTopology.cs     // quick-mode switching (Extend / Internal / External)
    DisplayConfigSnapshot.cs // capture + apply full display configurations
  Config/
    AppConfig.cs           // model: list of SavedConfiguration {id, name, capturedAt, paths, modes}
    ConfigStore.cs         // load/save JSON in %APPDATA%, atomic writes
  UI/
    SettingsForm.cs        // list view + Save current as / Rename / Update / Delete
  Resources/
    app.ico                // tray icon
```

- `TrayApplicationContext` rebuilds the menu whenever the config store changes (event-based).
- Left-click on the tray icon programmatically opens the same `ContextMenuStrip` as right-click.
- Errors applying a configuration (e.g. monitor unplugged since capture) surface as a tray balloon/toast, never a crash.

## Milestones

1. ~~**M0 — Scaffolding**~~ ✅: PLAN.md, CLAUDE.md, .gitignore, git init/commit, public GitHub repo, CI/CD, review pipeline.
2. ~~**M1 — Project + tray shell**~~ ✅: .NET 8 SDK installed, tray icon with menu, single-instance guard, Exit works.
3. ~~**M2 — Quick mode switching**~~ ✅: CCD P/Invoke layer, Extend / Show only 1 / Show only 2 working; hidden CLI added for scripted verification.
4. ~~**M3 — Snapshot & restore**~~ ✅: capture current config, JSON persistence with atomic writes, apply with adapter-LUID remapping; saved items shown in tray menu.
5. ~~**M4 — Settings window**~~ ✅: CRUD UI (add/rename/update-from-current/delete), menu refresh on change, `--settings` CLI entry.
6. ~~**M5 — Polish & ship**~~ ✅: app icon, "Start with Windows" checkbox (HKCU Run key), README with usage, `dotnet publish` self-contained exe, tag v0.1.0.

## Verification

- Scripted: run the exe with `--selftest` (validates all topologies and re-applies the current one as a visual no-op) and exercise `--save`/`--restore`/`--delete` round-trips.
- Manual: on this multi-monitor machine, exercise each quick mode, save a config, deliberately change display settings in Windows, then restore via the tray and confirm the topology returns.
- `dotnet build` clean with warnings-as-errors for the P/Invoke layer.

## Future ideas (post-MVP)

- Duplicate/clone quick mode; hotkeys per configuration; auto-apply on dock/undock events (`WM_DISPLAYCHANGE`); per-configuration icons; export/import configs; audio-device switching bundled with a display config.
