# EXE installer (Inno Setup)

Production builds use **Inno Setup** with self-contained publish output plus embedded prerequisites.

## Bundled in the app

- .NET 8 runtime (`SelfContained=true`)
- Windows App SDK (`WindowsAppSDKSelfContained=true`)

## Installed automatically when missing

- Microsoft Edge WebView2 Runtime
- Visual C++ 2015–2022 Redistributable (x64)

Fetch before compile:

```powershell
./scripts/fetch-installer-prerequisites.ps1
./build-installers.ps1
```

`build-installers.ps1` runs the fetch script automatically.

## Deployment

- **Personal** — per-user install (`PrivilegesRequired=lowest`); prerequisites install silently without admin when possible.
- **Enterprise / Admin** — machine-wide install under Program Files; IT can use `/VERYSILENT`.

See `packaging/intune/README.md` for Intune Win32 deployment.
