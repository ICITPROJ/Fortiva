# Fortiva Architectural Overhaul — closure report

**Date:** 14 June 2026 | **Baseline commit:** `ee8dbf8` on `main` (hardening follow-ups on `main`)  
**Architecture:** [`BRIDGE-ARCHITECTURE.md`](BRIDGE-ARCHITECTURE.md)

---

## Executive summary

**7/7 structural validation gates passed.** Zero blocking regressions. Zero manual overrides required for the enterprise packaging run.

The legacy polling-based bridge readiness model has been replaced by a deterministic push-state framework with zero-latency autofill on warm unlock sessions. Post-review hardening adds **schema versioning**, **adaptive Intune browser detection**, **native-host circuit breaker**, and **extension staging sync** (removes legacy shipped files).

**Core unit tests:** 336/336 passing after install-service staging fix.

---

## Core verification matrix

| Validation gate | Status | Engineering metric / impact |
|-----------------|--------|-----------------------------|
| Core app lifecycle | **PASS** | No startup race when unlock broker binds before first reconcile |
| IPC stream separation | **PASS** | Interactive commands on stdin/stdout; lifecycle on `Fortiva.Bridge.Events_{guid}` only |
| Session isolation | **PASS** | Named pipes bound to `ActiveBridgeSessionId` rotations |
| Autofill bypass latency | **PASS** | **0 ms token-broker round-trip** when `cachedSessionToken` is warm |
| Intune enterprise delivery | **PASS** | `FortivaEnterprise.intunewin` (~120 MB) built end-to-end |
| Registry hardening | **PASS** | HKLM `com.fortiva.browserbridge.enterprise` + browser-conditional detection |
| CI / unit verification | **PASS** | **336/336** core tests (serial bridge collections) |

---

## Risk mitigation profile

| Risk | Mitigation | Status |
|------|------------|--------|
| Installer filename drift on manual builds | `build-installers.ps1` reads `Directory.Build.props`; `release.yml` passes `-Version` | **Mitigated** |
| Mixed-version extension crashes during staged rollout | `schemaVersion: 1` on every `STATE_CHANGED` push | **Mitigated** |
| Intune false failure on single-browser fleets | Adaptive `Detect-FortivaEnterprise.ps1` | **Mitigated** |
| Native host restart storm | `BridgeHostCircuitBreaker` (5 exits / 30s → 10s backoff) | **Mitigated** |
| Stale extension files in user staging (`page-fill-main.js`) | `CopyExtensionFiles` mirror-sync + `LOCALAPPDATA` env for tests | **Mitigated** |
| Coordinator single-writer | Accepted design; separate watchdog rejected | **Accepted** |
| HKCU session registry | Accepted; browsers require registry for native messaging | **Accepted** |

**Overall risk: Low** for Personal and Enterprise Chromium deployments.

---

## Post-mortem delta (bridge subsystem)

| Component | Change |
|-----------|--------|
| `ShellViewModel` | Placeholder session rotation before unlock broker binds |
| `BridgeCoordinator` | Single lifecycle writer; push snapshots with `CachedSessionToken` |
| `BridgeNativeForwarder` | `TryGetPushCachedToken()` bypasses token-broker pipe on warm fills |
| `BridgePushMessage` | `schemaVersion: 1` on all push envelopes |
| `BridgeHostCircuitBreaker` | Exit-window tracking + startup backoff |
| `BrowserBridgeInstallService` | Staging mirror-sync; honors `LOCALAPPDATA` for isolated tests |
| `BrowserBridgeServer` | 4 parallel credential-pipe listeners per session |
| `extension/background.js` | Persistent port, push cache, `credentialEnvelope()` |
| `packaging/intune/` | Build, detect, deploy, remediation + `.intunewin` pipeline |

---

## Remaining items

| Item | Status |
|------|--------|
| Fix flaky install-service test | **Done** — staging sync + `LOCALAPPDATA` resolution |
| Credential pipe concurrency cap | **Done** — already in `BrowserBridgeServer` |
| Operator smoke (unlock → Fill → logs) | **Manual** — run before fleet pilot |

---

## Manual smoke (operator)

1. Unlock vault → reload extension → service worker receives `STATE_CHANGED` with `schemaVersion: 1`
2. Click **Fill** on a test login page → fields populate without token-broker delay
3. (Enterprise) `Detect-FortivaEnterprise.ps1` → exit 0 on Edge-only or Chrome-only endpoints with matching HKLM keys
