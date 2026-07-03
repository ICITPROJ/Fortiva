# Fortiva World Release Audit

**Date:** 2026-07-03  
**Product version audited:** 1.0.57 (`Directory.Build.props`)  
**Scope:** Personal, Enterprise, and Admin editions — functionality, security, UI/UX, performance, distribution, documentation, legal  
**Method:** Full static review, 388 automated tests (Release), cross-check against threat model and prior pentest reports, subagent deep-dives across AppHost/Core/extension/installers  
**Audience:** Internal go/no-go decision and **shareable summary** for public release communications  

---

## Executive summary

Fortiva is a **technically credible, locally encrypted password manager** with a mature vault/crypto stack, working Personal app flows, browser fill, import/export, security auditing, and automated CI/release pipelines. It is **not yet a polished “consumer launch”** without addressing distribution trust (signing, winget, website sync), a few documentation inaccuracies, accessibility gaps, and clearly scoping features that are not shipped (passkeys, snapshot restore UI).

### Go / no-go matrix

| Audience | Verdict | Notes |
|----------|---------|-------|
| **Technical early adopters** (GitHub Releases, sideload extension) | **GO** | Document SmartScreen, extension setup, loopback security model |
| **General public / “share with the world”** | **CONDITIONAL** | Fix website + README funnel, signing strategy, winget, doc version sync |
| **Enterprise IT** | **GO with checklist** | Production license key, HKLM bridge policy, Intune docs |
| **Security researchers** | **GO** | Publish threat model + this audit; add `SECURITY.md` |

### Overall scorecard (1–5)

| Dimension | Score | Summary |
|-----------|-------|---------|
| **Core functionality** | 4.5 | Vault CRUD, import/export, generator, audit, bridge fill — end-to-end |
| **Security (crypto)** | 4.5 | Argon2id + AES-GCM + DPAPI; Hello v4 path; rollback detection |
| **Security (integration)** | 3.5 | Loopback HTTP bridge weaker than pipes; unsigned Personal builds |
| **UI / UX** | 3.5 | Strong design system; recent legibility fixes; a11y + nav shell gaps |
| **Performance** | 3.5 | Generator debounce added; theme recursion reduced; favicon prefetch on large vaults |
| **Test coverage** | 4.0 | 359 Core + 29 AppHost; strong crypto/import; limited UI E2E |
| **Release automation** | 4.5 | CI, installers, manifest, auto-release on `main` |
| **Public readiness** | 2.5 | Stale website/winget, unsigned installers, extension sideload-only |
| **Documentation** | 3.0 | Good manual; version pins stale at 1.0.37 in several files |

---

## 1. Functionality audit

### 1.1 Verified working (designed and implemented)

| Feature | Entry points | Core implementation | Status |
|---------|--------------|---------------------|--------|
| Vault create / unlock / lock | Onboarding, Unlock, Lock button | `VaultEngine`, `VaultSession`, `ShellViewModel` | ✅ |
| Windows Hello unlock | Unlock, Settings | `HelloCredentialStore`, `HelloVerificationGate` | ✅ |
| Entry CRUD + favorites + tags | Vault, Entry, Quick add | `VaultEngine`, `VaultPage` | ✅ |
| Secure notes + TOTP (policy) | Entry editor | `VaultEntry`, `TotpGenerator` | ✅ |
| Password generator | Nav, Vault dialog, Entry | `PasswordGeneratorPanel` | ✅ |
| Import CSV (multi-format) | Import/Export | `VaultImportExport`, `ImportMergeService` | ✅ |
| Encrypted backup | Import/Export | `VaultImportExport` | ✅ |
| Vault duplicate scan | Import/Export, Health | `VaultDuplicateAnalyzer` | ✅ |
| Security audit (Health) | Nav “Security audit” | `SecurityAuditRunner` | ✅ |
| Audit log (events) | Nav “Audit Log” | `AuditLogger` | ✅ |
| Browser extension fill | Popup → bridge | `BridgeLocalhostServer`, extension | ✅ |
| Personal auto-update | Settings | `UpdateService`, `UpdateChecker` | ✅ |
| Enterprise license + policy | License page, Admin | `LicenseVerifier`, `PolicyStore` | ✅ |
| Portable vault sync | Settings | `VaultSync` | ✅ |
| Command palette | Ctrl+Shift+P | `CommandPalette` | ✅ |
| Global vault search | Ctrl+K | `MainWindow` | ✅ |

