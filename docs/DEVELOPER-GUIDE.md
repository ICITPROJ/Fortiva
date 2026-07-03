# Fortiva — developer guide

For **end users**, see [User Manual](UserManual.md). For **system diagrams and release flow**, see [Architecture](ARCHITECTURE.md).

This guide helps engineers and architects navigate the codebase, data on disk, and main runtime paths.

---

## Solution layout

```text
Fortiva/
├── src/
│   ├── Fortiva.Core/              # Crypto, vault engine, policy, bridge, updates (no UI)
│   ├── Fortiva.AppHost/           # Shared WinUI 3 pages, ViewModels, services
│   ├── Fortiva.Personal/          # Personal entry EXE (thin shell → AppHost)
│   ├── Fortiva.Enterprise/        # Enterprise entry EXE
│   ├── Fortiva.Admin/             # IT admin console
│   ├── Fortiva.BrowserBridge.Host/# Native messaging one-shot host
│   └── Fortiva.LicenseTool/       # CLI license sign/verify
├── extension/                     # Chromium MV3 extension (background + popup)
├── tests/
│   ├── Fortiva.Core.Tests/
│   └── Fortiva.AppHost.Tests/
├── packaging/installer/           # Inno Setup scripts
├── scripts/                       # CI helpers, bridge E2E, QA
└── docs/                          # All product documentation
```

| Project | Depends on | Role |
|---------|------------|------|
| `Fortiva.Personal` / `Enterprise` / `Admin` | `Fortiva.AppHost` | Edition-specific startup, branding, policy hooks |
| `Fortiva.AppHost` | `Fortiva.Core` | All UI pages (`Pages/`), `ShellViewModel`, `UpdateService`, Hello UI |
| `Fortiva.Core` | — | Single source of truth for vault, crypto, bridge pipes, import/export |
| `Fortiva.BrowserBridge.Host` | `Fortiva.Core` | Spawned by browser; talks to app via named pipes |

**Entry points:** `Fortiva.Personal/Program.cs` → `AppHost/App.xaml.cs` → `MainWindow.xaml`.

---

## On-disk data (Personal)

Canonical paths are defined in `Fortiva.Core/Platform/FortivaPaths.cs`. Keep installer uninstall scripts in sync.

### Roaming — `%AppData%\Fortiva\` (`PersonalDataRoot`)

| File | Secret? | Purpose |
|------|---------|---------|
| `vault.fva` | Encrypted | Primary vault (entries + integrity log inside) |
| `vault.fva.snapshot1` … `snapshotN` | Encrypted | Rolling recovery copies (default N=5) |
| `local.state` | DPAPI metadata | Rollback detection (revision, vault ID, security level) |
| `hello.keyprotect` | Protected blob | Windows Hello key protector (v3 software or v4 TPM) |
| `hello.binding` | Protected blob | Optional TPM upgrade sidecar |
| `user.prefs.json` | No | Auto-lock, theme, portable path, update prefs |

### Local — `%LocalAppData%\FortivaPersonal\`

| Path | Purpose |
|------|---------|
| `extension/` | Staged copy of `extension/` for browser load |
| `fortiva-crash.log` | Local error log (no passwords) |
| `appearance.json` | Theme persistence |
| `pre-update-backups/` | Last 3 vault sidecar copies before auto-update |
| `audit/` | Personal audit log (if enabled) |

### Install — `%LocalAppData%\Programs\icmclab studio\Fortiva Personal\`

Application binaries only. **Never** store vault data here. Updates replace this folder; user data survives.

### Enterprise differences

- Vault: `%ProgramData%\Fortiva\vault.fva`
- Policy/license: `%ProgramData%\Fortiva\`
- Hello: `%LocalAppData%\FortivaEnterprise\Hello\` (enterprise) or next to shared vault path
- Local staging: `%LocalAppData%\FortivaEnterprise\extension\`

---

## Key classes (where logic lives)

| Concern | Primary types | Path |
|---------|---------------|------|
| Unlock / save vault | `VaultEngine`, `VaultSession` | `Fortiva.Core/Vault/` |
| Master password KDF | `Argon2Kdf`, `CngAesGcm` | `Fortiva.Core/Crypto/` |
| Windows Hello | `HelloCredentialStore`, `WindowsHelloKeyProtector` | `Fortiva.Core/Hello/`, `AppHost/Services/HelloUnlockManager.cs` |
| Rollback / paranoia | `DpapiLocalStateStore` | `Fortiva.Core/LocalState/` |
| Import / export | `VaultImporter`, `VaultExporter`, `ImportMergeService` | `Fortiva.Core/ImportExport/` |
| Security audit | `SecurityAuditRunner` | `Fortiva.Core/Security/` |
| Auto-update | `UpdateService`, `PreUpdateVaultBackup` | `AppHost/Services/`, `Core/Updates/` |
| Loopback bridge | `BridgeLocalhostServer` | `Fortiva.Core/BrowserBridge/` |
| Native bridge | `NativeMessagingHostPump`, `BridgeNativeForwarder` | `Fortiva.Core/BrowserBridge/` |
| Pipe brokers | `BridgeTokenBroker`, `BridgeCredentialBroker` | `Fortiva.Core/BrowserBridge/` |
| Extension install | `BrowserBridgeInstallService` | `Fortiva.Core/BrowserBridge/` |
| UI shell state | `ShellViewModel` | `Fortiva.AppHost/ViewModels/` |
| Policy (Enterprise) | `PolicyEnforcer`, `LicenseStore` | `Fortiva.Core/Policy/`, `Licensing/` |

---

## Runtime flows

### 1. Unlock (master password)

```text
UnlockPage → ShellViewModel.UnlockAsync
  → VaultEngine.Unlock(vault.fva, password)
  → Argon2id → MK → unwrap VK → decrypt payload
  → DpapiLocalStateStore.CheckRollback
  → VaultSession (in-memory entries + integrity log)
  → BridgeCoordinator starts BridgeLocalhostServer :7847
