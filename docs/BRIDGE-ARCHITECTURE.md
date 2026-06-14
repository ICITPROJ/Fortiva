# Fortiva Browser Bridge Architecture (v1.0.37+)

Fortiva’s browser integration connects a Chromium extension, a **one-shot** native messaging host (`Fortiva.BrowserBridge.Host`), and the WinUI desktop app through **session-scoped named pipes** and a **single lifecycle coordinator**.

## Design principles

| Rule | Implementation |
|------|----------------|
| No long-lived port | Extension uses `chrome.runtime.sendNativeMessage` per operation |
| No push cache | No `STATE_CHANGED`, no snapshot merge, no `cachedSessionToken` |
| No session churn on focus | Watchdog / window-activate reconcile health only — no session rotate |
| Strict timeouts | Status command: **5 s**; host exits after each request |
| Single status command | `get_status_and_matches` replaces `ping`, `prepare_fill`, `list_credentials` for the extension |
| No browser unlock | Locked vault returns `vault_locked` immediately; user unlocks in Fortiva |

## End-state pipeline

```text
[Extension popup / background]
   └── sendNativeMessage({ command: "get_status_and_matches", payload: { domain, url } })
        └── Fortiva.BrowserBridge.Host.exe  (spawn → one request → one response → exit)
             └── Named pipes → Fortiva.Personal.exe (token broker + credential pipe)
                  └── Returns { status, matches, fillNonce }

[Fill click]
   └── sendNativeMessage({ command: "execute_fill", payload: { domain, url, entryId, fillNonce } })
        └── Same one-shot host path → get_credentials on credential pipe
             └── Extension injects username/password via content script
```

## Component overview

| Layer | Responsibility |
|-------|----------------|
| **Extension** (`background.js`, `popup.js`) | One-shot native calls only; no cached bridge state |
| **Native host** (`NativeMessagingHostPump`) | Read one stdin frame, dispatch, write one stdout frame, exit |
| **Forwarder** (`BridgeNativeForwarder.GetStatusAndMatchesAsync`) | 5 s budget; token from broker (retry once on stale); list matches |
| **WinUI** (`BridgeCoordinator`, `VaultSession`) | Pipes, session registry, hash sidecars; no push to extension |

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

## Enterprise

Enterprise endpoints use the same one-shot protocol. Intune deploys the host and HKLM native-messaging manifest; see `docs/CODESIGNING.md` and enterprise install docs.
