# Security Policy

## Supported versions

| Edition | Supported | Update channel |
|---------|-----------|----------------|
| Fortiva Personal | Latest [GitHub Release](https://github.com/ICITPROJ/Fortiva/releases/latest) | In-app **Check for updates** or reinstall |
| Fortiva Enterprise | Latest release + valid license | IT-managed (Intune / installer) |
| Fortiva Admin | Latest release | IT workstations only |

Security fixes are delivered via new releases. We do not backport to older versions unless explicitly stated in release notes.

## Reporting a vulnerability

**Please do not open public GitHub issues for security vulnerabilities.**

Report privately using one of:

1. **[GitHub Security Advisories](https://github.com/ICITPROJ/Fortiva/security/advisories/new)** (preferred)
2. Contact via [fortiva.studio.icmclab.cloud](https://fortiva.studio.icmclab.cloud/) (see site footer / contact)

Include:

- Affected component (Personal, Enterprise, browser extension, bridge, installer)
- Steps to reproduce
- Impact assessment (local vs remote, data at rest vs in memory)
- Fortiva version (`Settings → About` or `Directory.Build.props`)

We aim to acknowledge reports within **5 business days** and provide a fix timeline when confirmed.

## Scope

**In scope**

- Vault cryptography, Hello unlock, DPAPI storage
- Browser bridge (loopback HTTP, native messaging, named pipes)
- Import/export, backup, update pipeline
- Enterprise licensing and policy enforcement
- Installer and extension staging

**Out of scope (documented non-goals)**

- Same-user malware reading memory while the vault is unlocked
- Kernel-mode or rootkit attackers
- Physical access with full disk decryption outside Fortiva’s threat model
- Social engineering of the master password

See [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) for full trust boundaries.

## Security documentation

| Document | Purpose |
|----------|---------|
| [`docs/THREAT-MODEL.md`](docs/THREAT-MODEL.md) | Trust boundaries and mitigations |
| [`docs/SECURITY-PENTEST-REPORT.md`](docs/SECURITY-PENTEST-REPORT.md) | Adversarial review history |
| [`docs/WORLD-RELEASE-AUDIT.md`](docs/WORLD-RELEASE-AUDIT.md) | Public launch readiness audit |
| [`docs/BRIDGE-ARCHITECTURE.md`](docs/BRIDGE-ARCHITECTURE.md) | Browser bridge security model |

## Safe harbor

We welcome good-faith research. Do not access vaults or systems you do not own. Testing on your own Fortiva install with your own data is encouraged.
