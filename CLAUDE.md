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

## Development workflow (mandatory for all agents)

### Commit discipline

- **Commit after every functional step.** Size commits the way a careful human developer would: one small, working change or addition per commit. Do not batch a milestone's worth of work into one commit, and do not commit broken intermediate states.
- **Write meaningful, descriptive commit messages.** Followers of this project and other devs read the history to see how it actually evolved — each message must let them understand what changed and why without opening the diff. Subject line states the change; body explains motivation or non-obvious decisions when they exist.
- Push after committing so CI and the review pipeline run.

### Issue-driven fixes

When you notice something already in the repo needs changing in order for the project to work as it evolves (a bug, a wrong assumption, a design flaw — as opposed to planned new work):

1. **Do not just fold the fix into another commit.** First open a GitHub issue documenting the needed change: what is wrong, why it blocks progress, and the intended fix (`gh issue create`).
2. Commit the fix by itself with a message body containing `Fixes #<n>` (plus a reference to the issue in the explanation), then push — the push closes the issue and GitHub cross-references the commit and issue automatically.

### Post-commit review pipeline

- **After every push**, run the `post-commit-review` skill (`.claude/skills/post-commit-review/SKILL.md`). It launches four project agents in parallel: `ci-watchdog` (CI failures *and* warnings), `security-analyst`, `stability-reviewer`, and `best-practices-reviewer` (definitions in `.claude/agents/`).
- Everything those agents discover becomes a GitHub issue labeled `automated-review` plus a category label (`ci`, `security`, `stability`, `best-practices`).
- **The next coding agent/step must address open `automated-review` issues first** — fix each in its own commit with `Fixes #<n>`, or close it with a comment explaining why it won't be fixed. Never silently ignore one.
- A PostToolUse hook (`.claude/hooks/post-commit-reminder.ps1`) injects a reminder whenever a `git commit` command runs.

## Conventions

- Left-click and right-click on the tray icon open the same menu.
- Failures applying a display config must surface as a tray notification, never an unhandled exception (the app has no console).
- P/Invoke structs mirror the Windows SDK layouts exactly; keep them all in `DisplayApi.cs`.
- Bump `schemaVersion` in the JSON model when changing the saved-config shape, and keep a migration path.
