# DayZ Local Server Mod Manager V2

> **A Windows mod manager for DayZ local servers, offline play, and single-player PvE.**

**DayZ Local Server Mod Manager V2** is a Windows desktop application designed to make heavily modded DayZ local servers easier to manage.

It provides a graphical interface for managing your installed DayZ mods, load order, server configuration, map and `types.xml` settings, batch files, and mod directory structure — without having to repeatedly edit configuration files by hand.

V2 is the successor to the original [DayZ Local Server Mod Manager](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager), completely rebuilt with **C# / .NET 8 and WPF**.

---

## ⬇️ Download

### [Download DayZ Local Server Mod Manager V2](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2/releases/latest)

**Latest stable release: v2.0.0**

* Windows x64
* Self-contained
* No separate .NET runtime installation required
* Single executable

> **V2 is designed for local/offline DayZ servers.**

---

<img width="500" alt="Screenshot 2026-08-22 175937" src="https://github.com/user-attachments/assets/9db4af27-ce20-44f4-aa23-732ba9b5ce6e" />

<img width="500" alt="Screenshot 2026-08-22 175948" src="https://github.com/user-attachments/assets/3247a0fd-8567-499d-b91b-4648432c1105" />

---

## What Is This?

Managing a heavily modded DayZ local server can become surprisingly tedious.

You may need to:

* Keep track of dozens of installed mods.
* Decide which mods are currently active.
* Maintain the correct mod load order.
* Edit server configuration files.
* Configure your map and `types.xml`.
* Manage mod directories.
* Launch the server with the correct parameters.
* Repeat the process whenever you change your mod setup.

V2 brings these tasks together into one Windows application.

Instead of manually managing everything through Explorer and text editors, you can manage your local DayZ environment through a single graphical interface.

---

## Features

### Mod management

- Discover and list installed DayZ Workshop mods
- Enable/disable mods and reorder their load order
- Apply the mod configuration to the server: creates the required mod junctions and rewrites the launch batch file

### Junction & batch file handling

- Loaded mods are hosted as junctions in a dedicated `ModList` folder inside the server root, keeping the server directory tidy even with 100+ mods
- The batch file's `modList` is written 10 entries per line as server-root-relative paths (e.g. `ModList/@CF`), keeping every line a complete valid `cmd` command
- Existing mods and junctions are reconciled on every Apply; legacy root junctions are left untouched

### Map & Types page

- Discover maps and switch the active map (updates `serverDZ.cfg`, the batch `serverProfile`, `map_profiles`, and economy config)
- Configure a mod's types by copying its XML files into the mission's `db/ModTypes` folder, with `cfgeconomycore.xml` kept in sync
- Safe reconfiguration: each mod may have at most one active `types` and one active `spawnabletypes` file, previously configured files are pre-selected, and any change that overwrites or deletes existing type files asks for confirmation first
- `Remove Selected` and `Clean Invalid` also confirm before deleting files

### Progress saves

- Save the current world progress of the active map (the `storage_<instanceId>` folder, where `instanceId` comes from `serverDZ.cfg`, defaulting to 1)
- Add, load, and delete named saves, and start a fresh game — all stored under the manager's data directory (`Saves\<map>\<saveName>`)
- Loading a save is staged so an interruption never destroys the current progress; folder operations run off the UI thread
- Saves follow the data directory when it is relocated

### Other

- Data/config files (`settings.json`, `mod_order.json`, `types_config.json`, saves) live in a data directory that can be relocated from the Settings tab

---

## Server Configuration

* Manage server configuration files.
* Manage batch files used to launch the local server.
* Configure map-related settings.
* Configure `types.xml`.
* Keep your local server configuration organized alongside your mod setup.

### Local Server Workflow

V2 is designed around a simple workflow:

```text
DayZ Mods
   ↓
DayZ Local Server Mod Manager V2
   ↓
Mod / Load Order / Map / Types / Server Configuration
   ↓
Local DayZ Server
   ↓
DayZ