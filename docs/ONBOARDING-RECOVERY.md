# Onboarding and Recovery

Short reference for **users**, **IT**, and **developers**. For the full walkthrough see [User Manual](UserManual.md).

---

## Users — first run (Personal)

1. **Master password** — Choose a strong password. Fortiva **cannot** reset or recover it. Write it down offline.
2. **Optional Windows Hello** — Faster unlock with fingerprint, face, or PIN. Does not replace the master password for backup or recovery.
3. **Paranoia Mode** — Recommended. Helps detect if an older copy of your vault was restored.
4. **Browser extension** — After setup: **Settings → Browser extension → Connect browser**.
5. **Backup** — **Import / Export → Export encrypted** and store the `.fva` file safely.

---

## Users — everyday habits

| Habit | Why |
|-------|-----|
| Set **Website** on each login entry | Browser Fill matches the page hostname |
| Use **Security audit** periodically | Finds weak, reused, or old passwords |
| Export an encrypted backup occasionally | Recovery if you forget master password is **impossible** — backup is your safety net |
| **Panic lock** (shield icon) when someone approaches | Locks vault and hides the window |

**Shortcuts** (vault must be unlocked): **Ctrl+K** search · **Ctrl+Shift+P** command palette · **Ctrl+N** quick add · **Ctrl+G** generate password

**After a Fortiva update:** if **Settings → Browser extension** shows extension version ≠ app version, click **Connect browser** and **Reload** the extension in your browser.

---

## Users — recovery

| Scenario | What to do |
|----------|------------|
| **Corrupt vault file** | Fortiva auto-restores from `vault.fva.snapshot1` … `snapshot5` in the same folder |
| **Rollback warning** | Yellow banner on unlock — acknowledge in Settings, or stay read-only (Paranoia Mode) |
| **Forgot master password** | **No recovery.** Restore from an encrypted `.fva` backup if you made one |
| **New PC** | Install Fortiva → unlock with master password (if `%AppData%\Fortiva\` was migrated), **or** import encrypted backup |
| **Portable USB vault** | Copy the `Fortiva` folder on the drive; Hello binding stays on the original PC |

---

## IT / Enterprise

| Topic | Detail |
|-------|--------|
| Deploy | Intune Win32 or EXE silent install — see `packaging/intune/README.md` |
| Vault location | `%ProgramData%\Fortiva\vault.fva` (default) |
| Policy / license | `%ProgramData%\Fortiva\` — staged by Admin Console or installer |
| Extension | HKLM force-install + native messaging manifest (installer) |
| Audit | Admin Console → audit log export; Security audit **Activity** category in Enterprise client |
| Updates | No public GitHub feed in Enterprise client — IT supplies builds |

---

## Developers — related code

| Flow | Entry point |
|------|-------------|
| Onboarding UI | `Fortiva.AppHost/Pages/OnboardingPage.xaml.cs` |
| Vault create | `ShellViewModel.CreateVaultAsync` → `VaultEngine.Create` |
| Snapshot restore | `VaultEngine` snapshot rotation — see [VAULT-FORMAT.md](VAULT-FORMAT.md) |
| Rollback detection | `DpapiLocalStateStore.CheckRollback` |
| Portable vault | `FortivaPaths.TryResolvePortableVaultDirectory` |
| Pre-update backup | `PreUpdateVaultBackup.TryCreate` |

Full map: [Developer guide](DEVELOPER-GUIDE.md).
