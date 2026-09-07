# DayZ Local Server Mod Manager V2

> A Windows desktop manager for heavily modded DayZ **local / offline** servers — mods, load order, junctions, launch batch file, map, `types` configuration, and world-progress saves, all in one app.

**DayZ Local Server Mod Manager V2** is the successor to the original [DayZ Local Server Mod Manager](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager), rebuilt from scratch with **C# / .NET 8 and WPF (MVVM)**.

Everything a modded local DayZ server needs — discovering Workshop mods, choosing which to load and in what order, wiring them into the server, switching maps, configuring per-mod `types.xml` files, and snapshotting world progress — is done through a graphical interface instead of Explorer and text editors.

> V2 is designed for local/offline DayZ servers (the `DayZServer` installation launched with a `.bat`). It is **not** a remote/headless server admin tool.

---

## Screenshots

<img width="500" alt="Screenshot 2026-08-22 175937" src="https://github.com/user-attachments/assets/9db4af27-ce20-44f4-aa23-732ba9b5ce6e" />

<img width="500" alt="Screenshot 2026-08-22 175948" src="https://github.com/user-attachments/assets/3247a0fd-8567-499d-b91b-4648432c1105" />

---

## Features

### Mod management (Mods page)

- **Discovers** installed DayZ Workshop mods from the configured workshop directory (the folder that contains the `@...` mod folders).
- Shows **Loaded** vs. **Available** lists, with unified search (double-click or the toggle button to load/unload).
- **Reorder** the load order by drag-and-drop in the Loaded list.
- `Remove Missing` unloads mods that are no longer present on disk.
- An **Apply** persists everything: it validates the environment, updates junctions, and rewrites the launch batch file.

### Server / batch-file integration

