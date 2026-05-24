# Fortiva Threat Model

## Scope

| Surface | Personal | Enterprise |
|---------|----------|------------|
| Vault file (`vault.fva`) | `%APPDATA%\Fortiva` | Policy-defined + shared paths |
| Local metadata | DPAPI (user) | DPAPI (machine) |
| Policy / license | N/A | `%PROGRAMDATA%\Fortiva` |
| Browser bridge | Named pipe + native host | Same, policy-gated clipboard |
| Bridge session token | In-memory + secured token pipe while unlocked | Same |
| Network | Optional update check (HTTPS, user-initiated or startup) | None by design |

## Assets

- **Master Key (MK)** — derived via Argon2id; never written to disk.
- **Vault Key (VK)** — AES-256-GCM wrapped by MK; stored in vault header.
- **Entries & integrity log** — encrypted under VK.
- **License & policy blobs** — DPAPI-protected at rest; license signature verified with CNG (RSA).

## Trust boundaries

1. **User workstation** — trusted for Personal; partially hostile for Enterprise (malware, insider).
2. **IT admin** — can set policies and licenses; cannot decrypt vault without master password (zero-knowledge).
3. **Browser extension** — untrusted UI; receives only requested credentials from unlocked Fortiva via local IPC.

## Mitigations

- **Rollback / downgrade**: monotonic counters + DPAPI local state; suspicious rollback forces read-only until user confirms.
- **Corruption**: header MAC, sample decrypt checks, encrypted snapshots (`vault.fva.snapshot1..N`).
- **Memory**: `CryptographicOperations.ZeroMemory` / `RtlSecureZeroMemory`; panic lock wipes session keys.
- **Clipboard**: explicit copy, auto-clear, policy disable.
- **Export**: encrypted default; plaintext requires explicit confirmation (Personal) or blocked (Enterprise policy).
- **Enterprise seats**: `MaxSeats` enforced via `%PROGRAMDATA%\Fortiva\seats.dat` on unlock.
- **Shared vaults**: Admin configures paths; Enterprise client selects active vault in Settings.
- **Security audit export**: JSON/HTML reports contain findings and counts only — no secrets.

## Out of scope (explicit non-goals)

- Cloud sync, telemetry, custom updaters, background daemons.
- Protection against kernel-level malware or live memory scraping of an unlocked vault.

## Enterprise-specific

- Audit logs record events without secrets (unlock, policy violation, restore).
- Policy engine prevents weakening below IT baseline.
- Shared vault paths (SMB/OneDrive) inherit filesystem ACLs — document customer hardening.
