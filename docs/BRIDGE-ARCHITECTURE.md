# Fortiva Browser Bridge Architecture (v1.0.57+)

Fortiva’s browser integration connects a Chromium extension, a **one-shot** native messaging host (`Fortiva.BrowserBridge.Host`), and the WinUI desktop app. The extension prefers **loopback HTTP** on `127.0.0.1:7847` (while Fortiva is running and unlocked) and falls back to native messaging when HTTP is unavailable or token handoff fails.

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
| Prefer loopback HTTP when app is running | `BridgeLocalhostServer` on port **7847**; **same session token** as validated named pipes |
| Token handoff via native host only | `get_session_token` native command → pipe broker (`BridgeTokenBroker`); HTTP **never mints** tokens |
| Native fallback | `chrome.runtime.sendNativeMessage` — one spawn per operation, host exits after response |
| No long-lived native port | Extension does not hold `connectNative` open |
| No push cache | No `STATE_CHANGED`, no snapshot merge, no disk token cache |
| No session churn on focus | Watchdog reconciles health only — no session rotate on window activate |
| Strict timeouts | HTTP: **3 s** default; native token: **10 s**; native status: **8 s**; execute fill: **30 s** |
| Single status path | `get_status_and_matches` (HTTP or native) replaces legacy ping/prepare/list split |
| No browser unlock | Locked vault returns `vault_locked` immediately; user unlocks in Fortiva |
| No unlock-state leak | Unauthenticated HTTP never returns `vaultUnlocked: true` |

## End-state pipeline

```text
[Extension popup / background]
   ├── sendNativeMessage({ command: "get_session_token" })
   │        └── Native host → validated pipe → BridgeTokenBroker → { bridgeToken }
   │
   ├── (preferred) GET /status-and-matches?domain=&url=  (X-Fortiva-Bridge-Token)
   │        ← { status, matches, fillNonce }
   │
   └── (fallback) sendNativeMessage({ command: "get_status_and_matches", ... })
        └── Fortiva.BrowserBridge.Host.exe  (spawn → one request → one response → exit)
             └── Named pipes → Fortiva.Personal.exe → VaultSession

[Fill click]
   ├── POST /execute-fill  (HTTP, token header)
   └── or sendNativeMessage({ command: "execute_fill", ... })
        └── Same paths → credential release → content script fills fields
```

If HTTP returns `auth_required` (token missing/stale), the extension falls back to native `get_status_and_matches`.

## Component overview

| Layer | Responsibility |
|-------|----------------|
| **Extension** (`background.js`, `popup.js`) | Native token fetch → HTTP status/fill; native fallback; in-memory `bridgeToken` only |
| **Loopback server** (`BridgeLocalhostServer`) | In-process HTTP on `:7847`; validates session token header; no public unlock leak |
| **Native host** (`NativeMessagingHostPump`) | Read one stdin frame, dispatch, write one stdout frame, exit |
| **Forwarder** (`BridgeNativeForwarder`) | Native path: token broker + list matches + `get_session_token` |
| **WinUI** (`BridgeCoordinator`, `VaultSession`) | Pipes, session registry; localhost server shares pipe session token |

## Loopback HTTP API

| Method | Path | Auth | Purpose |
|--------|------|------|---------|
| `POST` | `/auth/session` | Extension `Origin` header | **Deprecated** — returns status + `authRequired` only; does **not** issue tokens |
| `GET` | `/status-and-matches` | Optional token | Public: locked summary only. Authed: status + matches + fillNonce |
| `POST` | `/execute-fill` | `X-Fortiva-Bridge-Token` | Release credentials for fill (nonce required) |

## Native commands

| Command | Purpose |
|---------|---------|
| `get_session_token` | Returns `{ bridgeToken, status }` — only path that exposes the HTTP bridge token |
| `get_status_and_matches` | Full status + matches via pipes (fallback) |
| `execute_fill` | Credential release via pipes (fallback) |

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
| `auth_required` | HTTP only — caller needs token via `get_session_token` |
| `token_stale` | Broker token unavailable after retry |
| `host_unreachable` | Fortiva not running or no active bridge session |
| `internal_error` | Pipe/timeout failure |

## Host lifecycle

1. Chromium spawns `Fortiva.BrowserBridge.Host.exe` for each `sendNativeMessage`.
2. Host verifies integrity, checks active session registry, processes **one** JSON command.
3. Host writes length-prefixed JSON to stdout and **exits**.
4. No background threads survive the request; no event-pipe fan-out.

## After Fortiva or extension updates

1. **Reload the extension** in `edge://extensions` or `chrome://extensions` (required after bridge security updates).
2. Run **Settings → Connect browser** if version mismatch is shown.
3. Unlock Fortiva and retry Fill.

## Migration from pre-1.0.57 token-via-HTTP

Older builds issued `bridgeToken` from `POST /auth/session`. Current builds require `get_session_token` via native host. Users on old extension builds must reload the extension staged by Fortiva.

## Acceptance test

With Fortiva running, vault unlocked, page URL `https://login.example.com`:

- `get_status_and_matches` returns in **< 2 s**
- `matches.length >= 1` when a matching entry exists
- Extension popup shows match count
- No orphan bridge host processes after popup closes
- `scripts/Test-BrowserBridgeE2E.ps1 -RequireReady` passes

## Validation checklist

- [ ] `get_session_token` returns token when unlocked (native host)
- [ ] `POST /auth/session` does **not** return `bridgeToken`
- [ ] Unauthenticated `GET /status-and-matches` never shows `vaultUnlocked: true`
- [ ] Locked vault returns `vault_locked` in < 5 s
- [ ] Fill click completes when vault unlocked and fields visible
- [ ] Extension reloaded after update

## Source files (developers)

| Area | Path |
|------|------|
| Extension HTTP client | `extension/background.js` — `ensureBridgeToken()`, native fallback |
| Extension UI | `extension/popup.js`, `extension/content.js` |
| Loopback server | `src/Fortiva.Core/BrowserBridge/BridgeLocalhostServer.cs` |
| Session token response | `src/Fortiva.Core/BrowserBridge/BridgeSessionTokenResponse` |
| Native one-shot host | `src/Fortiva.BrowserBridge.Host/` → `NativeMessagingHostPump` |
| Pipe forwarder | `src/Fortiva.Core/BrowserBridge/BridgeNativeForwarder.cs` |
| Token broker | `src/Fortiva.Core/BrowserBridge/BridgeTokenBroker.cs` |
| App lifecycle | `src/Fortiva.AppHost/Services/BridgeCoordinator.cs` |

## Enterprise

Enterprise endpoints use the same one-shot protocol. Intune deploys the host and HKLM native-messaging manifest; see `docs/CODESIGNING.md` and enterprise install docs.
