# DayZ Local Server Mod Manager

A Windows desktop application for managing mods on a DayZ local/dedicated server. Built with WPF on .NET 8.

## Features

- Discover and list installed DayZ mods
- Enable/disable mods and reorder their load order
- Apply mod configuration to the server
- Manage server config, batch files, and types/map configuration
- Junction support for mod directory management

## Project Structure

- `src/DayZModManager.Core` — domain logic and services (no UI dependencies)
- `src/DayZModManager.App` — WPF application (UI, view models, behaviors)
- `tests/DayZModManager.Core.Tests` — unit tests for the core library
- `tests/DayZModManager.App.Tests` — unit tests for the app layer

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows (the app targets `net8.0-windows` and uses WPF)

## Build

```powershell
dotnet build DayZModManagerV2.sln
```

## Run

```powershell
dotnet run --project src/DayZModManager.App
```

## Test

```powershell
dotnet test
```

## License

[MIT](LICENSE)
