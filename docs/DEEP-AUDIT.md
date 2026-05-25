# Fortiva Deep Security & Quality Audit

**Date:** 2026-05-24  
**Scope:** Full codebase — Core crypto/vault, AppHost UI, browser bridge, installers, CI/release, docs  
**Editions:** Personal, Enterprise, Admin  
**Method:** Static analysis, threat-model cross-check, installer script review, test gap analysis, prior crash-log correlation

---

## Executive summary

Fortiva’s vault format, Argon2id + AES-GCM (CNG) stack, and DPAPI layering are sound for an offline password manager. The **highest-risk gaps** are not in primitive crypto but in **integration**: Windows Hello stores the master key in a DPAPI-only blob (UI gate only), rollback detection can be bypassed by deleting `local.state`, release builds can ship **version mismatches** (infinite update loop), and several **AppHost flows** can lock users out or auto-lock mid-operation.

This document lists every finding by severity. Items marked **FIXED** were addressed in the audit follow-up pass; **OPEN** items remain for future work.

---

## Critical (C)

| ID | Area | Finding | Impact | Status |
|----|------|---------|--------|--------|
| C1 | Hello / Core | `WindowsHelloKeyProtector` stores MK in DPAPI blob; Hello is a **UI consent gate**, not TPM-gated key wrap | Local attacker with user session can extract MK without biometrics | **FIXED** — KeyCredential v4 + HelloVerificationGate |
| C2 | Rollback | Deleting `%VaultDir%\local.state` bypasses rollback detection (`DpapiLocalStateStore.CheckRollback` returned OK when file missing) | Attacker restores old vault revision undetected | **FIXED** — missing state + `RevisionCounter > 1` flagged |
| C3 | Snapshots | `VaultEngine.UnlockFromSnapshot` used `confirmRollback: true`, opening snapshots writable without user confirmation | Silent rollback acceptance on snapshot restore | **FIXED** — defaults to read-only until confirmed |
| C4 | Release | `release.yml` tags installers as `1.0.1` but `build-release.ps1` did not propagate version to assemblies (`AppVersion.Current` stays `1.0.0`) | Infinite auto-update loop | **FIXED** — `-Version` passed through MSBuild |
| C5 | Enterprise installer | `FortivaEnterprise.iss` killed `Fortiva.Personal.exe` instead of `Fortiva.Enterprise.exe` | Enterprise upgrade/uninstall may leave wrong process running | **FIXED** |

---

## High (H)

| ID | Area | Finding | Impact | Status |
|----|------|---------|--------|--------|
| H1 | Bridge | Session token persisted on disk (`bridge.session` under app data) | Token theft enables pipe RPC until lock | OPEN — prefer in-process token only |
| H2 | Bridge | Credentials returned in plaintext over named pipe to extension | Malware on same user session can sniff pipe | OPEN — document threat; consider short-lived pipe encryption |
| H3 | Bridge | `BridgeClientValidator` allowed any path when install roots empty | Dev/misconfigured hosts accept impostor clients | **FIXED** — fail closed |
| H4 | AppHost | Mandatory Hello + unavailable hardware = **total lockout** on unlock screen | Enterprise users blocked on VMs / broken Hello | **FIXED** — password fallback when HW unavailable |
| H5 | AppHost | Auto-lock could fire during master password change / bulk import | Operation interrupted; possible corrupt UX state | **FIXED** — suppress auto-lock + `IsBusy` gate |
| H6 | Policy | `PolicyEnforcer` not applied inside Core vault mutations | Policy bypass if UI circumvented | OPEN |
| H7 | Licensing | `LicenseStore.TryImportFromFile` deserialized JSON without signature verify | Unsigned JSON accepted if caller skips verify | **FIXED** — verify on import |
| H8 | AppHost | Stale Hello credential under mandatory policy left password disabled after clear | User lockout after Hello reset | **FIXED** — re-apply unlock controls |
| H9 | AppHost | Admin license import called `ReloadPolicies()` not `ReloadEnterpriseConfig()` | License state stale in running Admin session | **FIXED** |
| H10 | AppHost | Import while locked threw from `RequireSession()` | Crash / bad UX on Import page | **FIXED** — guard `IsUnlocked` |

---

## Medium (M)

| ID | Area | Finding | Impact | Status |
|----|------|---------|--------|--------|
| M1 | AppHost | Onboarding Hello sync after navigate could throw on disposed session | Finish flow error after success | **FIXED** — guard + try/catch |
| M2 | AppHost | Portable vault switch without prominent “return to local vault” risks perceived data loss | User confusion | OPEN — UX copy/warning |
| M3 | AppHost | `AppViewModel.cs` duplicate dead code vs `ShellViewModel` | Maintenance drift | **FIXED** — removed |
| M4 | QA scripts | `qa-stress-audit.ps1` compiled ISCC without `/DExtensionId` | QA installers ≠ production bridge manifests | **FIXED** |
| M5 | CI | `release.yml` runs Core tests only; no AppHost tests or installer smoke | Regressions ship | OPEN |
| M6 | Audit | Personal audit trail new; no hash chaining / tamper evidence | Log tampering possible | OPEN |
| M7 | Vault | `SecurityLevel` header not re-validated against policy max on unlock | Downgrade if file edited | OPEN |
| M8 | Updates | Silent auto-update previously called `Environment.Exit(0)` | Abrupt termination | FIXED (prior pass) |
| M9 | Docs | THREAT-MODEL still says “no network” for Personal; MSIX vs EXE deployment stale | Wrong operator assumptions | OPEN |

