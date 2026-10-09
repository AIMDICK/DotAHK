# DotAHK

<img src="src/DotAHK/Assets/AppIcon.png" alt="DotAHK application icon" width="96" />

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D6?logo=windows&logoColor=white)](#requirements)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A modern, minimalist AutoHotkey script manager for Windows, built with WinUI 3 (Windows App SDK) and C#.

AutoHotkey users tend to accumulate a scattered collection of `.ahk` scripts across the Documents folder, the Desktop, and project directories. Launching each one manually, remembering which interpreter it needs, and hunting down a stray script that keeps running in the background all add friction. DotAHK is a desktop hub that scans your chosen folders, presents every script in one list, and gives you precise control over launching, stopping, and grouping them. It is intended for power users and developers who already use AutoHotkey v1 and/or v2 and want a single, lightweight control panel instead of a pile of loose files.

## Core Features

### Process Isolation (PID Tracking)

Each script is launched through its resolved AutoHotkey interpreter, and the exact process ID is recorded. Stopping a script kills only that specific PID rather than every AutoHotkey process on the machine, so unrelated scripts keep running. DOTAHK also runs a short "burst" run mode (a fixed 10-second window) and samples CPU usage of tracked sessions to flag a script that appears to be stuck in a runaway loop. On application exit, every process DOTAHK started is terminated so no orphaned scripts are left behind.

**Benefit:** start and stop scripts independently without accidentally killing the scripts you want to keep running.

### Tray Hijacking (#NoTrayIcon Injection)

AutoHotkey scripts frequently add their own notification-area icon, so a handful of running scripts quickly clutter the tray. When tray hijacking is enabled (it is on by default), DOTAHK does not run your script file directly. Instead, it writes a temporary execution copy under `%LOCALAPPDATA%\DotAHK\Temp` with the `#NoTrayIcon` directive prepended, then launches that copy. The original file is never modified. The copy preserves the source file's byte-order mark and body bytes exactly so it stays valid for both v1 and v2 scripts, and DOTAHK skips the modification entirely if the script already contains `#NoTrayIcon`. The temporary copy is deleted once the run ends, and if the copy cannot be created DOTAHK falls back to launching the original script. This is script-content preprocessing and wrapping, not arbitrary code injection.

**Benefit:** a single, predictable master tray icon from DOTAHK instead of one icon per script.

### Environment Profiles (Many-to-Many Tagging)

Environment profiles such as "Gaming" or "Dev" group related scripts together. Membership is stored as a list of script keys on each profile, and a given script may belong to several profiles at once (many-to-many). Adding or removing a script from one profile never disturbs its membership in the others, and every change is saved immediately. Activating a profile launches the scripts it owns and stops tracked scripts that do not belong to it.

**Benefit:** switch your whole working context — for example from "Dev" to "Gaming" — without starting each script by hand.

### Smart Version Detection (AutoHotkey v1 / v2)

DOTAHK discovers installed AutoHotkey interpreters and determines whether each is v1 or v2 by reading the file version information, falling back to the interpreter's file name. When a script is scanned, DOTAHK inspects its `#Requires` directive to detect the version the script targets, then resolves the matching installation; when the script does not declare a version, the default installation is used (v2 is preferred, then v1). Interpreters are discovered by reading the AutoHotkey registry keys (64-bit, 32-bit, and per-user views) and by probing the well-known `Program Files` install locations.

**Benefit:** scripts written for the wrong AutoHotkey version are launched with the correct interpreter automatically, rather than failing or behaving unexpectedly.

### Multilingual Support

The user interface is localized through XML resource catalogs stored under `src/DotAHK/Strings/<language>/Resources.resw` and loaded at runtime. The following languages are included:

- English (`en-US`) — default and neutral language
- Spanish (`es-ES`)
- Russian (`ru-RU`)
- German (`de-DE`)

On first launch DOTAHK detects the operating-system display language and maps it to one of the supported languages, falling back to English when there is no match. The choice is persisted and can be overridden manually from the app.

**Benefit:** the app is usable in the user's own language out of the box, with an explicit selector for changing it.

## Screenshots

![DotAHK main window](DotAHKss.png)

## Requirements

### Build requirements

- **Windows 10 version 1809 (build 17763) or later** (Windows 11 supported). The project's minimum target version is `10.0.17763.0`.
- **.NET 8 SDK.**
- **Windows App SDK / WinUI 3 build tooling.** The project targets `net8.0-windows10.0.26100.0` and references `Microsoft.WindowsAppSDK`, so the Windows 10 SDK (build 26100 or a compatible one) and the Windows App SDK build tooling must be available.
- **Visual Studio 2022** with the *Windows application development* / WinUI workload is the most straightforward way to obtain the above; the .NET CLI plus the Windows App SDK build tooling is also supported.
- Target architectures: **x86, x64, and ARM64** are configured. The solution's default configuration maps to **x86**; select an explicit platform for other architectures.

### Runtime requirements

- **Windows App Runtime** (the Windows App SDK runtime) installed on the machine. DOTAHK is deployed as an **unpackaged, framework-dependent** application, so it uses the runtime installed on the system instead of bundling one. See the [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) page.
- **.NET 8 Desktop Runtime** (matching architecture) for a plain framework-dependent deployment.
- **AutoHotkey** installed separately. DOTAHK does **not** bundle AutoHotkey; it discovers interpreters already installed on the system. Scripts cannot be launched until an AutoHotkey v1 and/or v2 installation is present.

## Installation & Build

```cmd
git clone https://github.com/AIMDICK/DotAHK.git
cd DotAHK
```

Restore the NuGet dependencies for the solution:

```cmd
dotnet restore DotAHK.sln
```

Build a Debug configuration. The solution's default platform is x86; pass `-p:Platform=x64` (or `ARM64`) to build for another architecture:

```cmd
dotnet build DotAHK.sln -c Debug
```

Run the application from the development environment. DOTAHK is configured as an unpackaged Win32 desktop app, so it can be launched directly. Using the project (not the solution) selects the architecture of the current process:

```cmd
dotnet run --project src/DotAHK/DotAHK.csproj -c Debug
```

Alternatively, launch the produced executable directly from the build output folder:

```cmd
src\DotAHK\bin\x64\Debug\net8.0-windows10.0.26100.0\win-x64\DotAHK.exe
```

Build a Release configuration:

```cmd
dotnet build DotAHK.sln -c Release -p:Platform=x64
```

Produce a distributable, portable folder. This uses the framework-dependent publish settings in the project and the `Properties/PublishProfiles/win-x64.pubxml` profile; output lands under `bin\Release\<tfm>\win-x64\publish\`:

```cmd
dotnet publish src/DotAHK/DotAHK.csproj -c Release -p:Platform=x64
```

Published output notes:

- The deployment is **framework-dependent** and **unpackaged**, so the target machine must have the **Windows App Runtime** installed. The published folder is not a self-contained bundle.
- MSIX packaging is disabled in the project (`EnableMsixTooling=false`), so the app is shipped as a portable folder rather than an `.msix` package.
- Trimming and single-file publishing are disabled because WinUI 3 XAML/reflection are not trimming-safe.

> **Verification note:** the `dotnet build` / `dotnet publish` invocations above are documented instructions derived from `DotAHK.sln`, [`DotAHK.csproj`](src/DotAHK/DotAHK.csproj:1), and the publish profiles. Running `dotnet build DotAHK.sln -c Debug` against this repository succeeded with 0 warnings and 0 errors, producing `bin\x86\Debug\net8.0-windows10.0.26100.0\win-x86\DotAHK.dll` (the solution's default x86 platform). The `dotnet run`, Release, and `dotnet publish` commands were not executed while preparing this documentation.

## Project structure

```
DotAHK.sln                     Solution (single project: DotAHK)
src/
  DotAHK/
    App.xaml(.cs)              Application entry point, single-instance and activation handling
    MainWindow.xaml(.cs)       Main window, tray integration, minimize-to-tray
    MainPage.xaml(.cs)         Primary script-management UI
    OnboardingPage.xaml(.cs)   First-run experience
    LoadingPage.xaml(.cs)      Startup/loading screen
    Models/                    Data models (scripts, installations, profiles, settings, hotkeys)
    Services/                  Feature implementations (scanning, process tracking, tray, etc.)
    ViewModels/                MVVM view models and localized string bindings
    Converters/                XAML value converters
    Strings/<lang>/Resources.resw  Localization catalogs (en-US, es-ES, ru-RU, de-DE)
    Assets/                    Application icons, logos and splash images
    Package.appxmanifest       Packaging manifest (MSIX tooling is disabled in the project file)
    app.manifest               Win32 application manifest (OS compatibility, per-monitor DPI)
    DotAHK.csproj              Project file (target framework, platforms, dependencies)
tools/
  generate-icons.ps1           Regenerates the scaled asset images from the master .ico
  generate-app-icon.ps1        Helper for generating the master application icon
```

Directories such as `bin/`, `obj/`, and local user files are build/generated output and are intentionally excluded from version control.

## Configuration and data

DOTAHK stores all of its local state under `%LOCALAPPDATA%\DotAHK`:

- **`settings.json`** — user settings: watch folders, environment profiles and their membership, per-script launch arguments and global hotkeys, auto-start list, hidden scripts, the selected language, and the tray-hijacking / burst-mode options. It is created automatically on first run and written atomically (a temporary file is renamed into place) so a crash cannot corrupt it.
- **`startup.log`** — a best-effort startup timing trace used for diagnosing slow first paint.
- **`Temp\`** — temporary execution copies created for tray hijacking (removed when each run ends).

No manual setup is required: DOTAHK runs an onboarding flow on first launch to help you pick the folders to scan and to configure AutoHotkey. These files are machine-local and user-specific, are listed in `.gitignore`, and must never be committed. There is no sample configuration file in the repository; the defaults are defined in code in [`AppSettings`](src/DotAHK/Models/AppSettings.cs:1) and [`SettingsService`](src/DotAHK/Services/SettingsService.cs:1).

## Troubleshooting

- **The app does not start / "Windows App Runtime" missing.** DOTAHK is framework-dependent and unpackaged; install the Windows App Runtime (Windows App SDK runtime) and the matching .NET 8 Desktop Runtime, then launch again.
- **No scripts are found.** Confirm the folders you added to the watch list actually contain `.ahk`, `.ahk1`, or `.ahk2` files, and that they are not hidden or system folders (these attributes are skipped during scanning).
- **Scripts cannot be launched ("no AutoHotkey installation could be resolved").** AutoHotkey is not bundled. Install AutoHotkey v1 and/or v2, then trigger a rescan so DOTAHK can detect the interpreter through the registry or the standard install directories.
- **A script runs with the wrong interpreter version.** Check the script's `#Requires` directive, which DOTAHK uses to pick the interpreter; remove or correct it to fall back to the default installation.
- **Build fails with missing Windows SDK / Windows App SDK components.** Install the Visual Studio *Windows application development* workload (or the equivalent Windows App SDK build tooling) and a compatible Windows SDK. Note that the project references some NuGet packages with floating versions (`Version="*"`), so `dotnet restore` contacts NuGet and may resolve newer versions; a restore requires network access on a clean machine.

## Contributing

Contributions are welcome. There is currently **no automated test suite and no CI pipeline** in this repository, so changes must be verified manually.

1. Fork the repository.
2. Create a focused feature branch with a descriptive name.
3. Make your change, keeping it scoped to the topic at hand.
4. Build the solution and verify your change by running the app and exercising the affected feature:
   ```cmd
   dotnet build DotAHK.sln -c Debug
   dotnet run --project src/DotAHK/DotAHK.csproj -c Debug
   ```
5. Open a pull request describing what you changed, why, and how you verified it.

Please avoid including build output, local settings, or other machine-specific files in your commits.

## License

DotAHK is distributed under the terms of the **MIT License**. See the [`LICENSE`](LICENSE) file for the full text.

Third-party dependencies (for example the Windows App SDK and CommunityToolkit.Mvvm) are governed by their own licenses and are **not** relicensed under the MIT License by this project.

## Credits & Development

Developed by [Aimdyck](https://github.com/AIMDICK/).

This project was built and iterated using an AI-assisted development workflow:

* **Editor:** Visual Studio Code
* **AI Agent:** ZooCode
* **AI Assistant:** Supermaven
* **LLM Engine:** DeepSeek API
