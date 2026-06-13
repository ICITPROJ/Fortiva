# Security remediation — June 2026

Three-pass review: penetration test, development hardening, architecture threat model.

## Threat model (architecture)

Fortiva is an **offline-first, local-trust** password manager. The realistic adversary is **same-user malware** on Windows while the vault is unlocked—not remote network attackers.

| Boundary | Trust | Residual risk |
|----------|-------|----------------|
| Vault file (`vault.fva`) | Argon2id + AES-GCM | Offline cracking if password weak |
| Browser bridge pipes | Current-user ACL + client validation | Same-user pipe sniffing while unlocked |
| Native messaging | HKCU registry + extension ID | Registry hijack until next app launch |
| Windows Hello v4 | TPM/KeyCredential signature | User must complete Hello per unwrap |

**Design principle:** Fail closed on auth; minimize secret lifetime in pipes; never autofill without explicit user action in extension UI.

## Penetration test — findings patched in 1.0.29

| ID | Severity | Issue | Fix |
|----|----------|-------|-----|
| C-01 | Critical | Personal pipe guard accepted any process named `Fortiva.BrowserBridge.Host` | Path-under-install required; browser-parent fallback only when image path hidden |
| C-02 | Critical | Hello v4 wrap key = `SHA256(challenge)` without signature | `HKDF(signature, challenge)` — requires live Hello |
| H-01 | High | Unbounded backup JSON parse | 32 MB cap + `MaxDepth` |
| H-02 | High | Fill nonces without TTL / unbounded pending | 2 min TTL, one pending nonce per host |
| H-03 | High | Punycode (`xn--`) passed server validation | Rejected in `DomainSafety` |
| H-04 | High | IDN mismatch request vs vault entry | `EntryHostMatches` normalizes both sides |
| M-01 | Medium | Unbounded `ReadLineAsync` on pipe responses | `BridgeJson.ReadBoundedLineAsync` everywhere |
| M-02 | Medium | Bridge host launches unsigned EXE from disk | `IsAllowedExecutablePath` before `Process.Start` |
| M-03 | Medium | Native host runs from hijacked path | `NativeHostIntegrity.VerifyCurrentProcess` at startup |
| M-04 | Medium | Extension `onMessage` no sender check | `sender.id === chrome.runtime.id` |
| M-05 | Medium | Unlock pipe read without timeout | 10s read timeout |
| L-01 | Low | Unknown bridge commands returned empty body | `unknown_command` error |

## Development perspective

- **Shared `BridgePipeListener`** — single accept-loop implementation for all brokers
- **41+ bridge unit tests** + 233 Core tests; stress tests for pipe exhaustion
- **`scripts/Test-BrowserBridge.ps1`** — repeatable live validation (native host path)
- **`scripts/Deploy-FortivaPersonal.ps1`** — full publish + deploy + extension sync

## Remaining accepted risks (documented)

1. **Unsigned Personal updates** — SHA-256 manifest + HTTPS allowlist until code signing ships (`docs/CODESIGNING.md`)
2. **Plaintext credentials on local pipes** — same-user threat; mitigated by nonce + exact host match + audit log
3. **HKCU native messaging** — re-registered on app launch; Enterprise should use HKLM installer path
4. **Master passwords as `string` in CLR** — long-term refactor to pinned buffers

## Verification

```powershell
# Unit tests
dotnet test tests/Fortiva.Core.Tests

# Live bridge (unlock vault first for -RequireReady)
powershell -File scripts/Test-BrowserBridge.ps1 -Iterations 100 -RequireReady
```
