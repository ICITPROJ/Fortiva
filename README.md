# Fortiva — Windows-Native Password Manager

> Zero-knowledge · Local-first · Living-off-the-land · No background services

Fortiva is a Windows-native password manager built entirely on the Windows stack.
All cryptographic operations use Windows CNG. There is no cloud sync, no telemetry,
and no Electron runtime.

## Components

| Component | Description |
|-----------|-------------|
| `Fortiva.Core` | Shared library: vault format, crypto, policy, audit, security scan |
| `Fortiva.Personal` | Free, local-first WinUI 3 app for individuals |
| `Fortiva.Enterprise` | Licensed, policy-driven WinUI 3 app for businesses |
| `Fortiva.Admin` | IT/admin WinUI 3 console for licenses, policies, shared vaults |
| `Fortiva.LicenseTool` | CLI tool to generate and sign enterprise licenses |
| `Fortiva.BrowserBridge.Host` | .NET native-messaging host for browser extension |
| `extension/` | Edge/Chromium browser extension (local-only, no cloud) |

## Security Architecture

```
Master Password
    │
    ▼ Argon2id (CNG: ≥64 MB, ≥3 iter, ≥1 thread)
Master Key (MK)  ──AES-256-GCM──►  Wrapped Vault Key (VK)
                                          │
                                          ▼ AES-256-GCM
                                    Vault Payload (entries + integrity log)
                                          │
                                          ▼ DPAPI (LocalMachine/CurrentUser)
                                    Rollback state · Policy · License
```

- **Windows Hello**: `UserConsentVerifier` gates access to DPAPI-protected key blob.
  The master password remains the cryptographic root.
- **Paranoia Mode**: Vault opens read-only if revision counter or DPAPI state indicates rollback.
- **Snapshot rotation**: Last N vault snapshots retained for recovery.
- **Security audit**: Full in-app scan (passwords, settings, vault hygiene) with JSON/HTML export.
- **SecureZeroMemory**: All sensitive buffers explicitly zeroed via `CryptographicOperations.ZeroMemory`.

## Build

### Prerequisites (developers)

- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- Windows 10 19041+ or Windows 11
- Visual Studio 2022 with **Windows App SDK** workload (WinUI builds only)
- Inno Setup 6 (for EXE installers)

### Core library + tests (CLI, no VS required)

```powershell
dotnet build src/Fortiva.Core/Fortiva.Core.csproj -c Release
dotnet test  tests/Fortiva.Core.Tests/                        # 119+ tests
```

### Release build + installers

```powershell
./build-release.ps1
./build-installers.ps1 -Version 1.0.0
```

`build-installers.ps1` downloads **WebView2** and **VC++ redistributable** bootstrappers and embeds them in each setup EXE. Clients receive silent prerequisite installation on first run.

### License tool (CLI)

```powershell
dotnet build src/Fortiva.LicenseTool/ -c Release

dotnet run --project src/Fortiva.LicenseTool -- generate-key
dotnet run --project src/Fortiva.LicenseTool -- sign "Acme Corp" 365 private-key.xml
dotnet run --project src/Fortiva.LicenseTool -- verify fortiva-license-acme-corp.json
```

### WinUI applications (requires Visual Studio)

```powershell
./build-release.ps1   # preferred — MSBuild + resources.pri
```

## Distribution

| Channel | App |
|---------|-----|
| GitHub Releases | `FortivaPersonal-{version}-Setup.exe` (auto-update manifest) |
| EXE installer | Inno Setup — `packaging/installer/` |
| Intune / Endpoint Manager | `.intunewin` wrap (see `packaging/intune/`) |
| SCCM / GPO | Silent install via EXE `/VERYSILENT` |

Installers bundle .NET 8 + Windows App SDK and install **WebView2** + **VC++ x64** when missing.

## Documentation

| Document | Description |
|----------|-------------|
| [`docs/UserManual.md`](docs/UserManual.md) | End-user guide (install, vault, security audit, backup) |
| [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) | Threat model, trust boundaries, mitigations |
| [`docs/VAULT-FORMAT.md`](docs/VAULT-FORMAT.md) | `.fva` binary format specification |
| [`docs/POLICY-LICENSING.md`](docs/POLICY-LICENSING.md) | License structure, policy engine |
| [`docs/ONBOARDING-RECOVERY.md`](docs/ONBOARDING-RECOVERY.md) | Onboarding, panic lock, snapshot recovery |
| [`docs/UPDATE-STRATEGY.md`](docs/UPDATE-STRATEGY.md) | Personal auto-update via GitHub Releases |
| [`docs/RELEASE-PIPELINE.md`](docs/RELEASE-PIPELINE.md) | CI/CD release workflow |
| [`docs/SECURITY-PENTEST-REPORT.md`](docs/SECURITY-PENTEST-REPORT.md) | Adversarial review findings |

## QA

```powershell
dotnet test tests/Fortiva.Core.Tests/
powershell -ExecutionPolicy Bypass -File scripts/qa-stress-audit.ps1 -SkipBuild
```

## CI/CD

GitHub Actions (`.github/workflows/ci.yml`, `release.yml`):

1. **Core** — build + test
2. **Release** — `build-release.ps1`, prerequisites fetch, Inno Setup installers, GitHub Release assets
3. **CodeQL** — security scanning