**Automated tests:** 359 Core + 29 AppHost — **all passing in Release** (2026-07-03).

### 1.2 Functional gaps (not bugs — incomplete features)

| Gap | Severity | Detail |
|-----|----------|--------|
| **Passkeys** | High (if marketed) | `PasskeyStorage` is a stub; no WebAuthn UI. **Do not claim passkey support publicly.** |
| **Snapshot restore UI** | Medium | `UnlockFromSnapshot` exists in Core; Settings copy mentions snapshots but users cannot browse/restore. |
| **Admin shared-vault roles** | Low | `MemberRoles` defined but not assigned or enforced in Admin UI. |
| **ADMX generation** | Low | Documented in Intune README; not implemented in Admin Console. |
| **Seat management UI** | Low | `LicenseSeatRegistry` used in code; no admin UI for seats. |

### 1.3 Functional risks / inconsistencies

| Issue | Impact | Recommendation |
|-------|--------|----------------|
| Import preview match key ≠ duplicate analyzer keys | Users see “new” at import, “duplicate” later in vault scan | Align keys or explain in UI |
| “Security audit” vs “Audit Log” naming | Support confusion | Rename or add subtitles in nav |
| `NavAdmin` hidden in main shell | Dead handler | Remove or document Admin as separate EXE |
| Health activity audit API inconsistency | Maintenance only | Use `GetAuditLogger()` everywhere |

### 1.4 Manual smoke test checklist (Personal)

Use before any public announcement:

1. Onboarding → create vault → Hello → extension connect  
2. Add / edit / delete entry; copy password; favorite; tag  
3. Generator → Use password → new entry with categories  
4. Import CSV → preview conflicts → apply → view import history  
5. Duplicate scan → open entry → **exit back to vault**  
6. Security audit → drill weak passwords → export report  
7. Audit Log → scroll → export JSONL  
8. Settings → theme, auto-lock, check for updates  
9. Extension fill on test login page  
10. Lock → unlock → verify auto-lock  

---

## 2. Security audit

*Aligned with `docs/THREAT-MODEL.md` and `docs/SECURITY-PENTEST-REPORT.md`.*

### 2.1 Strengths

- **Vault at rest:** Argon2id + AES-256-GCM (CNG), MK/VK hierarchy, header MAC, integrity log  
- **Hello:** v3 DPAPI + verification gate; v4 KeyCredential/TPM upgrade path  
- **Rollback:** Revision counter + `local.state`; read-only until user confirms  
- **Bridge fill:** User-initiated popup only; exact host match; single-use nonce; homograph blocks  
- **Updates:** HTTPS allowlist, per-hop redirect validation, double SHA-256, lock-before-apply  
- **Audit integrity:** HMAC on read (tamper detection)  
- **Licensing:** RSA-SHA256; Release blocks dev license key without explicit env override  
- **Clipboard:** Policy-enforced clear; cleared on lock/panic  

### 2.2 Residual risks (document for public security page)

| ID | Severity | Finding | Public messaging |
|----|----------|---------|------------------|
| SEC-02 | High | Loopback HTTP (`127.0.0.1:7847`) — local malware while unlocked could request fills if vault unlocked | Same-user malware is out of scope; lock when away |
| SEC-05 | High | Personal builds unsigned by default | SmartScreen “More info → Run anyway”; plan code signing |
| SEC-06 | High | Memory scrape while unlocked | Industry-standard PM ceiling |
| SEC-08 | Medium | Audit logs detect tamper, not WORM | Enterprise should forward to SIEM |
| SEC-17 | Info | Passkeys not implemented | Do not market |

### 2.3 Pre-public security checklist

- [ ] `FORTIVA_ALLOW_DEV_LICENSE_KEY` unset on Enterprise release builds  
- [ ] `FORTIVA_ALLOW_UNSIGNED_BRIDGE` unset on consumer release builds  
- [ ] Production RSA public key embedded for Enterprise  
- [ ] Threat model linked from README / website  
- [ ] Add `SECURITY.md` with disclosure contact  
- [ ] Do not claim passkeys, WORM audit, or kernel-level protection  

