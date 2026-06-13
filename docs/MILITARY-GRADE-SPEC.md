# Fortiva Military-Grade Security Specification

**Version:** 1.0.55  
**Status:** Personal — **Tier B+** certified in automation; Enterprise — **Tier A−** when signed + Intune deployed  
**Audience:** Security review, IT deployment, release engineering

This document is the authoritative checklist for “military-grade” Fortiva. Each requirement maps to code, tests, or an explicit accepted risk.

---

## 1. Threat model

| Adversary | In scope | Out of scope |
|-----------|----------|--------------|
| Same-user malware while vault unlocked | **Yes** | — |
| Phishing / homograph login pages | **Yes** | — |
| Registry / native-host hijack (HKCU) | **Partial** (mitigated on launch) | — |
| Kernel / rootkit / live RAM scraping | — | **Yes** |
| Remote network attacker (no local access) | — | **Yes** (offline-first) |

**Design principle:** Fail closed on authentication; minimize secret lifetime on IPC; never autofill without explicit user action.

---

## 2. Requirement matrix

### SR-VAULT — Cryptography & storage

| ID | Requirement | Implementation | Verify |
|----|-------------|----------------|--------|
| SR-VAULT-01 | Argon2id KDF with per-vault salt | `Argon2Kdf`, `KeyHierarchy` | `Fortiva.Core.Tests` crypto suite |
| SR-VAULT-02 | AES-256-GCM payload + header MAC | `CngAesGcm`, `VaultSerializer` | Unit tests |
| SR-VAULT-03 | Rollback detection via DPAPI `local.state` | `DpapiLocalState.CheckRollback` | `DpapiLocalStateTests` |
| SR-VAULT-04 | Optimistic concurrency (revision counter) | `VaultConcurrencyException` | `VaultEngineTests` |
| SR-VAULT-05 | Integrity log hash chain | `IntegrityValidator` | Integration tests |
| SR-VAULT-06 | Hello v4 TPM wrap when available | `HelloCredentialStore` HKDF(signature, challenge) | Manual + Settings |
| SR-VAULT-07 | Hello v3 fallback gated by UserConsentVerifier | `WindowsHelloKeyProtector` | Manual |

### SR-BRIDGE — Browser IPC

| ID | Requirement | Implementation | Verify |
|----|-------------|----------------|--------|
| SR-BRIDGE-01 | Pipe ACL: current user only | `BridgePipeListener.CreateSecuredServerStream` | Code review |
| SR-BRIDGE-02 | Client PID/path validation on token + credential pipes | `validateClients: true` on brokers | `Audit-MilitaryGrade.ps1` |
| SR-BRIDGE-03 | Unlock pipe validates bridge host on UNLOCK | `BridgePipeGuard` in `BridgeUnlockBroker` | Unit tests |
| SR-BRIDGE-04 | Session token in-memory only (no disk) | `BridgeSessionAuth`, `BridgeTokenBroker` | Code + tests |
| SR-BRIDGE-05 | Username + password sealed on release pipe (AES-GCM + HKDF) | `BridgeCredentialProtector` | `BridgeCredentialProtectorTests` |
| SR-BRIDGE-06 | Fill nonce: per-host, TTL 2 min, single-use | `BridgeFillNonce` | `BridgeFillNonceTests` |
| SR-BRIDGE-07 | List: registrable domain; **release: exact host** | `HostsMatchForAutofill` / `HostsMatchForCredentialRelease` | `DomainSafetyTests` |
| SR-BRIDGE-08 | Punycode `xn--` labels rejected | `DomainSafety.ContainsAceEncodedLabel` | `DomainSafetyTests` |
| SR-BRIDGE-09 | Native host path-under-install | `BridgeClientValidator`, `NativeHostIntegrity` | `BridgeAppLauncherTests` |
| SR-BRIDGE-10 | Bounded pipe reads (DoS) | `BridgeJson.ReadBoundedLineAsync` | Unit tests |
| SR-BRIDGE-11 | Single native-host spawn per Fill | `execute_fill` in `BridgeNativeForwarder` | Extension matrix |
| SR-BRIDGE-12 | Unlock rate limit surfaced to user | `RATE_LIMITED` error path | Extension matrix |

### SR-EXT — Browser extension

| ID | Requirement | Implementation | Verify |
|----|-------------|----------------|--------|
| SR-EXT-01 | User-initiated fill only (no content scripts) | `manifest.json` — popup + background only | Manifest review |
| SR-EXT-02 | Stable extension ID pinned | `BrowserExtensionConstants.StableExtensionId` | Install service |
| SR-EXT-03 | `sender.id === chrome.runtime.id` | `background.js` | Code review |
| SR-EXT-04 | SPA / React input events | Native value setter + `InputEvent` | Manual IONOS |
| SR-EXT-05 | Multi-step login UX | `password_step_pending` message | Manual |

