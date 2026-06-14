# Fortiva Enterprise — Intune / Endpoint Manager Deployment

## Distribution method

Fortiva Enterprise Client is distributed as a `.intunewin` package (Win32 app), which
wraps the EXE installer. This enables silent deployment via Microsoft Intune or SCCM.

## PowerShell provisioning scripts

For Intune **Proactive Remediations**, **Win32 app install commands**, or post-install verification:

| Script | Purpose |
|--------|---------|
| `Build-IntunePackage.ps1` | Stage installer + scripts and emit `FortivaEnterprise.intunewin` via `IntuneWinAppUtil.exe` |
| `Install-FortivaEnterprise.ps1` | Silent `/VERYSILENT` install + HKLM native messaging repair (requires elevation) |
| `Deploy-Intune.ps1` | Write `{app}\NativeMessaging\*.json` and register **HKLM** `NativeMessagingHosts` for Chrome and Edge |
| `Detect-FortivaEnterprise.ps1` | Custom detection: `Fortiva.Enterprise.exe` + HKLM native messaging for installed Chrome/Edge |

```powershell
# After Win32 app delivers the EXE, or on existing installs:
powershell -ExecutionPolicy Bypass -File .\packaging\intune\Deploy-Intune.ps1

# Full silent install from build output (elevated):
powershell -ExecutionPolicy Bypass -File .\packaging\intune\Install-FortivaEnterprise.ps1
```

`Deploy-Intune.ps1` writes:

```
HKLM\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.fortiva.browserbridge.enterprise
HKLM\SOFTWARE\Microsoft\Edge\NativeMessagingHosts\com.fortiva.browserbridge.enterprise
```

Default value = full path to `{InstallRoot}\NativeMessaging\com.fortiva.browserbridge.enterprise.json`

This is **machine-wide** (not HKCU), so all users on the endpoint receive the same native host binding without per-user Developer mode steps.

See also `docs/BRIDGE-ARCHITECTURE.md` for session-bound pipe rules.

## Build the Intune bundle

After `build-release.ps1` and `build-installers.ps1`:

```powershell
powershell -ExecutionPolicy Bypass -File .\packaging\intune\Build-IntunePackage.ps1
```

Output: `dist\intune\FortivaEnterprise.intunewin` (installer + provisioning scripts staged automatically).

Metadata for the Intune portal is in `intune-package.json` (install/uninstall commands, detection script name, remediation script).

## Steps

1. **Package** (automated — or manual `IntuneWinAppUtil.exe` if you prefer):

```powershell
.\packaging\intune\Build-IntunePackage.ps1
```

2. **Create Win32 App in Intune**:
   - App type: Windows app (Win32)
   - Package file: `FortivaEnterprise.intunewin`
   - Install command: `powershell.exe -ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File Install-FortivaEnterprise.ps1`
   - Uninstall command: `"%ProgramFiles%\icmclab studio\Fortiva Enterprise\unins000.exe" /VERYSILENT`
   - Detection rule: **Use custom detection script** → upload `Detect-FortivaEnterprise.ps1` (EXE + HKLM keys for **installed** Chrome/Edge only)

3. **Proactive Remediation (daily drift repair)**:

   Create an Intune Proactive Remediation with:
   - **Detection script:** `Detect-FortivaEnterprise.ps1` (non-compliant when exe or HKLM keys are missing)
   - **Remediation script:** `Deploy-Intune.ps1 -Remediation` (silent HKLM manifest repair; exit 0 = compliant)

   Schedule daily (or weekly) so manual uninstalls or corrupted native messaging manifests self-heal without admin intervention.

4. **Policy deployment**:

   Deploy `policies.json` (encrypted via DPAPI-LocalMachine) to `%PROGRAMDATA%\Fortiva\`
   using an Intune PowerShell script or Configuration Baseline. The Admin Console can
   generate the encrypted policy file for distribution.

5. **License deployment**:

   Deploy `license.dat` (DPAPI-LocalMachine-encrypted signed license) to `%PROGRAMDATA%\Fortiva\`
   using the same mechanism.

6. **Browser extension (automatic)**:

   The Enterprise installer registers Chrome and Edge **ExtensionInstallForcelist** policy and
   HKLM native messaging. Managed PCs install the Fortiva extension without Developer mode.

   | Item | Value |
   |------|-------|
   | Extension ID | `llkpcnbhmhpenahlcdnbbfmkdfkgnpnj` |
   | Update manifest | `https://github.com/ICITPROJ/Fortiva/releases/latest/download/fortiva-extension-updates.xml` |

   Registry (installer writes these; Intune can verify or override):

   ```
   HKLM\SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist
     "1" = llkpcnbhmhpenahlcdnbbfmkdfkgnpnj;https://github.com/ICITPROJ/Fortiva/releases/latest/download/fortiva-extension-updates.xml

   HKLM\SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist
     "1" = (same value)
   ```

   **CRX signing:** GitHub Releases must include `FortivaAutofill.crx` and
   `fortiva-extension-updates.xml` (built by `scripts/pack-extension-crx.ps1` when
   `EXTENSION_PRIVATE_KEY_PEM` is configured). See `packaging/extension-keys/README.md`.

   Users should restart Chrome/Edge after install. Fortiva does not need to stay open — on a login page, users click the Fortiva icon → **Fill**; Fortiva launches and unlocks when needed.

   **Code signing:** Not required for Personal. For Enterprise IT trust (SmartScreen), provision
   [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) or a traditional OV/EV
   certificate when a customer engages — see `docs/CODESIGNING.md`. The release pipeline is already
   wired; signing stays off until repository secrets are added.

## Group Policy (ADMX)

An ADMX template (`Fortiva.admx`) can be generated by the Admin Console for environments
that prefer Group Policy over Intune. Place it in `%SystemRoot%\PolicyDefinitions\`.

## Registry-based policy

Fortiva Enterprise reads policy values from:

```
HKLM\SOFTWARE\Fortiva\Enterprise\Policy
```

Values:
| Value Name                  | Type   | Description                                       |
|-----------------------------|--------|---------------------------------------------------|
| `MaxAutoLockSeconds`        | DWORD  | Maximum inactivity before auto-lock               |
| `ClipboardClearSeconds`     | DWORD  | Seconds before clipboard is cleared               |
| `ExportMode`                | DWORD  | 0=EncryptedOnly, 1=NoPlaintext, 2=PlaintextWarn   |
| `PortableModeAllowed`       | DWORD  | 0 = blocked, 1 = allowed                          |
| `MandatoryWindowsHello`     | DWORD  | 1 = require Windows Hello on every unlock          |
| `MinArgon2MemoryKb`         | DWORD  | Minimum KDF memory (default 65536 = 64 MB)        |

Registry values take precedence over the `policies.json` file.