---

## 3. UI / UX audit

### 3.1 Strengths

- Unified `FortivaTheme.xaml` (light frosted glass + dark curated palette)  
- Hero toolbars on main tabs; sticky generator actions  
- Recent fixes: Security audit legibility, Audit Log contrast, vault card layout, category chip wrap  
- Responsive Entry editor; master-detail vault at wide widths  

### 3.2 High-priority UX issues

| Issue | Pages | Fix |
|-------|-------|-----|
| Nav visible during Unlock/Onboarding | Shell | Hide `NavigationView` or use dedicated host |
| Health stat cards pointer-only | Health | Keyboard + automation names |
| Audit log raw enum labels | Audit | “Unlock succeeded” not `UnlockSuccess` |
| Theme resolver fragmentation | Code-built UI | Standardize on `ResolveAppTheme()` + refresh on OS theme change |
| Minimal accessibility | All | Icon button names, focus order, live regions |

### 3.3 Medium-priority polish

- Settings buttons missing `FortivaSecondaryButton` style  
- Health page dense grids without narrow-width breakpoints  
- Duplicate Regenerate/Copy on generator page  
- Tag vs category terminology mixed (“Add tag” vs “Categories”)  
- Title case drift (“Audit Log” vs “Security audit”)  

---

## 4. Performance audit

| Area | Finding | Status |
|------|---------|--------|
| Password generator | Regenerate on every slider tick / keystroke caused UI lag | **Fixed** — debounced (v1.0.57) |
| Theme application | `ApplyThemeRecursively` on full generator tree | **Improved** — removed from hot path |
| Category chips | Full rebuild on every theme apply | **Fixed** — refresh themes only |
| Vault favicons | Background prefetch for all visible entries | Acceptable; may spike on huge vaults |
| Security audit | Full report rebuild on theme change | Acceptable for typical vault sizes |
| Navigation | Tab switches use `animate: false` | Good |
| Auto-lock | Deferred when `IsBusy`; resets on input | Good |

**Recommendation:** Profile Security audit with 500+ entries; consider incremental finding list updates.

---

## 5. Distribution & public readiness

### 5.1 Ready today

| Item | Status |
|------|--------|
| GitHub Releases + `latest.personal.json` | ✅ Auto on `main` push |
| Inno Setup Personal installer | ✅ |
| CI: tests, CodeQL, installer QA, bridge e2e | ✅ |
| Extension stable ID (`llkpcnbhmhpenahlcdnbbfmkdfkgnpnj`) | ✅ |
| LICENSE (EULA) in installer | ✅ |
| `PRIVACY.md` (product privacy) | ✅ |

### 5.2 Not ready for broad public launch

| Item | Status | Action |
|------|--------|--------|
| Marketing website version | ❌ Shows **1.0.37** | Update Fortiva-Website repo |
| `docs/UserManual.md` version pins | ❌ Still **1.0.37** | Sync to current or “1.0.x” |
| `docs/README.md` version claim | ❌ Says 1.0.37 | Point to `Directory.Build.props` |
| Winget manifest | ❌ Stub 1.0.0 + placeholder hash | Update + submit to winget-pkgs |
| Code signing | ❌ Unsigned Personal | OV/EV cert or document SmartScreen |
| Chrome/Edge Web Store extension | ❌ Sideload only | Store listing or heavy onboarding docs |
| README download CTA | ❌ Missing | Link to GitHub Releases latest |
| `SECURITY.md` | ❌ Missing | Add disclosure policy |
| Website privacy vs `PRIVACY.md` | ⚠️ Divergent | Unify or label separately |
| Enterprise license path in manual §12.1 | ❌ Wrong path | Fix to `%PROGRAMDATA%\Fortiva\license.dat` |

---

## 6. Documentation accuracy