```

Hello path: `HelloUnlockManager` → `HelloCredentialStore.TryUnwrapMasterKeyAsync` → same `VaultSession` attach.

### 2. Save entry

```text
EntryPage → VaultSession.AddOrUpdateEntry
  → PolicyEnforcer (Enterprise)
  → VaultEngine atomic write (tmp → replace → snapshot rotate)
  → Integrity log append
```

### 3. Browser Fill (HTTP path, v1.0.57+)

```text
extension/background.js
  → sendNativeMessage({ command: "get_session_token" })
  ← { bridgeToken, status }   (via validated named pipe — HTTP never mints tokens)
  → GET /status-and-matches?domain=&url=  (X-Fortiva-Bridge-Token)
  ← { matches, fillNonce }
User clicks Fill
  → POST /execute-fill { entryId, fillNonce, domain, url }
  ← credentials
  → content script injects fields
```

If token fetch or authed HTTP fails, extension falls back to native `get_status_and_matches` / `execute_fill`.

`POST /auth/session` is deprecated (status-only; no token). See [BRIDGE-ARCHITECTURE.md](BRIDGE-ARCHITECTURE.md).

Native fallback: `sendNativeMessage` → `Fortiva.BrowserBridge.Host.exe` (one shot) → named pipes → same `VaultSession`.

### 4. Personal auto-update

```text
UpdateService (≤ once per 24h on launch, or manual)
  → fetch latest.personal.json (GitHub Releases)
  → SHA-256 verify installer
  → PreUpdateVaultBackup.TryCreate (vault + sidecars)
  → lock vault, stop bridge host
  → Inno /VERYSILENT /FORCECLOSEAPPLICATIONS
  → installer relaunches Fortiva.Personal.exe
```

See [UPDATE-STRATEGY.md](UPDATE-STRATEGY.md) and [RELEASE-PIPELINE.md](RELEASE-PIPELINE.md).

---

## UI architecture (AppHost)

| Pattern | Implementation |
|---------|----------------|
| Navigation | `NavigationService`, `MainWindow.xaml` `NavigationView` |
| Shared styling | `Resources/FortivaTheme.xaml` — `FortivaPageScrollViewer`, `FortivaPagePanel`, toolbar |
| Page layout | Toolbar header row + full-width scroll content (Settings, Health, Import/Export, Generator) |
| Global search | `MainWindow` `AutoSuggestBox`; **Ctrl+K** when unlocked |
| Command palette | `CommandPalette.cs`; **Ctrl+Shift+P** when unlocked |
| Auto-lock | `ShellViewModel` timer; suppressed during `IsBusy` / password change |

Main pages: `VaultPage`, `EntryPage`, `UnlockPage`, `SettingsPage`, `HealthPage`, `ImportExportPage`, `PasswordGeneratorPage`, `OnboardingPage`.

---

## Testing

| Layer | Command |
|-------|---------|
| Core unit tests | `dotnet test tests/Fortiva.Core.Tests/` |
| AppHost / Hello | `dotnet test tests/Fortiva.AppHost.Tests/ -p:Platform=x64` |
| Bridge localhost | `BridgeLocalhostServerTests` (ephemeral port) |
| Extension staging | `scripts/test-browser-extension.ps1` |
| Bridge E2E | `scripts/Test-BrowserBridgeE2E.ps1 -RequireReady` |
| Full QA | `scripts/qa-stress-audit.ps1` |

**Bridge E2E** requires Fortiva running with vault unlocked and extension connected.

---

## Version and release

- Version: `Directory.Build.props` → `extension/manifest.json` must match (CI enforces).
- Push to `main` → `release.yml` auto-bumps patch, builds installers, publishes GitHub Release + `latest.personal.json`.
- Bot commit `[skip release]` syncs props only.

---

## Further reading

| Topic | Document |
|-------|----------|
| Doc index (all audiences) | [docs/README.md](README.md) |
| Binary vault spec | [VAULT-FORMAT.md](VAULT-FORMAT.md) |
| Security boundaries | [THREAT-MODEL.md](THREAT-MODEL.md) |
| Bridge validation | [BRIDGE-VALIDATION.md](BRIDGE-VALIDATION.md) |
| Enterprise policy | [POLICY-LICENSING.md](POLICY-LICENSING.md) |
