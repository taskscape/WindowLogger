# WindowLogger Installer

The installer publishes all four applications as self-contained `win-x64` executables and packages them with Inno Setup.

## Prerequisites

- .NET 10 SDK
- PowerShell 5.1 or later
- Inno Setup 6

## Local build

```powershell
.\Installer\Build-Installer.ps1 -Version 1.2.3
```

If Inno Setup is not installed in its default location:

```powershell
.\Installer\Build-Installer.ps1 `
  -Version 1.2.3 `
  -InnoSetupCompiler C:\Path\To\ISCC.exe
```

The resulting installer is written to `Installer\Output\WindowLogger-Setup-1.2.3.exe`.

## Installer behavior

- Installs into `%LOCALAPPDATA%\Programs\WindowLogger` by default.
- Deploys the logger, analyser, unified WPF control center, optional WPF tray controller, default configuration, and all self-contained runtime dependencies.
- Offers an unchecked **Start WindowLogger when I sign in to Windows** task.
- Adds or removes the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\WindowLogger` value according to that selection, including during upgrades.
- Offers to launch `WindowLoggerConfigGui.exe` (the WindowLogger Control Center) when setup finishes.
- Requires no administrator privileges or separately installed .NET runtime.

## GitHub releases

Push a `vMAJOR.MINOR.PATCH` tag whose commit is reachable from `main` or `master`:

```powershell
git tag v1.2.3
git push origin v1.2.3
```

The release workflow validates the tag and branch ancestry, builds the installer, and publishes it to the matching GitHub Release.
