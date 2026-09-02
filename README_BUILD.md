# Build notes — Garlic SaveMgr v6.8.1

The canonical build is the PowerShell script `build.ps1`.

Requirements:

- Windows x64
- .NET 8 SDK
- PowerShell

The project is WPF and targets `net8.0-windows` / `win-x64`.

## Local build

From the repository root:

```powershell
.\build.ps1
```

The script restores, builds and publishes a self-contained single-file executable to `publish/Garlic_SaveMgr.exe`.

For a compact launcher, `build-one-line.ps1` delegates to the canonical script.

## CI

GitHub Actions uses `.github/workflows/build-windows.yml` to build the project on `windows-latest` and publish `publish/Garlic_SaveMgr.exe` as a workflow artifact.