---

## Low (L)

| ID | Area | Finding | Status |
|----|------|---------|--------|
| L1 | AppHost | Settings paranoia toggle could fire during load | **FIXED** — detach handler during load |
| L2 | Core | `PolicyEnforcer` clipboard timeout not enforced in all copy paths | OPEN |
| L3 | Packaging | Empty parent folder cleanup on uninstall — edge cases on multi-user PC | OPEN |
| L4 | Extension | Stable extension ID requires manifest `key` at build — documented in scripts | OK |
| L5 | Tests | No dedicated `DpapiLocalState` / snapshot rollback tests | **FIXED** — tests added |

---

## Architecture notes

### Vault & crypto (sound)

- **KDF:** Argon2id (Konscious) with per-vault salt; parameters stored in header.
- **Encryption:** AES-256-GCM via CNG for payload + integrity MAC.
- **Key hierarchy:** Master password → MK → VK; Hello stores MK copy (see C1).
- **Snapshots:** Rotating encrypted copies; restore should be read-only until rollback confirmed (C3).

### Windows Hello (gap)

Current flow: UI verifies Hello → loads DPAPI-protected MK blob. **No** `KeyCredentialManager` / TPM wrap of vault key. Threat: same-user malware after one Hello unlock.

**Recommendation:** Wrap MK with Hello-protected key using WinRT `KeyCredentialManager` or NCrypt platform key; never store raw MK in DPAPI-only blob.

### Browser bridge

- Named pipe + session token; client validated by process name + install path (H3 fixed).
- Extension ID stable: `llkpcnbhmhpenahlcdnbbfmkdfkgnpnj` (RSA key in `extension/manifest.json`).
- Token on disk (H1) and plaintext creds on pipe (H2) remain accepted risks for v1 offline threat model.

### Rollback / paranoia

- `local.state` (DPAPI) records max security level, vault ID, revision counter, last modified.
- Paranoia mode forces read-only on suspicious downgrade even after confirmation prompt.
- Deleting `local.state` no longer silently OK for established vaults (C2).

---

## Installer & uninstall audit

| Check | Personal | Enterprise | Admin |
|-------|----------|------------|-------|
| Kill main EXE on uninstall | Personal.exe | **Enterprise.exe** (was wrong) | Admin.exe |
| Kill BrowserBridge.Host | Yes | Yes | N/A |
| Vault delete prompt | Yes | Yes | N/A |
| Hello / binding cleanup | Yes | Yes | N/A |
| Audit log delete prompt | Yes | Yes | Yes |
| Legacy `%AppData%\Fortiva` path | Checked | N/A | N/A |
| Temp update EXE cleanup | Yes | — | — |

---

## CI / release pipeline

| Step | Current | Gap |
|------|---------|-----|
| Core unit tests | Yes | — |
| AppHost tests | No in CI | Add `Fortiva.AppHost.Tests` |
| Version sync | **Fixed** — `build-release.ps1 -Version` | — |
| Installer build | Yes with ExtensionId | — |
| QA stress script | **Fixed** ExtensionId | — |
| Authenticode signing | Optional / manual | Production needs sig |

---

## Test coverage snapshot

| Suite | Count | Notes |
|-------|-------|-------|
| Fortiva.Core.Tests | 128+ | Crypto, vault, bridge validator |
| Fortiva.AppHost.Tests | 4 | ShellViewModel threading/unlock |
| Installer E2E | Manual / qa-stress | Not in CI |

---

## Prioritized remediation roadmap

### Done (this audit pass)

1. Enterprise installer process kill  
2. Release version propagation  
3. Rollback detection hardening  
4. Snapshot read-only default  
5. Bridge validator fail-closed  
6. Unlock mandatory-Hello recovery paths  
7. Auto-lock suppression during sensitive ops  
8. Import/admin/onboarding fixes  
9. QA script ExtensionId  
10. Remove dead `AppViewModel`

### Next sprint (recommended)

1. ~~**C1** — TPM/Hello-gated MK wrap (Core + AppHost)~~ **Done**  
2. **H6** — Enforce policy in `VaultSession` mutations  
3. **H1** — In-process bridge token only  
4. **M5** — CI: AppHost tests + installer compile  
5. **M6** — Audit log hash chain  
6. **M9** — Refresh THREAT-MODEL and deployment docs  

---

## References

- Crash log root cause (onboarding): COMException `0x8001010E` — UI thread marshalling (fixed)  
- Vault path: `%AppData%\Fortiva\vault.fva` (Personal default)  
- Extension build scripts: `scripts/compute-extension-id.ps1`, `scripts/write-browser-bridge-manifests.ps1`  
- Path contracts: `src/Fortiva.Core/Platform/FortivaPaths.cs`

---

*This audit is a point-in-time review. Re-run after major feature work or before production certification.*
