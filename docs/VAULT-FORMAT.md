# Fortiva Vault Format (`.fva`)

## File layout

```
[FORTIVA magic string]
[Header fields — see below]
[Encrypted entries blob — AES-256-GCM under VK]
[Encrypted integrity log blob — AES-256-GCM under VK]
```

## Header (MAC-protected)

| Field | Description |
|-------|-------------|
| `format_version` | Current version (1) |
| `min_supported_version` | Minimum reader version |
| KDF blob | Argon2id parameters (memory, iterations, parallelism) |
| `security_level` | Standard / Enhanced / Paranoia |
| `vault_id` | Random GUID |
| `created_at` / `last_modified_at` | UTC ticks |
| `revision_counter` | Monotonic anti-rollback |
| `security_level_counter` | Monotonic security level tracking |
| `salt` | Argon2 salt |
| `wrapped_vault_key` | MK → AES-GCM(VK) |
| `header_mac` | AES-GCM MAC over canonical header bytes |

## Key hierarchy

```
Master password → Argon2id → MK
MK → AES-256-GCM → VK
VK → entries + integrity log
```

## Snapshots

After each atomic save, rotate `vault.fva.snapshot1` … `vault.fva.snapshotN` (default N=5).

## Write protocol

1. Write `vault.fva.tmp`
2. Flush to disk
3. `File.Replace` / rename into `vault.fva`
4. Rotate snapshots

## Local state (DPAPI)

Separate `local.state` stores max `security_level`, last `vault_id`, last `modified_at`, last `revision_counter`.
