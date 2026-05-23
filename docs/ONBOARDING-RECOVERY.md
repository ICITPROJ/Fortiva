# Onboarding and Recovery

## First run (Personal)

1. Explain zero-knowledge: Fortiva cannot reset your master password.
2. Create master password — use strength meter; prefer 16+ chars or passphrase.
3. Optional **Windows Hello** — unlocks key protector only, not vault without password setup.
4. Enable **Paranoia Mode** (recommended) for rollback protection.

## Everyday use

- Target unlock &lt; 500 ms on typical hardware (after KDF warm cache).
- Copy password only via explicit action; clipboard clears per settings.
- **Panic lock** hotkey: lock, wipe memory, hide window.

## Recovery

| Scenario | Action |
|----------|--------|
| Corrupt vault | Open snapshot 1–5 from same folder |
| Rollback detected | Confirm override or stay read-only (Paranoia) |
| Forgot master password | No recovery — by design |
| Portable USB | Copy `Fortiva` folder; expect host traces (prefetch, MRU) |

## Enterprise

- IT deploys MSIX via Intune; license/policy pre-staged under `%PROGRAMDATA%\Fortiva`.
- Audit logs for unlock failures and policy violations — export from Admin Console.
