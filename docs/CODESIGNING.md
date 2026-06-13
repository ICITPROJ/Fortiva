# Code signing (Authenticode)

## Current stance (Personal + pre-Enterprise)

Fortiva **Personal** ships **unsigned** installers from GitHub Releases. This is intentional for now.

**You do not need to set up signing** unless an Enterprise customer asks for IT-managed deployment with SmartScreen/trust requirements.

Updates and the browser bridge still use other protections:

- HTTPS-only update URLs with redirect validation
- SHA-256 hash verification of downloaded installers
- Bridge session tokens, fill nonces, pipe ACLs, and password sealing on credential pipes

Windows may show **Unknown publisher** on first install → **More info → Run anyway**. Update integrity does **not** depend on Authenticode.

---

## When Enterprise customers engage

Signing is **deferred** until then. Two common options:

| Option | Notes |
|--------|--------|
| **Azure Trusted Signing** | Microsoft’s cloud code-signing service (sometimes called *Artifact Signing* in Azure docs). ~$10/mo Basic tier; no hardware token; GitHub Actions via `azure/artifact-signing-action`. **Not provisioned today** — document for when Enterprise asks. |
| **Traditional OV/EV certificate** | `.pfx` exported to GitHub secrets (`CODESIGN_PFX_BASE64`, `CODESIGN_PFX_PASSWORD`). EV builds SmartScreen reputation fastest. **What the repo wires today** (`scripts/sign-release-artifacts.ps1` + `signtool`). |

The release pipeline signing steps in `.github/workflows/release.yml` are **no-ops** until secrets exist — the script exits early when `CODESIGN_PFX_*` is missing. Switching to Azure Trusted Signing later is a pipeline swap, not an app change.

### Activation checklist (Enterprise go-live only)

1. Provision Azure Trusted Signing **or** purchase OV/EV cert.
2. Add repository secrets (see `scripts/sign-release-artifacts.ps1` header).
3. Set `FORTIVA_REQUIRE_CODESIGN=1` on signed Enterprise builds (or in CI release env).
4. Re-test bridge host + installers on a managed PC.

Personal can stay unsigned or adopt signing later if SmartScreen becomes a broad adoption blocker.

**Do not** set `FORTIVA_REQUIRE_CODESIGN=1` in release CI until signing secrets are provisioned — otherwise `UpdateService` will reject unsigned installers.

---

## Development / local deploy

| Variable | Purpose |
|----------|---------|
| `FORTIVA_ALLOW_UNSIGNED_BRIDGE=1` | Default for `Deploy-FortivaPersonal.ps1`, tests, CI — skips Authenticode checks |
| `FORTIVA_REQUIRE_CODESIGN=1` | Opt-in: app and bridge require signed executables |

`AuthenticodePolicy.ConfigureForEdition` only enforces signing when **both** Release build **and** `FORTIVA_REQUIRE_CODESIGN=1` (and not `FORTIVA_ALLOW_UNSIGNED_BRIDGE`).

---

## Related code

| Location | Behavior |
|----------|----------|
| `src/Fortiva.Core/Platform/AuthenticodePolicy.cs` | Opt-in gate |
| `src/Fortiva.Core/Platform/AuthenticodeVerifier.cs` | Skips verify when policy is off |
| `src/Fortiva.AppHost/Services/UpdateService.cs` | `IsSigned` only when `RequireSignedExecutables` is on |
| `src/Fortiva.Core/BrowserBridge/BridgeClientValidator.cs` | `IsSigned` for bridge host when policy on |
| `src/Fortiva.Core/BrowserBridge/NativeHostIntegrity.cs` | Integrity + optional Authenticode |

See also [MILITARY-GRADE-SPEC.md](MILITARY-GRADE-SPEC.md) and [packaging/intune/README.md](../packaging/intune/README.md).