| Document | Accurate? | Issue |
|----------|-----------|-------|
| `THREAT-MODEL.md` | ✅ | Matches implementation |
| `SECURITY-PENTEST-REPORT.md` | ✅ | Remediations largely in code |
| `BRIDGE-ARCHITECTURE.md` | ✅ | HTTP-first model correct |
| `UserManual.md` | ⚠️ | Version 1.0.37; license path bug §12.1 |
| `docs/README.md` | ⚠️ | Version 1.0.37 claim |
| `MILITARY-GRADE-SPEC.md` | ⚠️ | Frozen at 1.0.37 — milestone doc |
| `PRIVACY.md` vs website | ⚠️ | Different canonical policies |

---

## 7. Edition matrix (what ships)

| Capability | Personal | Enterprise | Admin |
|------------|----------|------------|-------|
| Local vault | ✅ | ✅ (licensed) | — |
| Browser extension | Manual connect | Force-install (installer) | — |
| Auto-update | ✅ | IT (Intune) | — |
| Security audit | ✅ | ✅ | — |
| Policy enforcement | — | ✅ | Configure |
| Shared vault paths | — | ✅ (client) | ✅ |
| License management | — | Import file | ✅ Full console |

---

## 8. What you can say publicly (approved claims)

✅ **Safe to claim:**
- Zero-knowledge, local-first password manager for Windows  
- Argon2id + AES-256-GCM encryption; vault never leaves your PC  
- Windows Hello unlock support  
- Browser extension fill (user-initiated, no passive autofill)  
- Import from CSV / browser exports; encrypted backup  
- In-app security audit and password health analysis  
- No telemetry; optional HTTPS update check (Personal)  
- Open development on GitHub; reproducible builds  

❌ **Do not claim until implemented:**
- Passkey / WebAuthn support  
- Cloud sync  
- Mobile apps  
- Chrome Web Store / Edge Add-ons listing (unless published)  
- Code-signed installers (until signing enabled)  
- One-click winget install (until manifest published)  
- WORM / tamper-proof audit logs  
- Protection against kernel malware or same-user trojans while unlocked  

---

## 9. Prioritized remediation roadmap

### P0 — Before “share with the world” announcement

1. Sync **website + UserManual + docs/README** to current release (1.0.57+)  
2. Add **README download button** → GitHub Releases latest  
3. Add **`SECURITY.md`** (disclosure contact, supported versions)  
4. Fix **UserManual Enterprise license path**  
5. Publish **honest extension setup guide** (or pursue store listing)  
6. Decide **code signing** timeline and document SmartScreen until then  

### P1 — First month post-launch

7. Unify **privacy policy** (repo vs website)  
8. Publish **winget** package with real SHA256  
9. **Audit log human-readable labels**  
10. Hide nav during **Unlock / Onboarding**  
11. Align **import preview** and **duplicate scan** match keys  
12. **Accessibility pass** on icon buttons and Health cards  

### P2 — Quality / enterprise

13. Snapshot restore UI or revise Settings copy  
14. Remove passkey fields from marketing; implement or hide  
15. Admin: seats UI, shared vault roles, ADMX or doc fix  
16. OS theme change refresh for code-built pages  
17. `NOTICE` file for third-party licenses  

---

## 10. Test & CI summary

```
Release configuration (2026-07-03):
  Fortiva.Core.Tests     — 359 passed
  Fortiva.AppHost.Tests  —  29 passed
  Total                  — 388 passed
```

CI (`ci.yml`): Core tests, AppHost tests, extension manifest validation, installer QA, bridge security filters, CodeQL.  
Release (`release.yml`): Build → installers → `latest.personal.json` → GitHub Release (auto-bump patch on `main`).

---

## 11. Conclusion

Fortiva is **ready for a cautious public beta** via GitHub Releases for technical users who accept sideloaded browser extensions and unsigned installer friction. It is **not yet ready for a mainstream consumer launch** without website/winget/signing alignment and documentation sync.

The cryptographic and vault foundations are **strong enough to stand behind publicly** with an honest threat model. The main gap between “works” and “world-ready” is **distribution trust and polish**, not core password-manager functionality.

---

*This document may be shared publicly. For security details see `docs/THREAT-MODEL.md`. For pentest history see `docs/SECURITY-PENTEST-REPORT.md`.*
