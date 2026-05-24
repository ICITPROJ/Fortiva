# Fortiva — User Manual
### Published by icmclab studio · Version 1.0.0

---

## Table of Contents

1. [Overview](#1-overview)
2. [System Requirements](#2-system-requirements)
3. [Installation](#3-installation)
4. [First Launch — Onboarding Wizard](#4-first-launch--onboarding-wizard)
5. [Unlocking Your Vault](#5-unlocking-your-vault)
6. [Managing Entries](#6-managing-entries)
7. [Security Audit](#7-security-audit)
8. [Import & Export](#8-import--export)
9. [Settings](#9-settings)
10. [Windows Hello Biometric Unlock](#10-windows-hello-biometric-unlock)
11. [Browser Extension](#11-browser-extension)
12. [Enterprise Edition — Licensing & Policies](#12-enterprise-edition--licensing--policies)
13. [Fortiva Admin Console](#13-fortiva-admin-console)
14. [Uninstallation](#14-uninstallation)
15. [Troubleshooting](#15-troubleshooting)
16. [Security & Privacy Notes](#16-security--privacy-notes)

---

## 1. Overview

Fortiva is a **zero-knowledge, local-first password manager for Windows**, published by **icmclab studio**. Every password you save is encrypted on your own device using:

- **AES-256-GCM** (via Windows CNG) for symmetric encryption
- **Argon2id** for master-password key derivation (memory-hard, resistant to GPU cracking)
- **DPAPI** to protect the vault key at rest using your Windows login

Fortiva never transmits your passwords to any server. There is no cloud sync, no telemetry, no background service running when the app is closed.

**Three editions are available:**

| Edition | Who it's for | Installer |
|---|---|---|
| **Fortiva Personal** | Individuals | `FortivaPersonal-1.0.0-Setup.exe` |
| **Fortiva Enterprise** | Business users with IT-managed policies | `FortivaEnterprise-1.0.0-Setup.exe` |
| **Fortiva Admin Console** | IT administrators | `FortivaAdmin-1.0.0-Setup.exe` |

---

## 2. System Requirements

| Requirement | Minimum |
|---|---|
| Operating system | Windows 10 version 2004 (build 19041) or later |
| Architecture | x64 |
| RAM | 256 MB free |
| Disk space | 250 MB |
| .NET runtime | **Not required** — bundled inside the installer |
| Windows App SDK | **Not required** — bundled inside the installer |
| Microsoft Edge WebView2 | **Installed automatically** by setup if missing |
| Visual C++ 2015–2022 (x64) | **Installed automatically** by setup if missing |
| Internet connection | **Not required** for daily use (needed once during install if prerequisites must be downloaded — they are embedded in the Fortiva installer) |

---

## 3. Installation

### 3.1 Standard Installation (Recommended)

1. Double-click **`FortivaPersonal-1.0.0-Setup.exe`** (or the edition you need).
2. If Windows shows a User Account Control (UAC) prompt saying *"Do you want to allow this app to make changes?"* — click **Yes**. This is needed to write to `Program Files`.
3. The setup wizard opens. Click **Next**.
4. Choose an install folder (the default `C:\Program Files\icmclab studio\Fortiva Personal` is recommended) and click **Next**.
5. Choose a Start Menu folder (default: `Fortiva (icmclab studio)`) and click **Next**.
6. Optionally tick **"Create a desktop icon"** then click **Install**.
7. Wait for the progress bar to complete (~30 seconds). If WebView2 or the Visual C++ runtime is not already on your PC, setup installs them silently first.
8. Tick **"Launch Fortiva Personal"** and click **Finish**.

### 3.2 Silent / Scripted Installation

For IT deployment, both installers support silent installation flags:

```
FortivaPersonal-1.0.0-Setup.exe /SILENT
FortivaPersonal-1.0.0-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES
```

`/SILENT` — shows a progress window but no wizard dialogs.  
`/VERYSILENT` — completely silent, no UI at all.

### 3.3 What Gets Installed

| What | Where |
|---|---|
| Application files | `C:\Program Files\icmclab studio\Fortiva Personal\` |
| Start Menu shortcuts | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Fortiva (icmclab studio)\` |
| Desktop shortcut | `%UserProfile%\Desktop\` (if selected) |
| Uninstaller | `C:\Program Files\icmclab studio\Fortiva Personal\unins000.exe` |
| Vault data (created on first run) | `%AppData%\Fortiva\Personal\` |

> **Note:** Vault data is stored in your user profile, not in Program Files. It is **not** deleted by the uninstaller unless you explicitly choose to remove it.

---

## 4. First Launch — Onboarding Wizard

The first time Fortiva launches (no existing vault found), a four-step wizard guides you through setup with a glass-style layout and progress indicators at the bottom.

### Step 1 — Welcome
An introduction to Fortiva's zero-knowledge security model: local encryption, no cloud sync, and Argon2id key derivation. Click **Get started** to continue.

### Step 2 — Create Your Master Password

This is the single password that protects everything. It is never stored anywhere — only a cryptographic key derived from it is used to unlock the vault.

- Type a strong master password in the first box.
- A prominent **Write it down offline** warning reminds you that Fortiva cannot reset or recover your master password.
- The **live strength meter** shows your password's entropy and strength:
  - **Very Weak / Weak** — shown in red. Fortiva will not proceed until you improve it.
  - **Moderate** — shown in amber. Acceptable but improvable.
  - **Strong / Very Strong** — shown in green. Recommended.
- Confirm the password in the second box.
- Click **Continue** when both boxes match and the password is at least Moderate strength.

**Tips for a strong master password:**
- Use a passphrase of 4+ random words (e.g. "correct-horse-battery-staple")
- Aim for at least 50 bits of entropy (the meter shows this)
- Do not reuse a password you use anywhere else
- **Record it on paper** and store it somewhere physically secure — not in email, cloud notes, or another password manager

### Step 3 — Windows Hello (Optional)

Windows Hello lets you unlock Fortiva using your fingerprint, face, or PIN instead of typing your master password every time.

- Click **Enable Windows Hello** to set it up. Windows will prompt you for your chosen Hello method.
- Or click **Skip for now** to use your master password only. You can enable Hello later in Settings.

### Step 4 — Final Security Setup

Before your vault is created, confirm you understand the recovery model:

- **Paranoia Mode** (recommended) protects against silent vault rollbacks.
- You must check **I have recorded my master password offline in a secure place** before **Create vault** becomes available.
- Click **Create vault** when ready.

While the vault is being created, a busy overlay appears with progress feedback. Key derivation (Argon2id) runs in the background so the UI stays responsive — this may take a few seconds.

Your vault is created, unlocked automatically, and the app navigates to the main vault view.

---

## 5. Unlocking Your Vault

On subsequent launches, the **Unlock** screen appears.

### With Master Password
1. Type your master password in the password box.
2. Click **Unlock** (or press Enter).

### With Windows Hello
1. Click the **Windows Hello** button (only shown if Hello is configured).
2. Authenticate with your fingerprint, face, or PIN when Windows prompts.

### Rollback Warning
If the vault detects that a previous snapshot has been restored (e.g. due to a system restore or file copy), a yellow warning banner appears. In Paranoia Mode the vault opens in read-only mode until you acknowledge the warning in Settings.

### Auto-lock
The vault automatically locks after a period of inactivity (configurable in Settings, default 5 minutes). Any mouse or keyboard interaction inside the app resets the timer.

### Panic Lock
The red **shield icon** in the top-right corner instantly locks the vault **and hides the application window**. Use this if someone approaches your screen unexpectedly. The app continues running in the background; click the taskbar icon to bring it back and unlock.

---

## 6. Managing Entries

### 6.1 Viewing Entries
The **My Vault** screen shows all your saved entries as a scrollable list. Each row shows the entry title, username, and a domain initial avatar.

### 6.2 Searching
Type in the search box at the top of the vault screen. Results filter live as you type, matching against title, username, URL, and tags.

### 6.3 Adding a New Entry
1. Click **+ Add entry** (top-right of the vault screen).
2. Fill in the fields:
   - **Title** — a name for the entry (e.g. "Gmail", "Work VPN")
   - **Username / Email**
   - **Password** — type it or click the dice icon to generate one
   - **URL** — the website address
   - **Tags** — optional comma-separated labels for organisation
   - **Notes** — freeform text, stored encrypted
3. Click **Save**.

### 6.4 Password Generator
Open **Password generator** from the left navigation (below **My Vault**), or use the toolbar button on the vault screen when adding entries.

| Option | Description |
|---|---|
| Length | Drag slider (8–128 characters) |
| Uppercase | A–Z |
| Lowercase | a–z |
| Digits | 0–9 |
| Symbols | `!@#$%^&*` etc. |
| Passphrase | Generates word-based passphrases |
| PIN / numeric | Numeric-only passwords |

On the entry editor, click **Generate** next to the password field to fill the password in directly.

### 6.5 Editing an Entry
Click any entry in the list to open it in the entry editor. Make changes and click **Save**.

### 6.6 Deleting an Entry
Open the entry editor and click **Delete**. A confirmation dialog appears — click **Delete permanently** to confirm. Deleted entries are removed from the vault and recorded in the integrity log.

### 6.7 Copying Passwords
- Click the **copy icon** on any list row to copy the password to the clipboard immediately.
- The clipboard is automatically cleared after the configured timeout (default 30 seconds).
- A toast notification counts down the remaining time.

### 6.8 Revealing a Password
Open the entry and click the **eye icon** next to the password field. The password is visible for **10 seconds** (5 seconds in Paranoia Mode) then automatically hidden again.

---

## 7. Security Audit

Navigate to **Security audit** in the left menu. This runs a **full vault scan** covering passwords, app settings, vault hygiene, and (Enterprise) recent activity.

### 7.1 What it checks

| Area | What it checks |
|---|---|
| **Passwords** | Weak, reused, old (12+ months), and missing passwords |
| **Settings** | Auto-lock timeout, clipboard auto-clear, Windows Hello, Paranoia Mode |
| **Vault hygiene** | HTTP URLs, missing site URLs, encrypted snapshot availability |
| **Activity** (Enterprise) | Failed unlock attempts and policy violations (30-day window) |

### 7.2 Running an audit

1. Click **Run full audit** (or open the page — it runs automatically).
2. Review your **overall score** (0–100), category issue counts, and the findings list.
3. Click any finding’s action button (**Open generator**, **Open settings**, **Export backup**, etc.) or expand password lists to jump to affected entries.

Severity badges:

| Badge | Meaning |
|---|---|
| **CRITICAL** | Fix immediately (e.g. clipboard disabled, many failed unlocks) |
| **WARNING** | Should fix soon (weak/reused passwords, slow auto-lock) |
| **INFO** | Recommended improvement |
| **PASS** | Check passed |

### 7.3 Export audit report

After an audit completes, click **Export report**:

| Format | Use |
|---|---|
| **JSON** (`.json`) | Machine-readable summary for IT/SIEM scripts or record-keeping |
| **HTML** (`.html`) | Human-readable report — open in a browser and use **Print → Save as PDF** |

Exported reports include scores, findings, and password **counts only**. **No vault passwords or secrets** are ever written to the export file.

---

## 8. Import & Export

### 8.1 Importing

Navigate to **Import / Export** in the left menu. Supported import formats:

| Format | How to export from source |
|---|---|
| Fortiva encrypted backup (`.fvab`) | Exported from another Fortiva installation |
| Generic CSV | Any CSV with columns: `title, username, password, url, notes` |
| Chrome / Edge CSV | Export via `chrome://password-manager/settings` → Export |
| Firefox CSV | Export via Firefox Lockwise / `about:logins` → Export |
| KeePass XML (`.kdbx` exported XML) | File → Export → KeePass XML in KeePass |

Steps:
1. Click **Browse…** next to the format you want.
2. Select your export file.
3. Click **Import**. A summary shows how many entries were imported.

> Imported entries are merged into the vault — existing entries are not overwritten.

### 8.2 Exporting

| Export type | Description |
|---|---|
| Encrypted backup (`.fvab`) | Full vault re-encrypted with a backup password. Safe to store anywhere. |
| Plaintext CSV | **Warning:** passwords are visible in plain text. Only use for migration. |

> **Enterprise note:** Plaintext CSV export may be blocked by your organisation's policy. Contact your IT administrator.

---

## 9. Settings

Navigate to **Settings** in the left menu.

### Security
| Setting | Description |
|---|---|
| Paranoia Mode | Toggle extra-strict clipboard/visibility restrictions |
| Auto-lock after inactivity | Drag slider from 30 seconds to 15 minutes |
| Clipboard auto-clear | Drag slider from 5 to 120 seconds |

### Windows Hello
See [Section 10](#10-windows-hello-biometric-unlock).

### Change Master Password
1. Enter your current master password.
2. Enter and confirm the new password (strength meter shown live).
3. Click **Change password**.

The vault is re-keyed immediately. The old master password is no longer valid.

### Portable Mode
Click **Open or create vault on USB…** in Settings to use a vault on removable media.

| Action | What to select |
|---|---|
| **Open existing vault** | USB drive root (with `Fortiva\vault.fva`), a `Fortiva` folder, or any folder containing `vault.fva` |
| **Create new vault** | USB drive root or an empty folder — Fortiva creates `Fortiva\vault.fva` on drive roots, or uses the folder you pick |

The vault is used directly from the USB — no copy is made to your local profile. Fortiva **remembers your USB location** and reopens it automatically when the drive is connected at startup.

If the USB drive is unplugged, Fortiva falls back to your local vault for that session and shows a status message. Plug the drive back in and restart Fortiva to resume the portable vault.

> Windows Hello and browser-bridge tokens remain on the local PC (they are tied to Windows credentials). Re-enable Hello after using a portable vault on a new machine.
>
> Windows may still leave traces (prefetch cache, MRU lists) on the host PC.

### About
Displays the Fortiva edition, version number, and publisher (icmclab studio).

---

## 10. Windows Hello Biometric Unlock

Windows Hello lets you unlock Fortiva without typing your master password.

### Setting Up Hello
1. Go to **Settings → Windows Hello → Set up Windows Hello**.
2. Windows prompts you to authenticate using your chosen Hello method (fingerprint, face, or PIN).
3. A protector key is stored securely using the Windows Hello credential store, tied to your Windows account.

### Using Hello
On the Unlock screen, click **Windows Hello** instead of typing your password.

### Removing Hello
Go to **Settings → Windows Hello → Remove Hello credential**. This removes the stored protector key. Your vault is unchanged — you will just need to use your master password to unlock.

### Security model
Windows Hello does **not** replace your master password. It protects a key protector that wraps the vault key. Your master password is still required:
- When setting up a new device
- After removing and re-enrolling Hello
- As a fallback if biometrics fail

---

## 11. Browser Extension

The Fortiva browser extension connects to the desktop app via a local named pipe (no internet involved).

### Installing the Extension
1. Open Edge or Chrome and go to `chrome://extensions` (or `edge://extensions`).
2. Enable **Developer mode** (top-right toggle).
3. Click **Load unpacked** and select the `extension/` folder from the Fortiva installation directory.
4. Note the extension's ID (shown under the extension name).

### Registering the Native Messaging Host
Run the provided PowerShell script from the `dist/` folder:
```powershell
.\Launch-FortivaPersonal.ps1 -RegisterBrowserBridge
```
When prompted, replace `REPLACE_WITH_YOUR_EXT_ID` in the JSON file with your actual extension ID.

### Using the Extension
1. Navigate to a website login page.
2. Click the Fortiva icon in the browser toolbar.
3. The extension requests credentials for the current domain from the desktop app.
4. If the vault is locked, the desktop app comes to the foreground for you to unlock.
5. Click a credential to auto-fill the login form.

---

## 12. Enterprise Edition — Licensing & Policies

### 12.1 Installing a License
Fortiva Enterprise requires a valid signed license file.

1. Your IT administrator will provide a `license.dat` file.
2. Copy it to: `%AppData%\Fortiva\Enterprise\license.dat`
3. Launch Fortiva Enterprise. The license is verified automatically on startup.

If no valid license is found, the app displays a message and exits.

### 12.2 Policy Enforcement
Your IT administrator can configure policies that restrict certain features. When a policy is active, the affected setting shows **(set by policy)** and cannot be changed by the user. Policies may include:

- Minimum master password strength
- Maximum auto-lock timeout
- Clipboard restrictions
- Forced Windows Hello requirement
- Paranoia Mode enforcement
- Blocked plaintext export

### 12.3 Audit Log
The **Audit Log** screen (footer of the left menu, Enterprise only) shows a timestamped record of all vault events: unlocks, entry changes, failed unlock attempts, policy violations, and exports. Logs can be exported to a `.jsonl` file for compliance purposes.

---

## 13. Fortiva Admin Console

The Admin Console is a separate application for IT administrators.

### 13.1 License Management
- **Import License**: Load a `license.dat` file to verify it.
- **Verify License**: Check the validity, company name, and expiry of any license file.
- **Generate Trial**: Create a time-limited trial license (requires the private key).

### 13.2 Generating Licenses (LicenseTool CLI)
```cmd
cd "C:\Program Files\icmclab studio\Fortiva Admin Console\"

rem Generate an RSA key pair (first time only — keep private.xml secure!)
Fortiva.LicenseTool.exe generate-key private.xml public.xml

rem Sign a 365-day license for "Acme Corp"
Fortiva.LicenseTool.exe sign "Acme Corp" 365 private.xml

rem Verify the produced license
Fortiva.LicenseTool.exe verify license.dat
```

Distribute `license.dat` to users. Store `private.xml` securely — it must never be shared.

### 13.3 Policy Management
1. Open the **Policy** tab.
2. Use sliders and toggles to configure the desired policy values.
3. Click **Validate** to check for logical errors.
4. Click **Save Policy** to write the DPAPI-protected policy file to `%ProgramData%\Fortiva\policies.json`.
5. Enterprise clients pick up the updated policy on their next vault unlock.

### 13.4 Shared Vaults
1. Open the **Shared Vaults** tab.
2. Click **Add shared vault**, enter a display name and a UNC path (e.g. `\\server\share\team.fva`).
3. Click **Add**. The vault path is recorded in the shared vault configuration.

### 13.5 Audit Reporting
Open the **Audit** tab to view recent audit events. Click **Export log** to save a `.jsonl` file for your SIEM or compliance system.

---

## 14. Uninstallation

### Method 1 — Start Menu
1. Open the **Start Menu**.
2. Find the **Fortiva (icmclab studio)** folder.
3. Click **Uninstall Fortiva Personal** (or the edition you installed).

### Method 2 — Windows Settings
1. Open **Settings → Apps → Installed apps**.
2. Search for "Fortiva".
3. Click the three-dot menu → **Uninstall**.

### Method 3 — Control Panel
1. Open **Control Panel → Programs → Programs and Features**.
2. Find **Fortiva Personal 1.0.0 (icmclab studio)**.
3. Click **Uninstall**.

### What happens during uninstall
1. The app is closed if it is running.
2. All application files in `Program Files` are removed.
3. Start Menu and Desktop shortcuts are removed.
4. The entry is removed from Add/Remove Programs.
5. A dialog asks: **"Do you also want to permanently delete your vault and all saved passwords?"**
   - Click **No** (default) — your vault data in `%AppData%\Fortiva\` is kept. You can reinstall later and pick up where you left off.
   - Click **Yes** — your vault and all passwords are permanently deleted. **This cannot be undone.**

---

## 15. Troubleshooting

### "Windows protected your PC" SmartScreen warning on first run
Click **More info** → **Run anyway**. This appears because the installer is not yet signed with an Extended Validation (EV) code-signing certificate. The software is safe — this is an icmclab studio product.

### The app won't launch — "Application failed to start"
Re-run the Fortiva installer — it bundles .NET 8 and the Windows App SDK, and **automatically installs Microsoft Edge WebView2 and the Visual C++ x64 runtime** if they are missing. Do not copy files out of the install folder manually; always use the setup EXE.

If the problem persists after reinstalling, export a **Security audit** HTML report (see [Section 7](#7-security-audit)) and check Settings for clipboard/auto-lock issues.

### "Invalid or expired license" (Enterprise)
- Confirm `license.dat` is in `%AppData%\Fortiva\Enterprise\`
- Check the expiry date with: `Fortiva.LicenseTool.exe verify license.dat`
- Contact your IT administrator for a renewed license.

### Forgot master password
There is no password reset or recovery. This is by design — Fortiva is zero-knowledge and no copy of your password exists anywhere. If you have a previous encrypted backup (`.fvab`), restore it with the backup password.

### Vault won't open after a system restore
If Windows System Restore reverted your `%AppData%\Fortiva\` folder to an older snapshot, Fortiva detects a rollback and shows a warning. In Paranoia Mode the vault is read-only until acknowledged. To fully restore, go to **Settings** and acknowledge the rollback, or restore from a `.fvab` backup.

### Clipboard doesn't clear
Check that **Clipboard auto-clear** is enabled and not set to 0 seconds in Settings. On some machines, clipboard monitoring by other software (e.g. clipboard managers) can interfere.

### Windows Hello button doesn't appear
Windows Hello must be configured in **Windows Settings → Accounts → Sign-in options** before Fortiva can use it. Set up at least one Hello method there first.

---

## 16. Security & Privacy Notes

- **Zero-knowledge**: icmclab studio has no access to your vault data. No data ever leaves your machine.
- **No telemetry**: Fortiva contains no analytics, error reporting, or usage tracking code.
- **No background service**: Fortiva only runs when you open it. Nothing runs at startup or in the background.
- **Vault format**: AES-256-GCM encrypted with a per-vault key. Key wrapped with Argon2id-derived master key. All cryptography uses Windows CNG — the same primitives trusted by Windows itself.
- **Memory safety**: Sensitive key material is zeroed from memory immediately after use using `CryptographicOperations.ZeroMemory`.
- **Integrity log**: Every vault mutation is recorded in a tamper-evident integrity log stored inside the encrypted vault.
- **Snapshots**: Up to 5 rolling backup snapshots are kept alongside the vault. If the vault file is corrupted, the most recent clean snapshot is automatically restored.
- **Security audit**: In-app scan of password hygiene, settings, and vault health. Export JSON or HTML (print to PDF) — never includes plaintext passwords.

---

*Fortiva v1.0.0 — Published by icmclab studio — https://studio.icmclab.cloud*  
*For support, visit https://studio.icmclab.cloud/support*
