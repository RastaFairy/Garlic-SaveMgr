# Build and release guide — Garlic SaveMgr v6.8.1

## Build requirements

- Windows x64
- .NET 8 SDK
- PowerShell

## Local build

From the repository root:

```powershell
.\build.ps1
```

The script restores, builds and publishes a self-contained `win-x64` single-file executable to `publish/Garlic_SaveMgr.exe`.

## GitHub Actions

`.github/workflows/build-windows.yml` builds on `windows-latest` with the .NET 8 SDK and exposes the executable as a workflow artifact.

## Release checklist

1. Confirm the version in `GarlicSaveMgr/GarlicSaveMgr.csproj`, `GarlicSaveMgr/Infrastructure/AppInfo.cs` and `GarlicSaveMgr/app.manifest`.
2. Confirm `CHANGELOG.md` has the release entry.
3. Run `build.ps1` on Windows.
4. Exercise discovery, backup, restore and payload startup against a test console.
5. Verify the resulting `Garlic_SaveMgr.exe` is the expected `win-x64` artifact.
6. Tag the repository using the version format `v6.8.1` for the release.
