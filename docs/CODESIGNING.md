# Code signing (Authenticode)

Fortiva **Personal** currently ships **unsigned** installers from GitHub Releases. This is intentional to avoid the cost and setup of code signing while the product is in early release.

Updates and the browser bridge still use other protections:

- HTTPS-only update URLs with redirect validation
- SHA-256 hash verification of downloaded installers
- Bridge session tokens, fill nonces, and pipe ACLs

Authenticode signing is **not** required for Personal builds at this time (`AuthenticodePolicy.RequireSignedExecutables` is off).

## Activating signing (the pipeline is already wired)

The release workflow (`.github/workflows/release.yml`) now **always runs** the signing steps
(`Sign published executables`, `Sign installers`). They are **no-ops until you add the certificate
secrets** — `scripts/sign-release-artifacts.ps1` exits early when they are missing. To go live:

1. Obtain an **OV or EV** code-signing certificate (EV builds SmartScreen reputation fastest), or
   set up **Azure Trusted Signing**.
2. Export the `.pfx`, base64-encode it, and add repository secrets:
   - `CODESIGN_PFX_BASE64` — base64 of the `.pfx`
   - `CODESIGN_PFX_PASSWORD` — the `.pfx` password
   - `CODESIGN_TIMESTAMP_URL` *(optional)* — defaults to DigiCert's timestamp server
3. Push to `main`. The release job signs the published EXEs **before** they are bundled into the
   installers, and signs the installers **before** the update manifest hashes them, so the SHA-256
   in `latest.personal.json` matches the signed artifact.

> **Public-release note:** until a certificate is configured, every new user sees a Windows
> SmartScreen **"Unknown publisher"** prompt on first install. This is the single biggest adoption
> barrier for a password manager and should be resolved before a broad launch. Update integrity does
> not depend on signing (SHA-256 over HTTPS), so signing is a *trust/UX* requirement, not a
> functional one.

---

## Enterprise (planned)

**Enterprise** may enable signing later for IT-managed deployments (Intune, private feeds):

1. Configure Azure Artifact Signing or a traditional `.pfx` certificate.
2. Sign Enterprise/Admin installers in CI (see scripts below).
3. Set `FORTIVA_REQUIRE_CODESIGN=1` on Enterprise clients **or** wire `AuthenticodePolicy.ConfigureForEdition` to default Enterprise to `true` once signing is routine.

Personal is expected to stay unsigned or use optional signing only if SmartScreen reputation becomes a blocker.

---

## Re-enabling CI signing (when ready)

Scripts remain in the repo:

| Script | Purpose |
|--------|---------|
| `scripts/sign-release-artifacts.ps1` | Sign dist EXEs + installers with a `.pfx` (GitHub secrets `CODESIGN_PFX_BASE64`, `CODESIGN_PFX_PASSWORD`) |
| `scripts/verify-authenticode.ps1` | Fail CI if installers are unsigned |

Alternatively use **`azure/artifact-signing-action`** in `.github/workflows/release.yml` (~$9.99/mo Azure Artifact Signing Basic).

The **Sign published executables** and **Sign installers** steps are already present in `release.yml` and activate automatically once the `CODESIGN_PFX_*` secrets exist. (A **Verify Authenticode signatures** step using `scripts/verify-authenticode.ps1` can be added after signing if you want CI to fail when an artifact is unexpectedly unsigned.)

Then set `AuthenticodePolicy` so Personal requires signatures only if you want strict publisher checks in the app (today Personal never requires them unless you change `ConfigureForEdition`).

---

## User experience (unsigned Personal)

- Windows SmartScreen may show **Unknown publisher** on first install → **More info → Run anyway**.
- Auto-update and browser extension **work** without Authenticode (hash + bridge security still apply).
- Documented in `docs/UserManual.md`.

---

## Related code

| Location | Behavior |
|----------|----------|
| `src/Fortiva.Core/Platform/AuthenticodePolicy.cs` | Edition / env gate |
| `src/Fortiva.Core/Platform/AuthenticodeVerifier.cs` | Skips verify when policy is off |
| `src/Fortiva.AppHost/Services/UpdateService.cs` | Calls `IsSigned` before launching installer |
| `src/Fortiva.Core/BrowserBridge/BridgeClientValidator.cs` | Calls `IsSigned` for bridge host |