### SR-ENT — Enterprise deployment

| ID | Requirement | Implementation | Verify |
|----|-------------|----------------|--------|
| SR-ENT-01 | HKLM `ExtensionInstallForcelist` | `FortivaEnterprise.iss` | Installer + Intune README |
| SR-ENT-02 | HKLM native messaging manifest | `FortivaEnterprise.iss` + `TryRegisterMachineNativeHost` | Registry inspect |
| SR-ENT-03 | Authenticode when Enterprise customer engages | `FORTIVA_REQUIRE_CODESIGN=1` opt-in | Signed build + secrets |
| SR-ENT-04 | Policy engine on mutations | `VaultSession.EnsureWritable`, `PolicyEnforcer` | AppHost tests |
| SR-ENT-05 | Audit log HMAC chain | `AuditIntegrity` | `AuditLogger` tests |

### SR-OPS — Release & operations

| ID | Requirement | Implementation | Verify |
|----|-------------|----------------|--------|
| SR-OPS-01 | CI: Core + AppHost + bridge security tests | `.github/workflows/ci.yml` | GitHub Actions |
| SR-OPS-02 | Update SHA-256 + HTTPS allowlist | `UpdateService` | `PreUpdateVaultBackupTests` |
| SR-OPS-03 | Pre-update vault backup | `PreUpdateVaultBackup` | Unit tests |
| SR-OPS-04 | Extension matrix automation | `Test-ExtensionFullMatrix.ps1` | Local / CI agent |
| SR-OPS-05 | Military audit gate | `Audit-MilitaryGrade.ps1` | Release checklist |

---

## 3. Certification tiers

| Tier | Criteria | Fortiva Personal today | Fortiva Enterprise (signed + Intune) |
|------|----------|------------------------|-------------------------------------|
| **A** | All SR-* pass + Authenticode + pen test sign-off | — | Target after cert + signing |
| **A−** | All SR-* pass + Authenticode on Enterprise deploy | — | When customer + signing provisioned |
| **B+** | Bridge validation + pipe sealing + extension matrix | **1.0.55** | Personal today (unsigned OK) |
| **B** | Sound crypto; bridge gaps | Pre-1.0.50 | — |
| **C** | Broken fill / open pipes | — | — |

**Personal 1.0.55 rating: B+** (unsigned installer is the main adoption blocker, not a crypto failure).

---

## 4. Accepted residual risks (documented, not bugs)

1. **Match-list usernames** — `list_credentials` summaries are plaintext for picker UI; release (`get_credentials`) seals username + password.
2. **HKCU native messaging (Personal)** — repaired on every app launch; user-writable until then.
3. **CLR `string` passwords** — cannot reliably zero; `SecureMemory` used where `byte[]` exists.
4. **Same-user malware with unlocked vault** — industry-wide PM limitation.
5. **Personal unsigned builds** — SmartScreen warning; signing deferred until Enterprise engagement (see `CODESIGNING.md`).

---

## 5. Release verification (mandatory)

```powershell
# Full military gate (~5–8 min)
.\scripts\Audit-MilitaryGrade.ps1

# After unlock — full fill path
.\scripts\Test-ExtensionRequireReady.ps1
```

**Manual gate (every release):**

1. `edge://extensions` → Reload Fortiva Autofill (version matches app).
2. Cold Fill on `https://login.ionos.co.uk` → unlock → fields filled.
3. Enterprise: verify HKLM native messaging + force-install registry.

---

## 6. Environment flags

| Variable | Effect |
|----------|--------|
| `FORTIVA_ALLOW_UNSIGNED_BRIDGE=1` | Dev/CI: skip Authenticode on bridge (default for Deploy script) |
| `FORTIVA_REQUIRE_CODESIGN=1` | Release: require signed EXEs when set with `ALLOW_UNSIGNED` unset (Enterprise go-live) |
| `FORTIVA_BRIDGE_DISABLE_PIPE_VALIDATION=1` | Test-only: disable PID guard |
| `FORTIVA_BRIDGE_FAST_TEST=1` | Test-only: shorten bridge timeouts |

---

## 7. References

- [THREAT-MODEL.md](THREAT-MODEL.md) — architecture boundaries  
- [DEEP-AUDIT.md](DEEP-AUDIT.md) — finding tracker  
- [SECURITY-REMEDIATION-2026.md](SECURITY-REMEDIATION-2026.md) — patch history  
- [CODESIGNING.md](CODESIGNING.md) — Authenticode activation  
- [packaging/intune/README.md](../packaging/intune/README.md) — Enterprise deployment  

---

*Re-run `Audit-MilitaryGrade.ps1` after any bridge, extension, or vault change.*
