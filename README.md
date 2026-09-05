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

<img width="500" alt="Screenshot 2026-09-05 173150" src="https://github.com/user-attachments/assets/86210604-3814-4fe3-b2f7-04f40611698d" />


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

# ⚠️ Important: Local / Offline Servers Only

V2 is intentionally focused on **offline and local-server use**.

It is **not** designed to be a complete public DayZ server administration platform, and it does **not** provide Bikey management.

### Bikey / Signature Management

V2 does **not** manage:

* `.bikey` files
* `.bisign` files
* server-side signature verification
* automated Bikey deployment

Therefore, the intended V2 configuration requires:

```cpp
verifySignatures = 0;
```

in your DayZ server configuration.

### Example

```cpp
hostname = "My Local DayZ Server";
verifySignatures = 0;
```

Without this setting, mods that rely on signature verification may not work correctly with the V2 workflow.

> **Do not treat V2 as a public-server security or signature-management solution.**
>
> For public or shared dedicated servers that require proper signature verification and Bikey management, use a dedicated server-management solution designed for that purpose.

---

# Who Is V2 For?

V2 is primarily intended for players who:

* Play DayZ offline or through a local server.
* Prefer single-player PvE.
* Run large mod collections.
* Frequently change their mod setup.
* Want a graphical alternative to manually editing server files.
* Enjoy experimenting with different maps, mods, and server configurations.

It is especially useful for players who have built their own heavily modded DayZ survival environment and want a cleaner way to maintain it.

---

# Getting Started

## 1. Download V2

Download the latest release from:

**[GitHub Releases](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2/releases/latest)**

Download the Windows x64 executable.

## 2. Prepare Your DayZ Local Server

Make sure your DayZ local/dedicated server is already installed and working.

V2 is a management tool; it does not replace the DayZ server itself.

## 3. Disable Signature Verification

Open your server configuration and make sure:

```cpp
verifySignatures = 0;
```

is configured.

## 4. Open V2

Launch:

```text
DayZModManagerV2.exe
```

and configure your local server environment.

## 5. Configure Your Mods

Use the application to select, organize, and configure the mods you want to use.

## 6. Start Your Server

Launch the local DayZ server using your configured server or batch-file setup.

---

# Requirements

### For Users

* Windows
* 64-bit Windows system
* DayZ Standalone
* A working DayZ local/dedicated server installation

The released application is published as a **self-contained Windows x64 application**, so users do not need to install the .NET runtime separately.

### For Developers

To build V2 from source:

* Windows
* .NET 8 SDK
* Visual Studio 2022 or another compatible .NET development environment

---

# V1 → V2

V2 is more than a visual redesign of the original project.

The original V1 was built around a PowerShell-based implementation. V2 was redesigned as a proper Windows application using **C# / .NET 8 and WPF**.

### Major improvements

| V1                                     | V2                                  |
| -------------------------------------- | ----------------------------------- |
| PowerShell                             | C# / .NET 8                         |
| PowerShell GUI                         | WPF                                 |
| Script-oriented architecture           | Structured application architecture |
| More fragmented configuration workflow | More integrated management workflow |
| Complex state/checking logic           | Cleaner state-driven design         |
| Limited testability                    | Dedicated test projects             |
| Monolithic script                      | Separated Core and App layers       |

The V2 solution currently separates:

```text
src/
├── DayZModManager.Core
└── DayZModManager.App

tests/
├── DayZModManager.Core.Tests
└── DayZModManager.App.Tests
```

The Core layer contains the domain logic and services, while the App layer contains the WPF user interface and application-specific components.

---

# Build From Source

Clone the repository:

```bash
git clone https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2.git
cd DayZ-Local-Server-Mod-Manager-V2
```

Build:

```bash
dotnet build DayZModManagerV2.sln
```

Run:

```bash
dotnet run --project src/DayZModManager.App
```

Run tests:

```bash
dotnet test
```

### Publish a Windows x64 build

```powershell
dotnet publish src/DayZModManager.App `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:DebugType=None `
  -p:DebugSymbols=false
```

---

# Releases

Stable builds are distributed through GitHub Releases.

### Current Release

**[v2.0.0 — First Stable Release](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2/releases/tag/v2.0.0)**

Released **August 22, 2026**.

---

# Project Direction

The purpose of V2 is not to compete with large multiplayer server-management platforms.

Instead, it focuses on a specific use case:

> **Making heavily modded DayZ local and single-player PvE environments easier to build and maintain.**

The project will continue to prioritize usability, reliability, and the needs of local-server players.

---

# Contributing

Bug reports, suggestions, and improvements are welcome.

When reporting a problem, please include:

* What you were trying to do.
* What you expected to happen.
* What actually happened.
* Relevant error messages or screenshots.
* Your DayZ/server configuration when appropriate.

---

# License

This project is licensed under the [MIT License](LICENSE).

---

# Disclaimer

This project is an independent community tool.

**DayZ** is a trademark of **Bohemia Interactive**.

This project is not affiliated with or endorsed by Bohemia Interactive.

---

## Links

* **[Download Latest Release](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2/releases/latest)**
* **[View Releases](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2/releases)**
* **[V2 Repository](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2)**
* **[V1 Repository](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager)**
