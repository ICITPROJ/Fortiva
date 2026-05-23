# Policy and Licensing

## License (`%PROGRAMDATA%\Fortiva\license.dat`)

- JSON (or protobuf) document: edition, expiry, company, feature flags, seats.
- Signed with RSA/ECDSA (CNG verification).
- Entire file DPAPI-protected (machine scope).

## Policies (`%PROGRAMDATA%\Fortiva\policies.json`)

DPAPI-protected JSON defining:

| Setting | Purpose |
|---------|---------|
| Min Argon2 memory/iterations/parallelism | KDF floor |
| Max auto-lock seconds | Upper bound on unlock duration |
| Clipboard mode / clear timeout | Exfiltration control |
| Export mode | Encrypted-only / no plaintext |
| Portable mode | Allowed / forbidden |
| Mandatory Paranoia | Read-only on downgrade |
| Mandatory Windows Hello | Biometric gate for key protector |

Enterprise Client loads on startup and via **Reload policies**. Users cannot weaken below policy.

## Admin Console

- Apply license files
- Edit/validate policies → write `policies.json`
- Configure shared vaults (`shared-vaults.json`)
- View/export audit logs for SIEM

## Shared vaults

Definitions include storage path (SMB, OneDrive folder, etc.) and role map (`admin` / `user`). Access control is enforced by filesystem ACLs plus Fortiva role metadata.
