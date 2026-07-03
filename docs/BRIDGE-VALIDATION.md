# Fortiva Browser Bridge — validation notes

> **Current architecture (v1.0.57+):** See [`BRIDGE-ARCHITECTURE.md`](BRIDGE-ARCHITECTURE.md).  
> Session tokens are issued via native `get_session_token` only — not `POST /auth/session`.  
> Historical notes below describe the pre-1.0.37 push-cache model.

---

## Historical closure report (pre-1.0.37 push model)

**Date:** 14 June 2026 | **Baseline commit:** `ee8dbf8` on `main`

The legacy polling/push bridge model (persistent `connectNative`, `STATE_CHANGED`, `cachedSessionToken`) was **replaced in v1.0.37** with loopback HTTP + one-shot native fallback. Do not use the metrics below for current releases.

| Legacy gate | Status (historical) |
|-------------|---------------------|
| Push-state framework | Superseded — removed from extension path |
| `cachedSessionToken` bypass | Superseded — HTTP token or native broker per request |
| Core unit tests | Baseline 336/336 at time of report |

---

## Current validation checklist (v1.0.37+)

Use [`BRIDGE-ARCHITECTURE.md`](BRIDGE-ARCHITECTURE.md) acceptance criteria and:

```powershell
./scripts/Test-BrowserBridgeE2E.ps1 -RequireReady
./scripts/test-browser-extension.ps1
```

Manual smoke:

1. Unlock Fortiva → open extension on a login page with a matching vault entry.
2. Popup shows match count within ~2 s (HTTP path).
3. Click **Fill** → fields populate; no orphan `Fortiva.BrowserBridge.Host.exe` after popup closes.
4. Lock vault → extension shows locked state without hanging.
5. After Fortiva update: **Settings → Connect browser** → reload extension in `edge://extensions`.

---

## Enterprise

Enterprise uses the same HTTP + one-shot native protocol. Intune deploys the host and HKLM native-messaging manifest; see `packaging/intune/README.md` and [`CODESIGNING.md`](CODESIGNING.md).
