# Fortiva — application architecture

High-level structure of the Fortiva desktop stack: components, data flow, distribution, and how updates reach installed clients.

| Audience | Start here |
|----------|------------|
| **Users** | [User Manual](UserManual.md) — no code required |
| **Developers** | [Developer guide](DEVELOPER-GUIDE.md) — repo layout, classes, flows |
| **Security / IT** | [Threat model](THREAT-MODEL.md), [Policy & licensing](POLICY-LICENSING.md) |
| **All docs** | [Documentation index](README.md) |

For vault binary layout see [`VAULT-FORMAT.md`](VAULT-FORMAT.md). For on-disk paths see [`DEVELOPER-GUIDE.md`](DEVELOPER-GUIDE.md#on-disk-data-personal).

---

## System overview

```text
┌─────────────────────────────────────────────────────────────────────────┐
│                         Fortiva Personal / Enterprise                    │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌─────────────┐ │
│  │ Fortiva.AppHost │ Fortiva.Core │  │ WinUI 3 UI   │  │ VaultSession│ │
│  │ (Pages, VM)  │  │ crypto/vault │  │ Settings…    │  │ policy/audit│ │
│  └──────┬───────┘  └──────┬───────┘  └──────────────┘  └──────┬──────┘ │
│         │                 │                                      │        │
│         │ BridgeLocalhostServer :7847 + BrowserBridgeCoordinator │        │
└─────────┼─────────────────┼──────────────────────────────────────┼────────┘
          │                 │                                      │
          ▼                 ▼                                      ▼
┌─────────────────┐  ┌──────────────────┐              ┌─────────────────┐
│ Browser ext.    │  │ BrowserBridge    │              │ Local vault     │
│ (MV3 Chromium)  │◄─┤ .Host.exe        │              │ %APPDATA%\      │
│ HTTP :7847 first│  │ one-shot native  │              │ Fortiva\*.fva   │
│ native fallback │  │ → named pipes    │              │ hello.keyprotect│
└─────────────────┘  └──────────────────┘              └─────────────────┘
```

| Layer | Projects / paths | Role |
|-------|------------------|------|
| **UI shell** | `Fortiva.AppHost`, `Fortiva.Personal`, `Fortiva.Enterprise`, `Fortiva.Admin` | WinUI 3 pages, navigation, settings, onboarding |
| **Core** | `Fortiva.Core` | Vault format, Argon2id + AES-GCM (CNG), policy, audit, updates |
| **Browser bridge** | `Fortiva.BrowserBridge.Host`, `extension/`, `BridgeLocalhostServer` | Manual fill only; loopback HTTP + one-shot native messaging fallback |
| **Tooling** | `Fortiva.LicenseTool`, `scripts/`, `packaging/` | Licenses, installers, CI helpers |

---

## Editions

| Edition | Update channel | Network use |
|---------|----------------|-------------|
| **Personal** | GitHub Releases manifest + silent installer | Update check only (HTTPS) |
| **Enterprise** | IT (Intune / manual); no public feed in client | As configured by policy |
| **Admin** | Bundled with enterprise tooling | Local |

---

## Release & update architecture

End-to-end path from developer commit to user installing an update.

### Developer → GitHub (autonomous)

```text
Developer machine                          GitHub
─────────────────                          ──────
git commit
git push origin main  ───────────────────►  main branch updated
                                                    │
                                                    ▼
                                            .github/workflows/release.yml
                                              1. prepare: new commits since
                                                 last v*.*.* tag?
                                              2. auto-bump patch (e.g. 1.0.6)
                                              3. build + test + installers
                                              4. publish GitHub Release + tag
                                              5. sync Directory.Build.props
                                                 [skip release] (no loop)
                                                    │
                                                    ▼
                                            Assets on Release:
                                              • FortivaPersonal-{v}-Setup.exe
                                              • latest.personal.json
```

**Rule:** pushing to `main` with new commits (not already tagged) triggers a release. **No manual git tag required.**

Implementation:

| Piece | Location |
|-------|----------|
| Release workflow | `.github/workflows/release.yml` |
| Version bump helper | `scripts/bump-version.ps1` |
| Legacy push helper | `scripts/publish-release.ps1` (push only; documents auto-release) |
| Manifest generator | `scripts/publish-release-manifest.ps1` |
| Build | `build-release.ps1`, `build-installers.ps1` |

Prepare job logic (summary):

- **Push to `main`:** if `HEAD` ≠ latest tag commit → release next patch version.
- **Push to `v*.*.*` tag:** release that explicit version (manual override).
- **Commit message contains `[skip release]`:** skip (used by version-sync bot commit).
- **`workflow_dispatch`:** optional manual version input.

See [`RELEASE-PIPELINE.md`](RELEASE-PIPELINE.md) for operational detail and troubleshooting.

### GitHub → installed app

```text
GitHub Release (latest)
  latest.personal.json  ──HTTPS──►  UpdateChecker (Fortiva.Core)
  FortivaPersonal-{v}-Setup.exe       ReleaseManifestLoader
         │                            UpdateUrlPolicy (host allow-list)
         │                            SHA-256 verify
         └──────────────────────────►  Silent Inno Setup (/VERYSILENT)
                                       Vault data preserved
```

| Component | Location |
|-----------|----------|
| Update orchestration | `src/Fortiva.AppHost/Services/UpdateService.cs` |
| Manifest fetch + policy | `src/Fortiva.Core/Updates/` |
| User trigger | Settings → **Check for updates**; optional auto-check on launch (24h) |
| Manifest URL | `ReleaseManifestUrls.PersonalLatest` → `…/releases/latest/download/latest.personal.json` |

**Important:** the client compares **GitHub Releases `latest`**, not the `main` branch. Until the Release workflow finishes (~8–10 min), “Check for updates” may still show the previous version.

### Typical release timeline

| Time | What happens |
|------|----------------|
| T+0 | `git push origin main` |
| T+1 min | GitHub Actions **Release** workflow starts |
| T+8–10 min | Workflow green; `latest.personal.json` shows new version |
| T+10 min+ | User: Settings → **Check for updates** → **Install now** |

---

## Browser extension architecture

One-click setup from the app. **Personal:** auto-load via `--load-extension` when the browser
is closed, or prompt to close-and-relaunch; manual **Load unpacked** remains the fallback.
**Enterprise:** installer sets `ExtensionInstallForcelist` + HKLM native messaging (IT-managed);
Intune Win32 packaging and daily HKLM drift repair are documented in [`packaging/intune/README.md`](../packaging/intune/README.md).

As of **v1.0.37+**, the extension uses **loopback HTTP first** (`http://127.0.0.1:7847`) with a
session token, falling back to **one-shot** `sendNativeMessage` per operation when HTTP is unavailable.
There is **no persistent native port**, **no push cache**, and **no `cachedSessionToken` bypass**.
Full design: [`BRIDGE-ARCHITECTURE.md`](BRIDGE-ARCHITECTURE.md).

```text
App launch / Connect browser
        │
        ▼
BrowserBridgeInstallService
  • copy extension/ → %LOCALAPPDATA%\Fortiva{Personal|Enterprise}\extension
  • write native-messaging JSON
  • register HKCU Chrome + Edge keys
  • Enterprise: HKLM native messaging under Program Files (installer + Deploy-Intune.ps1 repair)
        │
        ▼
Personal: --load-extension / close-browser prompt / Load unpacked
Enterprise: policy force-install from GitHub CRX + updates.xml
        │
        ▼
Fortiva.Personal.exe (vault unlocked)
  • BridgeLocalhostServer :7847 — GET status/matches, POST execute-fill (token auth)
  • Named pipes — native host when extension falls back from HTTP
        │
        ▼
extension/background.js
  • HTTP /auth/session → bridge token → /status-and-matches
  • Fallback: sendNativeMessage → one-shot Fortiva.BrowserBridge.Host.exe → pipes → VaultSession
  • User-initiated Fill only (content script)
```

| Component | Location |
|-----------|----------|
| Install / register | `Fortiva.Core/BrowserBridge/BrowserBridgeInstallService.cs` |
| Loopback server | `Fortiva.Core/BrowserBridge/BridgeLocalhostServer.cs` |
| UI helper | `Fortiva.AppHost/Services/BrowserExtensionSetupHelper.cs` |
| Extension | `extension/` (MV3, manual fill only) |
| Verification script | `scripts/test-browser-extension.ps1` |

---

## CI / quality gates

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `ci.yml` | Push / PR to `main` | Core + AppHost tests, WinUI build, CodeQL |
| `release.yml` | Push to `main`, version tags, manual dispatch | Build installers, publish release, update manifest |

Tests run **before** release artifacts are published.

---

## Repository map (documentation)

Full index with audience tags: **[README.md](README.md)**.

| Document | Topic |
|----------|--------|
| [`DEVELOPER-GUIDE.md`](DEVELOPER-GUIDE.md) | Repo layout, on-disk data, key classes, runtime flows |
| [`UserManual.md`](UserManual.md) | End-user install, vault, browser extension |
| [`BRIDGE-ARCHITECTURE.md`](BRIDGE-ARCHITECTURE.md) | Loopback HTTP + one-shot native host |
| [`BRIDGE-VALIDATION.md`](BRIDGE-VALIDATION.md) | Bridge smoke tests (v1.0.37+) |
| [`VAULT-FORMAT.md`](VAULT-FORMAT.md) | `.fva` binary specification |
| [`THREAT-MODEL.md`](THREAT-MODEL.md) | Trust boundaries and mitigations |
| [`RELEASE-PIPELINE.md`](RELEASE-PIPELINE.md) | CI release workflow |
| [`UPDATE-STRATEGY.md`](UPDATE-STRATEGY.md) | Personal auto-update behaviour |
| [`POLICY-LICENSING.md`](POLICY-LICENSING.md) | Enterprise license and policy engine |

---

## Related repositories

| Repo | Purpose |
|------|---------|
| **ICITPROJ/Fortiva** | Desktop app, installers, update manifest (this repo) |
| **ICITPROJ/Fortiva-Website** | Marketing static site (Azure SWA — separate CI) |

Do not confuse the two when checking GitHub Actions or Releases.
