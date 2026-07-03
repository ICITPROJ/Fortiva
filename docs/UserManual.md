# Fortiva — User Manual
### Published by icmclab studio

> **Who this is for:** Anyone using Fortiva Personal or Enterprise on Windows — no technical background required.  
> **Developers / IT:** See [docs/README.md](README.md) for architecture, threat model, and deployment guides.

---

## Quick start (Personal)

If you just installed Fortiva and want the essentials:

1. **Create your vault** — pick a strong master password and write it down offline. Fortiva cannot reset it.
2. **Add a login** — **My Vault** → **+ Add entry** → fill title, username, password, and **Website** (the login page URL).
3. **Connect the browser** — **Settings** → **Browser extension** → **Connect browser**. Reload the extension if Settings shows a version mismatch.
4. **Fill on a website** — open a login page → click the Fortiva icon → **Fill**.
5. **Back up** — **Import / Export** → **Export encrypted** → save the `.fva` file somewhere safe (cloud or USB).

Optional: enable **Windows Hello** in Settings for faster unlock, and run **Security audit** to review password health.

---

## Glossary (plain language)

| Term | Meaning |
|------|---------|
| **Vault** | Your encrypted password store on this PC (`vault.fva`). |
| **Master password** | The one password that unlocks the vault. Never stored by Fortiva. |
| **Windows Hello** | Fingerprint, face, or PIN unlock — optional shortcut; master password still needed for recovery. |
| **Fill** | You click Fill in the browser; Fortiva sends username/password to the page. Never automatic on page load. |
| **Paranoia Mode** | Extra protection if an old copy of your vault is restored — vault may open read-only until you confirm. |
| **Security audit** | In-app health check for weak/reused passwords and settings — no data sent online. |
| **Encrypted backup** | A `.fva` file you export with its own password — safe to store in cloud or email. |
| **Panic lock** | Shield icon (top-right) — locks vault and hides the window instantly. |

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
| **Fortiva Personal** | Individuals | `FortivaPersonal-{version}-Setup.exe` from [GitHub Releases](https://github.com/ICITPROJ/Fortiva/releases) |
| **Fortiva Enterprise** | Business users with IT-managed policies | `FortivaEnterprise-{version}-Setup.exe` |
| **Fortiva Admin Console** | IT administrators | `FortivaAdmin-{version}-Setup.exe` |

**Key capabilities:**

- **Security audit** — full-width dashboard with actionable deep links and JSON/HTML export
- **Windows Hello** — software-backed unlock by default; optional TPM upgrade in Settings
- **Browser extension** — fill logins from Chrome or Edge when you click **Fill** (see [§11](#11-browser-extension))
- **Import/export** — duplicate scanning, encrypted `.fva` backups (import also accepts `.fvab`), preview before import
- **Keyboard shortcuts** — **Ctrl+K** vault search, **Ctrl+Shift+P** command palette

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

1. Download and double-click the latest **`FortivaPersonal-*-Setup.exe`** (or the edition you need) from GitHub Releases.
2. **Personal edition** installs per-user (no admin required). **Enterprise and Admin** may show a UAC prompt — click **Yes** to install to Program Files.
3. The setup wizard opens. Click **Next**.
4. Choose an install folder and click **Next**:
   - **Personal (default):** `%LOCALAPPDATA%\Programs\icmclab studio\Fortiva Personal\`
   - **Enterprise / Admin:** `C:\Program Files\icmclab studio\...`
5. Choose a Start Menu folder (default: `Fortiva (icmclab studio)`) and click **Next**.
6. Optionally tick **"Create a desktop icon"** then click **Install**.
7. Wait for the progress bar to complete (~30 seconds). If WebView2 or the Visual C++ runtime is not already on your PC, setup installs them silently first.
8. Tick **"Launch Fortiva Personal"** and click **Finish**.

### 3.2 Silent / Scripted Installation

For IT deployment, both installers support silent installation flags:

```
FortivaPersonal-{version}-Setup.exe /SILENT
FortivaPersonal-{version}-Setup.exe /VERYSILENT /SUPPRESSMSGBOXES
```

`/SILENT` — shows a progress window but no wizard dialogs.  
`/VERYSILENT` — completely silent, no UI at all.

### 3.3 What Gets Installed

| What | Where |
|---|---|
| Application files (Personal) | `%LOCALAPPDATA%\Programs\icmclab studio\Fortiva Personal\` |
| Application files (Enterprise) | `C:\Program Files\icmclab studio\Fortiva Enterprise\` |
| Start Menu shortcuts | `%ProgramData%\Microsoft\Windows\Start Menu\Programs\Fortiva (icmclab studio)\` |
| Desktop shortcut | `%UserProfile%\Desktop\` (if selected) |
| Uninstaller (Personal) | `%LOCALAPPDATA%\Programs\icmclab studio\Fortiva Personal\unins000.exe` |
| Vault data (Personal, first run) | `%AppData%\Fortiva\vault.fva` |
| Vault data (Enterprise) | `%ProgramData%\Fortiva\vault.fva` |

> **Note:** Personal vault data lives in your user profile, not in the install folder. Uninstalling Fortiva Personal **permanently deletes** your vault, Windows Hello credential, settings, and crash logs.

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

- Click **Enable Windows Hello** to set it up. Windows prompts once for your chosen Hello method.
- Fortiva stores a **`hello.keyprotect`** file next to your vault (software-backed by default; TPM/KeyCredential upgrade offered later in Settings when available).
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
1. If Hello is configured, the unlock screen opens in **Hello-first** mode: Windows Hello may **prompt automatically**, and the password field is hidden until you choose **Use password instead**.
2. Or click the **Windows Hello** button when shown.
3. Authenticate with your fingerprint, face, or PIN when Windows prompts.

### Rollback Warning
If the vault detects that a previous snapshot has been restored (e.g. due to a system restore or file copy), a yellow warning banner appears. In Paranoia Mode the vault opens in read-only mode until you acknowledge the warning in Settings.

### Auto-lock
The vault automatically locks after a period of inactivity (configurable in Settings, default 5 minutes). Any mouse or keyboard interaction inside the app resets the timer.

### Panic Lock
The red **shield icon** in the top-right corner instantly locks the vault **and hides the application window**. Use this if someone approaches your screen unexpectedly. The app continues running in the background; click the taskbar icon to bring it back and unlock.

---

## 6. Managing Entries

### 6.1 Viewing Entries
The **My Vault** screen shows all your saved entries as a scrollable list or card grid (toggle in the toolbar). Each row shows the entry title, username, and a domain initial avatar.

### 6.2 Searching
When the vault is **unlocked**, use the **search box in the title bar** (or press **Ctrl+K** to focus it). Results filter live as you type, matching title, username, URL, and tags.

Press **Ctrl+Shift+P** to open the **command palette** — jump to Settings, Security audit, Import/Export, lock the vault, and other actions without using the sidebar.

Other shortcuts on the vault screen: **Ctrl+N** quick add, **Ctrl+G** generate password, **Ctrl+S** save (on the entry editor).

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
Open the entry and click the **eye icon** next to the password field. The password is visible for **5 seconds**, then automatically hidden again.

---

## 7. Security Audit

Navigate to **Security audit** in the left menu. The page uses the same full-width layout as Settings and Password generator (toolbar header + scrollable content). This runs a **full vault scan** covering passwords, app settings, vault hygiene, and (Enterprise) recent activity.

### 7.1 What it checks

| Area | What it checks |
|---|---|
| **Passwords** | Weak, reused, old (12+ months), missing passwords, similar URLs |
| **Settings** | Auto-lock timeout, clipboard auto-clear, Windows Hello, Paranoia Mode |
| **Vault hygiene** | HTTP URLs, missing site URLs, encrypted backup recommendation, duplicate imports |
| **Activity** (Enterprise) | Failed unlock attempts and policy violations (30-day window) |

### 7.2 Running an audit

1. Open **Security audit** — an audit runs automatically when you visit, or click **Run full audit**.
2. Review your **overall score** (0–100), category cards, and the **Audit findings** list.
3. Use action buttons on each finding — **View entries**, **Open settings**, **Open generator**, **View duplicates**, **Export backup**, etc. — to jump directly to the relevant screen or filtered list.

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

Navigate to **Import / Export** in the left menu. The page has a **toolbar header** at the top and scrollable content below (same layout pattern as Settings and Security audit).

Supported import formats:

| Format | How to export from source |
|---|---|
| Fortiva encrypted backup (`.fva`; import also accepts `.fvab` / `.json`) | **Export encrypted** on Import / Export |
| Generic CSV | Any CSV with columns: `title, username, password, url, notes` |
| KeePass CSV | File → Export → KeePass CSV (KDBX XML is not supported) |
| Browser CSV | Export via `chrome://password-manager/settings` → Export (Chrome / Edge) or Firefox Lockwise / `about:logins` |
| Apple Keychain CSV | Export from iPhone / iCloud Keychain as CSV |

Steps:
1. Choose the **source format** from the dropdown.
2. Click **Choose file and review import…** and select your export file.
3. Review the preview (new, duplicate, conflicting entries) and confirm. A summary shows how many entries were imported.

Imported entries are merged into the vault. **Duplicates** (same site + username + password) are skipped by default. **Conflicts** (same site + username, different password) are shown in the preview for you to resolve. Review skipped duplicates under **Import history** → **Review skipped duplicates from selected import**.

### 8.2 Duplicate management

Under **Import / Export → Duplicate management** (the vault is scanned automatically when you open this page):

- **Scan vault for duplicate logins** — refreshes the scan; finds **exact** duplicates (same site + username + password) and **similar** groups (same site + username with different passwords, or same domain with URL variations).
- Select a group and **Open selected entry** to review or consolidate manually.
- Fortiva never deletes entries automatically.

Security audit findings can also link to **View duplicates** when overlapping logins are detected.

### 8.3 Exporting

| Export type | Description |
|---|---|
| Encrypted backup (`.fva`) | Full vault re-encrypted with a separate backup password. Safe to store anywhere. Import accepts `.fva`, `.fvab`, or `.json`. |
| Plaintext CSV | **Warning:** passwords are visible in plain text. Only use for migration. |

> **Enterprise note:** Plaintext CSV export may be blocked by your organisation's policy. Contact your IT administrator.

---

## 9. Settings

Navigate to **Settings** in the left menu. Content appears in a glass panel with consistent 24px page margins (same as Import/Export and Password generator).

### Appearance
| Setting | Description |
|---|---|
| Theme | System, Light, or Dark |

### Security
| Setting | Description |
|---|---|
| Paranoia Mode | Toggle extra-strict clipboard/visibility restrictions |
| Auto-lock after inactivity | Drag slider from 30 seconds to 15 minutes (default 5 minutes) |
| Clipboard auto-clear | Drag slider from 5 to 120 seconds (default 30 seconds) |

### Updates (Personal)
| Setting | Description |
|---|---|
| Automatic updates | When on, Fortiva checks GitHub Releases (at most once per 24 hours), verifies SHA-256, and installs silently. Vault and Hello data are preserved. |
| Check for updates | Manual check and install from the latest GitHub Release |

Before auto-update, Fortiva backs up vault sidecars to `%LocalAppData%\FortivaPersonal\pre-update-backups\` (last 3 kept).

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

Windows Hello lets you unlock Fortiva without typing your master password each time.

### Setting Up Hello
1. Go to **Settings → Windows Hello → Set up Windows Hello**.
2. Windows prompts once for your fingerprint, face, or PIN (`UserConsentVerifier`).
3. Fortiva stores a **`hello.keyprotect`** file next to your vault (`%AppData%\Fortiva\` for Personal).
4. By default this uses **software-backed** protection (DPAPI + verification gate). If your PC supports TPM/KeyCredential, a **warning banner** may offer a one-time **hardware upgrade** — use **Set up Windows Hello** again while the banner is visible.

> **Note:** If TPM enrollment fails (some corporate PCs), software Hello still works for unlock. Fortiva keeps your existing binding and shows that hardware upgrade is unavailable.

### Using Hello
- On launch, Fortiva **auto-prompts Hello** when the vault is locked and Hello is configured (you can cancel and use your master password).
- Or click **Windows Hello** on the Unlock screen.

Settings shows status such as *Windows Hello is configured (software protection)* or *(hardware-backed TPM)* with the binding path (`hello.keyprotect` next to your vault; TPM upgrades may also use `hello.binding`).

### Removing Hello
Go to **Settings → Windows Hello → Remove Hello credential**. This removes `hello.keyprotect`. Your vault is unchanged — you will need your master password to unlock.

### Security model
Windows Hello does **not** replace your master password. It protects a key protector that wraps the vault key. Your master password is still required:
- When setting up a new device
- After removing and re-enrolling Hello
- As a fallback if biometrics fail
- For **Change master password** and encrypted export

Software-only Hello is easier for same-user malware on the PC to bypass than TPM-backed storage — use hardware upgrade when offered on personal machines.

---

## 11. Browser Extension

The Fortiva browser extension lets you **Fill** saved logins into website forms. It talks only to the Fortiva app on **your PC** — not to any cloud server.

**How it works (simple):**
1. Fortiva must be installed and the extension connected (**Settings → Connect browser**).
2. When you open the extension on a login page, it asks the local app (address `127.0.0.1:7847`) which entries match that site.
3. You click **Fill** — username and password are sent to the page. Fortiva never fills automatically when a page loads.

**Technical note (developers):** HTTP-first with native-messaging fallback — see [BRIDGE-ARCHITECTURE.md](BRIDGE-ARCHITECTURE.md).

### One-time setup (recommended)
1. Open **Fortiva → Settings → Browser extension**.
2. Click **Connect browser** (Fortiva copies the extension to a stable folder and registers the browser bridge).
3. If your browser is **closed**, Fortiva opens it with the extension loaded.
4. If your browser is **already open**, Fortiva offers to **close and reopen** — or set up manually.
5. **Manual fallback:** Developer mode → **Load unpacked** → select the folder Fortiva opened (path copied to clipboard).
6. **After Fortiva updates:** run **Connect browser**, then click **Reload** on the extension in `edge://extensions` or `chrome://extensions` (required after bridge security updates).

The extension folder is:
`%LOCALAPPDATA%\FortivaPersonal\extension` (Personal) or `%LOCALAPPDATA%\FortivaEnterprise\extension` (Enterprise).

Extension ID (stable): **`llkpcnbhmhpenahlcdnbbfmkdfkgnpnj`**

### Using the Extension
1. Navigate to a website login page (Fortiva does not need to be open first).
2. Click the Fortiva icon in the browser toolbar.
3. Click **Fill** (Fortiva never autofills on page load).
4. If the vault is locked, Fortiva opens and asks for Windows Hello or your master password.
5. Set each entry’s **Website** field in Fortiva (e.g. `https://login.example.com`) so Fill can match the page hostname.

### Troubleshooting
- **“Fortiva did not answer…”** — Unlock Fortiva, run **Connect browser**, reload the extension.
- **“Fortiva did not respond in time…”** — Fortiva may still be starting; try Fill again or click **Restart bridge** in Settings.
- **“No saved login”** — Edit the vault entry and set **Website** to the login page URL.
- **Wrong folder when loading unpacked** — Use the path shown in Settings; do not pick the repo root.
- **Stale extension after update** — Reload extension + **Connect browser** (version must match Fortiva — see `extension/manifest.json` vs About).

---

## 12. Enterprise Edition — Licensing & Policies

### 12.1 Installing a License
Fortiva Enterprise requires a valid signed license file.

1. Your IT administrator will provide a `license.dat` file.
2. Copy it to: `%ProgramData%\Fortiva\license.dat`
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
The **Audit Log** screen in the left menu shows a timestamped record of vault events: unlocks, locks, failed unlock attempts, configuration changes, and browser-bridge credential access.

| Edition | Log location |
|---|---|
| Personal | `%LocalAppData%\FortivaPersonal\audit\` |
| Enterprise | `%ProgramData%\Fortiva\audit\` |

Logs are HMAC-signed and can be exported to `.jsonl` for compliance or review. Personal audit logs are removed when Fortiva Personal is uninstalled.

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
2. Find **Fortiva Personal** (icmclab studio) — version shown in Settings → About.
3. Click **Uninstall**.

### What happens during uninstall

**Fortiva Personal**
1. Fortiva and the browser bridge are closed if running.
2. Application files in `%LOCALAPPDATA%\Programs\icmclab studio\Fortiva Personal\` are removed.
3. **All user data is permanently deleted:**
   - `%AppData%\Fortiva\` — vault, snapshots, Windows Hello, settings
   - `%AppData%\Fortiva\Personal\` — legacy path (if present)
   - `%LocalAppData%\FortivaPersonal\` — crash logs, appearance, extension staging
   - `%LocalAppData%\Fortiva\` — legacy bridge path (if present)
   - Downloaded update installers in `%TEMP%\FortivaPersonal-*-Setup.exe`
4. Start Menu / Desktop shortcuts and Add/Remove Programs entry are removed.
5. An informational dialog explains that your vault will be deleted before uninstall proceeds.

**Fortiva Enterprise**
1. Application files in Program Files are removed.
2. `%LocalAppData%\FortivaEnterprise\` (crash logs, Hello) is always removed.
3. You are prompted whether to delete the enterprise vault in `%ProgramData%\Fortiva\`.
4. If you delete the vault, you may also delete audit logs in `%ProgramData%\Fortiva\audit\`.
5. Policies and license files are kept unless removed separately via Admin Console uninstall.

**Fortiva Admin Console**
1. Application files in Program Files are removed.
2. `%LocalAppData%\FortivaAdmin\` is always removed.
3. You are prompted whether to delete admin config (`policies.json`, `license.dat`, `shared-vaults.json`).
4. If you delete admin config, you may also delete audit logs in `%ProgramData%\Fortiva\audit\`.
5. Enterprise vault files are never deleted by the Admin uninstaller.

---

## 15. Troubleshooting

### "Windows protected your PC" SmartScreen warning on first run
Click **More info** → **Run anyway**. This appears because the installer is not yet signed with an Extended Validation (EV) code-signing certificate. The software is safe — this is an icmclab studio product.

### The app won't launch — "Application failed to start"
Re-run the Fortiva installer — it bundles .NET 8 and the Windows App SDK, and **automatically installs Microsoft Edge WebView2 and the Visual C++ x64 runtime** if they are missing. Do not copy files out of the install folder manually; always use the setup EXE.

If the problem persists after reinstalling, export a **Security audit** HTML report (see [Section 7](#7-security-audit)) and check Settings for clipboard/auto-lock issues.

### "Invalid or expired license" (Enterprise)
- Confirm `license.dat` is in `%ProgramData%\Fortiva\license.dat`
- Check the expiry date with: `Fortiva.LicenseTool.exe verify license.dat`
- Contact your IT administrator for a renewed license.

### Forgot master password
There is no password reset or recovery. This is by design — Fortiva is zero-knowledge and no copy of your password exists anywhere. If you have a previous encrypted backup (`.fva`), restore it with the backup password.

### Vault won't open after a system restore
If Windows System Restore reverted your `%AppData%\Fortiva\` folder to an older snapshot, Fortiva detects a rollback and shows a warning. In Paranoia Mode the vault is read-only until acknowledged. To fully restore, go to **Settings** and acknowledge the rollback, or restore from an encrypted `.fva` backup.

### Clipboard doesn't clear
Check that **Clipboard auto-clear** is enabled and not set to 0 seconds in Settings. On some machines, clipboard monitoring by other software (e.g. clipboard managers) can interfere.

### Windows Hello button doesn't appear
Windows Hello must be configured in **Windows Settings → Accounts → Sign-in options** before Fortiva can use it. Set up at least one Hello method there first, then use **Set up Windows Hello** in Fortiva Settings.

If Hello was set up but the button is missing, check that `%AppData%\Fortiva\hello.keyprotect` exists. Re-run **Set up Windows Hello** from Settings if the file was lost.

### Windows Hello setup fails or asks multiple times
Fortiva should prompt **once** during setup. If setup fails with a TPM/hardware error, software Hello is still saved when possible. Check `%LocalAppData%\FortivaPersonal\fortiva-crash.log` for `HelloSetup:` lines. Use **Remove Hello credential** and set up again if the binding is orphaned.

### Security audit layout looks different from other pages
As of recent releases, Security audit uses the same toolbar + full-width scroll layout as Settings. Update Fortiva if you still see a narrow centered column with large side margins.

---

## 16. Security & Privacy Notes

> Full policy: [PRIVACY.md](../PRIVACY.md) · Technical boundaries: [THREAT-MODEL.md](THREAT-MODEL.md) (for engineers)

- **Zero-knowledge**: icmclab studio has no access to your vault data. No data ever leaves your machine except the optional update check (Personal).
- **No telemetry**: Fortiva contains no analytics, error reporting, or usage tracking code.
- **No background service**: Fortiva only runs when you open it. Nothing runs at startup or in the background.
- **Vault format**: AES-256-GCM (Windows CNG) for encryption at rest. Master key derived with Argon2id (memory-hard KDF). DPAPI protects local rollback state; Windows Hello uses `hello.keyprotect`; bridge session tokens exist in memory only while unlocked.
- **Memory safety**: Sensitive key material is zeroed from memory immediately after use using `CryptographicOperations.ZeroMemory`.
- **Integrity log**: Every vault mutation is recorded in a tamper-evident integrity log stored inside the encrypted vault.
- **Snapshots**: Up to 5 rolling backup snapshots are kept alongside the vault. If the vault file is corrupted, the most recent clean snapshot is automatically restored.
- **Security audit**: In-app scan of password hygiene, settings, and vault health. Export JSON or HTML (print to PDF) — never includes plaintext passwords.

---

*Fortiva — Published by icmclab studio — https://fortiva.studio.icmclab.cloud/*  
*Privacy: https://fortiva.studio.icmclab.cloud/privacy.html · Terms: https://fortiva.studio.icmclab.cloud/terms.html*  
*Support: contact@studio.icmclab.cloud*
