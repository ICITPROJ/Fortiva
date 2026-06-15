# Fortiva documentation

This folder contains all product documentation for **Fortiva** (v1.0.37+). Pick the path that matches your role.

---

## I am a user

Start here if you use Fortiva Personal or Enterprise on your PC.

| Document | What you get |
|----------|----------------|
| **[User Manual](UserManual.md)** | Full guide: install, vault, Hello, browser Fill, import/export, settings, troubleshooting |
| **[Onboarding & recovery](ONBOARDING-RECOVERY.md)** | Short checklist: first run, daily habits, lost password, snapshots |
| **[Privacy policy](../PRIVACY.md)** | What Fortiva stores locally and the one optional network call (updates) |

**Quick paths in the app**

| Goal | Where to go |
|------|-------------|
| Save a password | **My Vault** → **+ Add entry** |
| Fill a website login | Browser toolbar → Fortiva icon → **Fill** (set **Website** on the entry first) |
| Check password health | **Security audit** (runs when you open the page) |
| Back up the vault | **Import / Export** → **Export encrypted** |
| Change auto-lock / theme | **Settings** |

---

## I am a developer or architect

Start here if you build, review, or integrate Fortiva.

| Document | What you get |
|----------|----------------|
| **[Developer guide](DEVELOPER-GUIDE.md)** | Repo layout, on-disk data, key classes, runtime flows, how to test |
| **[Architecture](ARCHITECTURE.md)** | System diagram, editions, release/update pipeline, doc map |
| **[Vault format](VAULT-FORMAT.md)** | `.fva` binary layout, key hierarchy, snapshots |
| **[Bridge architecture](BRIDGE-ARCHITECTURE.md)** | Loopback HTTP `:7847`, native fallback, API, error codes |
| **[Threat model](THREAT-MODEL.md)** | Trust boundaries, assets, mitigations, explicit non-goals |
| **[Update strategy](UPDATE-STRATEGY.md)** | Personal auto-update behaviour and guarantees |
| **[Release pipeline](RELEASE-PIPELINE.md)** | CI/CD, GitHub Releases, troubleshooting |
| **[Policy & licensing](POLICY-LICENSING.md)** | Enterprise license JSON and policy engine |

**Build & verify**

```powershell
dotnet build src/Fortiva.Core/Fortiva.Core.csproj -c Release
dotnet test tests/Fortiva.Core.Tests/
dotnet test tests/Fortiva.AppHost.Tests/ -p:Platform=x64
./build-release.ps1
./scripts/test-browser-extension.ps1
./scripts/Test-BrowserBridgeE2E.ps1 -RequireReady   # vault must be unlocked
```

Root **[README](../README.md)** covers installer build and distribution channels.

---

## I am security / IT / compliance

| Document | What you get |
|----------|----------------|
| **[Threat model](THREAT-MODEL.md)** | Adversary model and controls |
| **[Deep audit](DEEP-AUDIT.md)** | Historical findings register (many marked FIXED) |
| **[Security remediation](SECURITY-REMEDIATION-2026.md)** | Pen-test fixes and accepted residual risks |
| **[Security pentest report](SECURITY-PENTEST-REPORT.md)** | Full adversarial review |
| **[Military-grade spec](MILITARY-GRADE-SPEC.md)** | SR-* requirement checklist |
| **[Code signing](CODESIGNING.md)** | Authenticode policy (deferred for Personal) |
| **[Intune packaging](../packaging/intune/README.md)** | Enterprise deployment |

---

## Document conventions

- **Version:** Product version lives in `Directory.Build.props` (currently **1.0.37**). Extension manifest must match for bridge QA.
- **Paths:** `%AppData%` = roaming profile (`FortivaPaths.PersonalDataRoot`). `%LocalAppData%` = local app data (`FortivaPersonal` staging, logs, pre-update backups).
- **Vault file:** On-disk encrypted store is always `vault.fva`. Encrypted **export** backups use `.fva` (import also accepts `.fvab` / `.json`).

---

## Related repos

| Repo | Purpose |
|------|---------|
| **ICITPROJ/Fortiva** | Desktop app, installers, update manifest (this repo) |
| **ICITPROJ/Fortiva-Website** | Marketing site (separate CI) |

Do not check Actions or Releases on the website repo when debugging desktop releases.
