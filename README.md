# Fortiva — Windows-Native Password Manager

> Zero-knowledge · Local-first · Living-off-the-land · No background services

Fortiva is a Windows-native password manager built on the Windows stack.
Symmetric encryption uses Windows CNG (AES-256-GCM). Master-password key derivation
uses memory-hard Argon2id. There is no cloud sync, no telemetry, and no Electron runtime.

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
    ▼ Argon2id (memory-hard KDF, ≥64 MB / ≥3 iter personal default)
Master Key (MK)  ──AES-256-GCM (Windows CNG)──►  Wrapped Vault Key (VK)
                                          │
                                          ▼ AES-256-GCM
                                    Vault Payload (entries + integrity log)
                                          │
                                          ▼ DPAPI (LocalMachine/CurrentUser)
                                    Rollback state · Policy · License
```

- **Windows Hello**: Protects a `hello.keyprotect` blob next to your vault (software-backed by default; TPM/KeyCredential upgrade when available). One biometric prompt during setup. Auto-prompt on unlock when Hello is configured.
- **Paranoia Mode**: Vault opens read-only if revision counter or DPAPI state indicates rollback.
- **Snapshot rotation**: Last N vault snapshots retained for recovery.
- **Security audit**: Full in-app scan (passwords, settings, vault hygiene) with actionable deep links and JSON/HTML export.
- **Browser bridge**: Loopback HTTP (`127.0.0.1:7847`) with native-messaging fallback; one-shot host per Fill (see [`docs/BRIDGE-ARCHITECTURE.md`](docs/BRIDGE-ARCHITECTURE.md)).
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
dotnet test  tests/Fortiva.Core.Tests/                        # 200+ tests
dotnet test  tests/Fortiva.AppHost.Tests/ -p:Platform=x64     # ViewModel + Hello tests
```

### Release build + installers

```powershell
./build-release.ps1
./build-installers.ps1   # version from Directory.Build.props (or -Version x.y.z)
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

**[Documentation index](docs/README.md)** — pick your path: user, developer, or security/IT.

| Audience | Document | Description |
|----------|----------|-------------|
| **Users** | [`docs/UserManual.md`](docs/UserManual.md) | Install, vault, Hello, browser Fill, backup, troubleshooting |
| **Users** | [`docs/ONBOARDING-RECOVERY.md`](docs/ONBOARDING-RECOVERY.md) | First run checklist and recovery scenarios |
| **Developers** | [`docs/DEVELOPER-GUIDE.md`](docs/DEVELOPER-GUIDE.md) | Repo layout, on-disk data, key classes, flows, tests |
| **Developers** | [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | System overview, release/update pipeline |
| **Developers** | [`docs/BRIDGE-ARCHITECTURE.md`](docs/BRIDGE-ARCHITECTURE.md) | Browser bridge — loopback HTTP + native fallback |
| **Developers** | [`docs/VAULT-FORMAT.md`](docs/VAULT-FORMAT.md) | `.fva` binary format specification |
| **Security / IT** | [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) | Threat model, trust boundaries, mitigations |
| **Security / IT** | [`docs/POLICY-LICENSING.md`](docs/POLICY-LICENSING.md) | License structure, policy engine |
| **Ops** | [`docs/RELEASE-PIPELINE.md`](docs/RELEASE-PIPELINE.md) | CI/CD — auto-release on push to `main` |
| **Ops** | [`docs/UPDATE-STRATEGY.md`](docs/UPDATE-STRATEGY.md) | Personal auto-update via GitHub Releases |
| **Compliance** | [`PRIVACY.md`](PRIVACY.md) | Privacy policy (local-first, no telemetry) |

## QA

```powershell
dotnet test tests/Fortiva.Core.Tests/
powershell -ExecutionPolicy Bypass -File scripts/qa-stress-audit.ps1 -SkipBuild
```

## CI/CD

GitHub Actions (`.github/workflows/ci.yml`, `release.yml`):

1. **Core** — build + test on every push / PR
2. **Release** — **auto-release on push to `main`**: bump patch version, build installers, publish GitHub Release + `latest.personal.json` (~8–10 min)
3. **CodeQL** — security scanning

Developer flow: `git push origin main` → wait for Release workflow → users **Check for updates** in the app. See [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## License & Privacy

- **License:** Proprietary. See [`LICENSE`](LICENSE) (End User License Agreement). Fortiva Personal is free for personal, non-commercial use; Enterprise/Admin require a paid license.
- **Privacy:** Local-first and zero-knowledge — no telemetry, no accounts, no server-side processing of your vault. See [`PRIVACY.md`](PRIVACY.md) and [fortiva.studio.icmclab.cloud/privacy.html](https://fortiva.studio.icmclab.cloud/privacy.html).
