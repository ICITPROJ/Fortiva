# Fortiva Browser Bridge Architecture (v1.0.37+)

Fortiva’s browser integration connects a Chromium extension, a **one-shot** native messaging host (`Fortiva.BrowserBridge.Host`), and the WinUI desktop app. As of v1.0.37 the extension prefers **loopback HTTP** on `127.0.0.1:7847` (while Fortiva is running and unlocked) and falls back to native messaging when HTTP is unavailable.

| Audience | What to read |
|----------|--------------|
| **Users** | [User Manual §11 — Browser Extension](UserManual.md#11-browser-extension) |
| **Developers** | This document + [Developer guide — Browser Fill flow](DEVELOPER-GUIDE.md#runtime-flows) |
| **QA** | [Bridge validation checklist](BRIDGE-VALIDATION.md) |

### In one sentence (for everyone)

The extension asks **your local Fortiva app** for matching logins over `127.0.0.1` — never the internet — and only fills fields when **you** click Fill.

---

## Design principles

| Rule | Implementation |
|------|----------------|
| Prefer loopback HTTP when app is running | `BridgeLocalhostServer` on port **7847**; token via `POST /auth/session` |
| Native fallback | `chrome.runtime.sendNativeMessage` — one spawn per operation, host exits after response |
| No long-lived native port | Extension does not hold `connectNative` open |
| No push cache | No `STATE_CHANGED`, no snapshot merge, no `cachedSessionToken` |
| No session churn on focus | Watchdog reconciles health only — no session rotate on window activate |
| Strict timeouts | HTTP: **3 s** default; native status: **8 s**; execute fill: **30 s** |
| Single status path | `get_status_and_matches` (HTTP or native) replaces legacy ping/prepare/list split |
| No browser unlock | Locked vault returns `vault_locked` immediately; user unlocks in Fortiva |

## End-state pipeline

```text
[Extension popup / background]
   ├── (preferred) fetch http://127.0.0.1:7847/auth/session → bridge token
   │        └── GET /status-and-matches?domain=&url=  → { status, matches, fillNonce }
   │
   └── (fallback) sendNativeMessage({ command: "get_status_and_matches", ... })
        └── Fortiva.BrowserBridge.Host.exe  (spawn → one request → one response → exit)
             └── Named pipes → Fortiva.Personal.exe → VaultSession

[Fill click]
   ├── POST /execute-fill  (HTTP, token header)
   └── or sendNativeMessage({ command: "execute_fill", ... })
        └── Same paths → credential release → content script fills fields
```

## Component overview

| Layer | Responsibility |
|-------|----------------|
| **Extension** (`background.js`, `popup.js`) | HTTP-first status/fill; native fallback; in-memory `bridgeToken` only (no disk cache) |
| **Loopback server** (`BridgeLocalhostServer`) | In-process HTTP on `:7847`; token auth; public status when locked |
| **Native host** (`NativeMessagingHostPump`) | Read one stdin frame, dispatch, write one stdout frame, exit |
| **Forwarder** (`BridgeNativeForwarder.GetStatusAndMatchesAsync`) | Native path: token broker + list matches |
| **WinUI** (`BridgeCoordinator`, `VaultSession`) | Pipes, session registry, hash sidecars; starts localhost server when unlocked |

## Loopback HTTP API (v1.0.37+)

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `POST` | `/auth/session` | Extension `Origin` header | Issue `bridgeToken` when vault unlocked |
| `GET` | `/status-and-matches` | Optional token | Status + credential matches for domain/URL |
| `POST` | `/execute-fill` | `X-Fortiva-Bridge-Token` | Release credentials for fill (nonce required) |

Unauthenticated `GET /status-and-matches` returns vault locked/unlocked summary with empty matches (`authRequired: true` when unlocked).

## `get_status_and_matches` response

```json
{
  "status": {
    "app_running": true,
    "vault_unlocked": true,
    "error": null
  },
  "matches": [
    { "id": "...", "username": "...", "url": "...", "score": 100, "title": "...", "releasable": true }
  ],
  "fillNonce": "..."
}
```

### Error values (`status.error`)

| Value | Meaning |
|-------|---------|
| `null` | Success (matches may still be empty → no vault entry for URL) |
| `vault_locked` | Fortiva running, vault locked — unlock in app |
| `token_stale` | Broker token unavailable after one retry |
| `host_unreachable` | Fortiva not running or no active bridge session |
| `internal_error` | Pipe/timeout failure within 5 s budget |

## Host lifecycle

1. Chromium spawns `Fortiva.BrowserBridge.Host.exe` for each `sendNativeMessage`.
2. Host verifies integrity, checks active session registry, processes **one** JSON command.
3. Host writes length-prefixed JSON to stdout and **exits**.
4. No background threads survive the request; no event-pipe fan-out.

## BridgeCoordinator

`BridgeCoordinator.ReconcileLifecycleAsync()` remains the only writer for:

- Session GUID rotation (cold start / explicit restart only — **not** watchdog/focus)
- Native host cleanup after session rotate
- Hash sidecar repair

Push broadcaster (`BridgeEventBroadcaster`) is **not** used by the extension path in v1.0.37+.

## Migration from v1.0.36

1. Build and deploy Fortiva Personal + extension staging (`Connect browser` in Settings).
2. Reload extension in `edge://extensions`.
3. Kill any orphan `Fortiva.BrowserBridge.Host.exe` processes.
4. Run `scripts/Test-BrowserBridgeE2E.ps1 -RequireReady`.

## Acceptance test

With Fortiva running, vault unlocked, page URL `https://login.ionos.co.uk`:

- `get_status_and_matches` returns in **< 2 s**
- `matches.length >= 1`
- Extension popup shows **“1 match found”**
- No “Connecting…” longer than 1 s
- No orphan bridge host processes after popup closes
- No push events required

## Validation checklist

- [ ] `get_status_and_matches` responds < 2 s when unlocked
- [ ] Locked vault returns `vault_locked` in < 5 s (no unlock prompt from browser)
- [ ] App not running returns `host_unreachable`
- [ ] Popup shows match count without persistent `connectNative` port
- [ ] Fill click completes when vault unlocked and fields visible
- [ ] `Test-BrowserBridgeE2E.ps1 -RequireReady` passes
- [ ] No `Fortiva.BrowserBridge.Host.exe` left running after native calls

## Source files (developers)

| Area | Path |
|------|------|
| Extension HTTP client | `extension/background.js` — `BRIDGE_HTTP`, timeouts, token cache |
| Extension UI | `extension/popup.js`, `extension/content.js` |
| Loopback server | `src/Fortiva.Core/BrowserBridge/BridgeLocalhostServer.cs` |
| Port constant | `src/Fortiva.Core/BrowserBridge/BridgeLocalhostConstants.cs` |
| Native one-shot host | `src/Fortiva.BrowserBridge.Host/` → `NativeMessagingHostPump` |
| Pipe forwarder | `src/Fortiva.Core/BrowserBridge/BridgeNativeForwarder.cs` |
| App lifecycle | `src/Fortiva.AppHost/Services/BridgeCoordinator.cs` |
| Install / registry | `src/Fortiva.Core/BrowserBridge/BrowserBridgeInstallService.cs` |
| Settings UI | `src/Fortiva.AppHost/Pages/SettingsPage.xaml.cs` |

## Enterprise

Enterprise endpoints use the same one-shot protocol. Intune deploys the host and HKLM native-messaging manifest; see `docs/CODESIGNING.md` and enterprise install docs.