- Loaded mods are exposed to the server as **junctions** inside a single `ModList` folder in the server root, so the server directory stays tidy even with 100+ mods. Each mod list entry is server-root-relative, e.g. `-mod=ModList/@CF`.
- The launch batch file's `modList` is written **10 entries per physical line** using `%modList%` continuation lines, so every `set` line is a complete valid `cmd` command.
- Legacy junctions created directly in the server root are **left untouched**.
- Apply is safe by construction: non-destructive junction preparation and batch-file validation happen first, destructive junction cleanup only runs after the batch file and configuration are successfully committed (see [Safety properties](#safety-properties)).

### Map & Types (Map & Types page)

- **Discovers** maps from the server's `mpmissions` folder and switches the active map, updating `serverDZ.cfg` (`template`), the batch file `serverProfile`, the matching `map_profiles` folder, and the economy config.
- **Configures a mod's types**: pick any XML file(s) in a mod whose name contains `type`; they are copied into the mission's `db/ModTypes` folder under the manager's naming scheme, and `cfgeconomycore.xml` is kept in sync (regular types are ordered before `spawnabletypes`, and entries the manager does not own — e.g. base/third-party files — are preserved).
- Reconfiguring a mod that already has configured files **pre-selects** the active files and asks before overwriting or deleting anything. `Remove Selected` and `Clean Invalid` also confirm first.
- **Untracked-file detection**: any XML file physically present in `db/ModTypes` that the manager is not tracking is shown as an `(untracked)` row, so orphans can't silently linger. Untracked rows can be removed (file + `cfgeconomycore.xml` entry) or re-adopted through the normal Config XML flow.

### Progress saves

- Save the current world of the active map (`storage_<instanceId>`, where `instanceId` is read from `serverDZ.cfg`, defaulting to 1).
- Each save stores a copy of the world data plus a **snapshot of the mission's `db/ModTypes` folder** and a `meta.json` that records the map, save time, the ordered mod list, and the active types files.
- Add, load, delete, and "new game" operations are available. **Loading is staged** (a temporary copy is promoted only after it fully succeeds), so an interruption never destroys the current progress.
- Loading a save created under a **different mod/types setup** warns first with context-aware guidance: mod-list differences point to the save's `meta.json` (`ModList`) for alignment; missing types files point to the stored `ModTypes` snapshot for investigation/restore.

### Settings & data directory

- All configuration is stored as JSON in a **data directory** that follows the setup automatically:
  - `%LOCALAPPDATA%\DayZ-Mod-Manager-V2` until a server path is configured;
  - then `<serverPath>\DayZ-Mod-Manager-V2`.
- When the location changes, the data directory (including progress saves) is **relocated** to the new location.
- Optional **auto-cleanup of old server logs** (`.RPT` and script logs) on start, after Apply, and before starting the server.

---

## Quick start

1. **Install / update DayZ + DayZ Server** and run the DayZ server once so it generates `serverDZ.cfg`, `mpmissions`, and the default map folders.
2. **Prepare a launch batch file** (an example ships as `LocalServer.example.bat`). It must contain a `modList` line — the manager owns and rewrites that block from then on.
3. **Open the app.** On the Settings tab set:
   - **Workshop path** — the folder containing your installed `@...` mod folders (e.g. `<Steam>\steamapps\common\DayZ\!Workshop`);
   - **Server path** — the folder containing `DayZServer_x64.exe` and `serverDZ.cfg`;
   - **Batch file** — your launch `.bat` (a bare name resolves inside the server folder).
4. **Mods tab**: load the mods you want and arrange their order, then **Apply** (or just switch tabs — pending changes are applied automatically).
5. **Map & Types tab**: pick your map; it is applied immediately (template + profile + economy).
6. **Config XML** per mod to bring its `types`/`spawnabletypes` into the mission.
7. **Start Server** from the main window. Test in the DayZ client with `-connect=127.0.0.1` as usual for local play.

> Stop the DayZ server before operations that change the world or the active mission (save/load/new game, map switch, types configuration). Save/load/new-game are refused automatically while the server process is running.

---

## Data layout

```text
<serverPath>\
├─ DayZ-Mod-Manager-V2\                 # data directory (once a server path is set)
│  ├─ settings.json                     # workshop/server/batch paths, toggles
│  ├─ mod_order.json                    # ordered loaded-mod list
│  ├─ types_config.json                 # per-map types configuration (mods -> db/ModTypes files)
│  └─ Progress_Saves\
│     └─ <mapName>\
│        └─ <saveName>\
│           ├─ storage_<id>\            # copy of the world data
│           ├─ meta.json                # map, saved-at, mod list, active types files
│           └─ ModTypes\                # snapshot of the mission's db/ModTypes
├─ ModList\                             # junction per loaded mod (@mod -> workshop/@mod)
├─ mpmissions\
│  └─ <mapName>\db\ModTypes\            # generated types files the server loads
└─ map_profiles\<mapName>\              # profile/log folder (mirrors the mission)
```

`%LOCALAPPDATA%\DayZ-Mod-Manager-V2` holds a pointer (`data_directory.txt`) to the active data directory before/after it is relocated.

---

## Safety properties

These invariants are enforced by the design and covered by unit tests:

- **Apply** validates first, prepares junctions non-destructively, writes the batch file, and only then commits configuration and performs destructive junction cleanup. A failed or aborted batch write never deletes junctions that the (unchanged) launch batch still references, and an undeletable leftover junction degrades to a warning instead of blocking future Applies.
- **Save / Load / New game** are refused while the DayZ server is running. Loading is staged and rolled back safely if anything fails; `.restore` staging folders are swept on the next load.
- **Types configuration** asks before overwriting or deleting anything, keeps `cfgeconomycore.xml` and `db/ModTypes` consistent, and never touches entries it doesn't own.
- **Map/template writes** only ever change the manager-owned lines (`template`, `serverProfile`, `modList`); the rest of `serverDZ.cfg` and the batch file are left alone.

---

## Architecture

```
src/
  DayZModManager.Core/      business logic (no UI)
    Services/               Apply, Junction, Batch, Settings, Types, EconomyCore,
                            Map, SaveGame, ModOrder, Discovery, Validation, ...
    Models/                 Settings, TypesConfig, SaveMetaData, ModState, ...
    Abstractions/ + IO/     IFileSystem / PhysicalFileSystem, IJunctionOperations
  DayZModManager.App/       WPF shell
    ViewModels/             MVVM view models + commands
    MainWindow.xaml(.cs)    views & tab orchestration
    Behaviors/              drag-drop reorder, selection sync, auto-scroll
    Dialogs/                confirm/warning, text prompt, types-file picker
    Services/               dialog service, process launcher
tests/
  DayZModManager.Core.Tests/   xUnit unit tests (services, models)
  DayZModManager.App.Tests/    xUnit unit tests (view models)
```

Key ideas:

- **MVVM everywhere.** View models expose commands and observable collections; the window code-behind stays thin.
- **`IFileSystem` abstraction.** Every service that touches disk goes through it, so tests use an in-memory `FakeFileSystem` (and fault-injecting wrappers) instead of real I/O. `JunctionOperations`/`IJunctionOperations` similarly abstracts Win32 junction creation for testability.
- **Single source of truth for mod paths.** `ModListFolder` produces the same server-root-relative entry used for both the junction layout and the batch `modList`.
- **Dependency injection** wires the composition root in `App.xaml.cs`.

### Data flow

```text
Mods page (Loaded/Available/order)
   │  Apply
   ▼
Validate → prepare ModList junctions → rewrite batch modList → save settings.json/mod_order.json → finalize junctions → verify
   │
   ├─ Map & Types: discover maps → switch map (serverDZ.cfg template + serverProfile) → config types XML into db/ModTypes + cfgeconomycore.xml
   └─ Progress saves: copy storage_<id> + db/ModTypes + meta.json → Progress_Saves\<map>\<save>
```

---

## Development

### Requirements

- Windows (the app targets `net8.0-windows` and uses WPF).
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

### Build & test

```bash
dotnet build DayZModManagerV2.sln
dotnet test  DayZModManagerV2.sln
```

The solution builds three projects plus two test projects:

| Project | Kind | Purpose |
| --- | --- | --- |
| `DayZModManager.Core` | class library | platform-agnostic business logic |
| `DayZModManager.App` | WPF (`net8.0-windows`) | UI shell + view models |
| `DayZModManager.Core.Tests` | xUnit | service/model unit tests |
| `DayZModManager.App.Tests` | xUnit (`net8.0-windows`) | view-model unit tests |

### Conventions

- No UI logic in `Core`; `Core` talks to disk only through `IFileSystem` / `IJunctionOperations`.
- Filesystem and configuration writes are UTF-8 without BOM.
- Unit tests prefer in-memory fakes and assert on observable state (config files written, junctions present, economy XML content) rather than on mock call sequences.
- Add a regression test alongside any bug fix (the suite currently runs **280 tests**).

---

## License

See the project repository for licensing details.
