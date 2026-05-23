# Fortiva — Windows-Native Password Manager

> Zero-knowledge · Local-first · Living-off-the-land · No background services

Fortiva is a Windows-native password manager built entirely on the Windows stack.
All cryptographic operations use Windows CNG. There is no cloud sync, no telemetry,
and no Electron runtime.

## Components

| Component | Description |
|-----------|-------------|
| `Fortiva.Core` | Shared library: vault format, crypto, policy, audit, Hello |
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
- **SecureZeroMemory**: All sensitive buffers explicitly zeroed via `CryptographicOperations.ZeroMemory`.

## Build

### Prerequisites

- .NET 8 SDK (`dotnet --version` ≥ 8.0)
- Windows 10 1903+ (19041) or Windows 11
- Visual Studio 2022 with **Windows App SDK** workload (WinUI builds only)

### Core library + tests (CLI, no VS required)

```powershell
dotnet build src/Fortiva.Core/Fortiva.Core.csproj -c Release
dotnet test  tests/Fortiva.Core.Tests/                        # 54 tests
```

### License tool (CLI)

```powershell
dotnet build src/Fortiva.LicenseTool/ -c Release

# Generate a new RSA 2048 key pair
dotnet run --project src/Fortiva.LicenseTool -- generate-key

# Sign a license (requires private key XML)
dotnet run --project src/Fortiva.LicenseTool -- sign "Acme Corp" 365 private-key.xml

# Verify a license
dotnet run --project src/Fortiva.LicenseTool -- verify fortiva-license-acme-corp.json
```

### WinUI applications (requires Visual Studio)

```powershell
dotnet build src/Fortiva.Personal/    -c Release
dotnet build src/Fortiva.Enterprise/  -c Release
dotnet build src/Fortiva.Admin/       -c Release
```

### MSIX packaging

See `packaging/msix/` — open `Fortiva.Personal.wapproj` in VS with
"MSIX Packaging Tools" workload installed.

## Distribution

| Channel | App |
|---------|-----|
| Microsoft Store | `Fortiva.Personal` |
| Winget | `winget install Fortiva.Personal` (see `packaging/winget/`) |
| EXE installer | `FortivaPersonal-Setup-x64.exe` (Inno Setup, `packaging/installer/`) |
| Intune / Endpoint Manager | `.intunewin` wrap (see `packaging/intune/`) |
| SCCM / GPO | Silent install via EXE `/VERYSILENT` |

## Documentation

| Document | Description |
|----------|-------------|
| [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) | Threat model, trust boundaries, mitigations |
| [`docs/VAULT-FORMAT.md`](docs/VAULT-FORMAT.md) | `.fva` binary format specification |
| [`docs/POLICY-LICENSING.md`](docs/POLICY-LICENSING.md) | License structure, policy engine |
| [`docs/ONBOARDING-RECOVERY.md`](docs/ONBOARDING-RECOVERY.md) | Onboarding, panic lock, snapshot recovery |

## Test matrix

| Suite | Tests | Description |
|-------|-------|-------------|
| `Crypto/CngAesGcmTests` | 4 | AES-256-GCM round-trip, tampering |
| `Crypto/Argon2Tests` | 5 | KDF determinism, salt uniqueness, serialization |
| `Crypto/KeyHierarchyTests` | 5 | MK→VK wrap/unwrap, wrong password, payload AEAD |
| `Vault/VaultEngineTests` | 4 | Create/unlock/add/rollback |
| `Vault/VaultParserFuzzTests` | 2 | Garbage/bad-magic rejection |
| `Vault/VaultIntegrationTests` | 8 | Full workflow, 10k entries, rapid lock/unlock |
| `Vault/AutoLockTimerTests` | 3 | Fire, reset, dispose |
| `Password/PasswordStrengthTests` | 6 | Scoring, entropy, generation |
| `Policy/PolicyEnforcerTests` | 3 | KDF enforcement, clipboard, export |
| `Licensing/LicenseTests` | 3 | Sign/verify, tamper, expired |
| **Total** | **43+** | All passing (`dotnet test`) |

## CI/CD

GitHub Actions (`.github/workflows/ci.yml`):
1. **Core** — build + test + Roslyn analyzers
2. **LicenseTool** — build + smoke test
3. **BrowserBridge.Host** — build
4. **WinUI** — build (VS workload required; `continue-on-error`)
5. **CodeQL** — security scanning across all managed projects
