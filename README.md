# DayZ Local Server Mod Manager V2

A Windows desktop application for managing **DayZ local servers and offline single-player environments**.

DayZ Local Server Mod Manager V2 is the successor to [DayZ Local Server Mod Manager V1](https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager), redesigned from the ground up with **C# / .NET 8 and WPF**.

The project is intended primarily for players who run DayZ through a **local server**, especially those who want a heavily modded PvE or single-player experience without relying on a full-featured public server management stack.

> **V2 is designed for offline/local-server use. It does not manage Bikeys or server-side signature verification.**

---
<img width="600" alt="Screenshot 2026-08-22 175937" src="https://github.com/user-attachments/assets/9db4af27-ce20-44f4-aa23-732ba9b5ce6e" />

<img width="600" alt="Screenshot 2026-08-22 175948" src="https://github.com/user-attachments/assets/3247a0fd-8567-499d-b91b-4648432c1105" />

<img width="600" alt="Screenshot 2026-08-22 175951" src="https://github.com/user-attachments/assets/edc51624-2b66-4577-b100-aa50346c78c3" />


---


## Features

### Mod Management

* Detect and list installed DayZ mods.
* Enable or disable mods.
* Reorder the active mod load order.
* Generate and apply the server's mod configuration.
* Manage mod directories through Windows junctions.
* Keep the available-mod and loaded-mod workflows organized separately.

### Server Configuration

V2 brings several configuration tasks into the same management workflow:

* Server configuration management.
* Batch file management.
* Map configuration.
* `types.xml` configuration.
* Mod load-order configuration.
* Server launch configuration.

### Local Server Workflow

The project is specifically designed around the workflow of running DayZ locally:

```text
Steam Workshop Mods
        ↓
DayZ Local Server
        ↓
DayZ Local Server Mod Manager V2
        ↓
Mod / Map / Types / Server Configuration
        ↓
Launch DayZ
```

The goal is to make configuring a heavily modded local DayZ installation easier without requiring a large external server-management system.

---

## Important: `verifySignatures`

**This application does not manage Bikeys and does not provide Bikey/signature management.**

Because V2 is intended for offline and local-server usage, your DayZ server must run with signature verification disabled.

Open your DayZ server configuration and make sure you have:

```text
verifySignatures = 0;
```

For example:

```cpp
hostname = "My Local DayZ Server";
verifySignatures = 0;
```

### Why is this required?

DayZ normally uses signature verification to ensure that clients are using properly signed server-approved mods.

V2 does **not** manage `.bikey` files, `.bisign` files, or server signature verification. Therefore, the intended V2 workflow is:

```text
Local / Offline Server
        +
verifySignatures = 0
        +
DayZ Local Server Mod Manager V2
```

This makes V2 particularly suitable for **personal local servers, offline experimentation, mod testing, and single-player PvE setups**.

> **Do not use this configuration for a public server where proper mod signature verification is required.**

---

## Requirements

### Runtime

* Windows
* .NET 8
* DayZ Standalone
* A working DayZ local/dedicated server installation

The application targets `net8.0-windows` and uses WPF.

### Development

To build the project from source:

* .NET 8 SDK
* Windows
* Visual Studio 2022 or another compatible .NET development environment

---

## Build

Clone the repository:

```bash
git clone https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2.git
cd DayZ-Local-Server-Mod-Manager-V2
```

Build the solution:

```bash
dotnet build DayZModManagerV2.sln
```

Run the application:

```bash
dotnet run --project src/DayZModManager.App
```

Run the test suite:

```bash
dotnet test
```

---

## Project Structure

V2 separates the application layer from the core domain and service logic.

```text
DayZ-Local-Server-Mod-Manager-V2/
│
├── src/
│   ├── DayZModManager.Core/
│   │   ├── Abstractions/
│   │   ├── IO/
│   │   ├── Models/
│   │   ├── Services/
│   │   └── ...
│   │
│   └── DayZModManager.App/
│       ├── Behaviors/
│       ├── Dialogs/
│       ├── Resources/
│       ├── Services/
│       ├── ViewModels/
│       ├── MainWindow.xaml
│       └── ...
│
├── tests/
│   ├── DayZModManager.Core.Tests/
│   └── DayZModManager.App.Tests/
│
├── DayZModManagerV2.sln
├── LICENSE
└── README.md
```

The architecture keeps the Core project independent from WPF while the App project contains the desktop UI, view models, behaviors, and application-specific services.

---

## V1 → V2

V2 is not intended to be merely a visual redesign of V1.

The original V1 was implemented as a PowerShell-based graphical manager. V2 was created as a larger architectural rewrite using C#/.NET and WPF.

### Major changes

| V1                                                          | V2                                      |
| ----------------------------------------------------------- | --------------------------------------- |
| PowerShell                                                  | C# / .NET 8                             |
| PowerShell GUI                                              | WPF                                     |
| Monolithic script-oriented design                           | Separated application/core architecture |
| Mod Manager and configuration workflows were more separated | More integrated management workflow     |
| Complex mod-checking workflow                               | Simplified state-driven management      |
| Limited testability                                         | Dedicated unit-test projects            |
| Script-based implementation                                 | Structured service/model architecture   |

The new solution contains separate `Core`, `App`, and test projects, making future maintenance and expansion considerably easier than the original V1 structure.

---

## What V2 Is — and Is Not

### V2 is intended for:

* Offline DayZ players
* Local-server players
* Single-player PvE environments
* Personal modded servers
* Mod testing and experimentation
* Players who want a convenient graphical way to manage a large local mod collection

### V2 is not intended to be:

* A public DayZ server administration platform
* A replacement for mature multiplayer server managers
* A Steam Workshop downloader
* A Bikey management system
* A server signature-management solution
* A complete public-server security framework

Its scope is deliberately narrower: **make the local DayZ modding experience easier to manage.**

---

## Typical Usage

A typical workflow looks like this:

### 1. Prepare your DayZ local server

Make sure your DayZ server installation is working correctly.

### 2. Disable signature verification

Set:

```cpp
verifySignatures = 0;
```

### 3. Install your DayZ mods

Install the required Workshop mods and make sure they are available to your local DayZ installation.

### 4. Launch V2

Open **DayZ Local Server Mod Manager V2** and configure your local server.

### 5. Configure your mods

Use the application to:

* Select the mods you want to use.
* Enable or disable mods.
* Arrange their load order.
* Apply the configuration.
* Configure map and `types.xml` settings.
* Manage your server-related files.

### 6. Start the local server

Launch the server using your configured server/batch configuration and then start DayZ.

---

## Design Philosophy

V2 was built around a simple idea:

> **A local DayZ server should be easy to manage without requiring the complexity of a full multiplayer server-management platform.**

The project focuses on the needs of players who want to build their own heavily modded DayZ environment and spend more time playing than manually editing configuration files.

---

## Known Scope Limitation

V2 intentionally does not handle DayZ signature files.

This means:

```text
.bikey
.bisign
verifySignatures
```

are outside the scope of the application.

For the intended local/offline workflow, use:

```cpp
verifySignatures = 0;
```

Users who require proper signature verification and Bikey management for a public or shared dedicated server should use a server-management solution designed for that purpose.

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## Repository

**V2:**
https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager-V2

**V1:**
https://github.com/RichardVincent1324/DayZ-Local-Server-Mod-Manager

---

## Disclaimer

This project is an independent community tool for managing local DayZ server files and configurations.

DayZ is a trademark of Bohemia Interactive.

This project is not affiliated with or endorsed by Bohemia Interactive.
