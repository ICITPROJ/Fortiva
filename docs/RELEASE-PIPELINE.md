# Fortiva release pipeline

Automated path from **`git push origin main`** → **GitHub Release** → **Personal auto-update**.

For system context see [`ARCHITECTURE.md`](ARCHITECTURE.md). For client-side update behaviour see [`UPDATE-STRATEGY.md`](UPDATE-STRATEGY.md). For how users discover and download Fortiva see [`DISTRIBUTION-MARKETING.md`](DISTRIBUTION-MARKETING.md).

---

## Overview

```text
git push origin main
        │
        ▼
GitHub Actions — Release workflow (.github/workflows/release.yml)
  │
  ├─ prepare (ubuntu)
  │    • Skip if commit message contains [skip release]
  │    • Skip if HEAD already equals latest tag commit
  │    • Else: auto-bump patch from latest v*.*.* tag (e.g. 1.0.5 → 1.0.6)
  │
  └─ release (windows-latest) — if prepare says should_release
       • unit tests (Core + AppHost)
       • build-release.ps1 -Version {computed}
       • build-installers.ps1 -Version {computed}
       • publish-release-manifest.ps1
       • GitHub Release (tag v{x.y.z}, make_latest)
       • sync Directory.Build.props + extension/manifest.json
         commit: chore(release): sync version … [skip release]
        │
        ▼
Fortiva Personal (installed clients)
  GET …/releases/latest/download/latest.personal.json
  verify SHA-256 → silent install
```

**You do not need to create git tags manually.** Push to `main`; CI assigns the next patch version and publishes.

---

## Developer workflow (recommended)

```powershell
cd C:\Repo\Github\Fortiva
git add -A
git commit -m "Describe your change"
git push origin main
```

Then:

1. Watch **Actions → Release**: https://github.com/ICITPROJ/Fortiva/actions/workflows/release.yml  
2. Wait until the run is green (~8–10 minutes)  
3. In Fortiva: **Settings → Check for updates** → **Install now**

Optional helper (same result — push only):

```powershell
.\scripts\publish-release.ps1
```

---

## When releases are skipped

| Condition | Result |
|-----------|--------|
| `HEAD` is already the commit pointed to by the latest `v*.*.*` tag | No release (already published) |
| Commit message contains `[skip release]` | No release (version-sync bot commit) |
| Only CI/docs change with no new commits since tag | Same as first row |

---

## Manual / override triggers

### Push an explicit version tag

```bash
git tag v1.0.7
git push origin v1.0.7
```

The Release workflow runs for that tag with version **1.0.7** (no auto-bump).

### GitHub Actions UI

**Actions → Release → Run workflow**

- Leave version empty to auto-bump from latest tag  
- Or enter e.g. `1.0.7`

### Local dry run (no publish)

```powershell
./build-release.ps1 -Version 1.0.7
./build-installers.ps1 -Version 1.0.7
./scripts/publish-release-manifest.ps1 -Version 1.0.7
# inspect packaging/releases/latest.personal.json
```

---

## Version numbering

| Source | When used |
|--------|-----------|
| **Auto-bump** from latest git tag | Default on push to `main` |
| **Tag push** `vX.Y.Z` | Explicit release version |
| **`Directory.Build.props`** | Local/dev builds; synced by CI after release |
| **`scripts/bump-version.ps1 -Patch`** | Optional local bump before push |

CI build always uses the **computed release version**, not whatever happens to be in props at commit time (props may lag until sync commit).

---

## Release assets (per version)

Each GitHub Release includes:

| Asset | Purpose |
|-------|---------|
| `latest.personal.json` | Update manifest (`/releases/latest/download/`) |
| `FortivaPersonal-{version}-Setup.exe` | Personal auto-update installer |
| `FortivaEnterprise-{version}-Setup.exe` | Optional IT / manual download |
| `FortivaAdmin-{version}-Setup.exe` | Optional admin console download |

Verify manifest in a browser:

- https://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json

---

## Cost

| Service | Role | Cost |
|---------|------|------|
| **GitHub Actions** | Build + publish | Free tier (public repos) |
| **GitHub Releases** | Host manifest + installers | **Free** |

No FTP, Azure storage, or CDN required for Personal updates.

---

## One-time setup

Push this repo to **ICITPROJ/Fortiva** on GitHub. Workflows:

- `.github/workflows/ci.yml` — tests on every push  
- `.github/workflows/release.yml` — auto-release on `main`

If you rename the repository, update:

- `ReleaseManifestUrls.GitHubRepository` in `src/Fortiva.Core/Updates/ReleaseManifest.cs`
- `UpdateUrlPolicy` allowed repositories

---

## Enterprise clients

Enterprise edition does **not** poll the public manifest. IT distributes via:

- GitHub Release assets, or  
- Intune / manual install  

See `packaging/intune/` and [`UPDATE-STRATEGY.md`](UPDATE-STRATEGY.md).

---

## Troubleshooting

| Symptom | Cause | Fix |
|---------|-------|-----|
| App says “Up to date” but you pushed code | Release not finished or `HEAD` already tagged | Wait for green Release workflow; confirm manifest version at `/releases/latest/download/latest.personal.json` |
| `git push` says “everything up to date” | No new commits | `git commit` first, then push |
| Release workflow red | Build/installer/test failure | Open failed run logs on Actions tab |
| Manifest 404 | No successful release yet | Fix workflow; re-push or manual dispatch |
| SHA mismatch on install | Manifest out of sync with installer | Re-run release (manifest generated in CI from built EXE) |
| Wrong repo in app | Old build or wrong `GitHubRepository` constant | Ship new release from this repo |
| Looking at **Fortiva-Website** Actions | Wrong repository | Use **ICITPROJ/Fortiva** for the desktop app |

---

## Security

- Only GitHub release URLs from **ICITPROJ/Fortiva** are accepted by the client (`UpdateUrlPolicy`).
- Installers must match `FortivaPersonal-{version}-Setup.exe`.
- Manifest must include a real SHA-256 (placeholder hashes are rejected client-side).
- Legacy `studio.icmclab.cloud` URLs remain allowed for older builds.
- Release builds require **Authenticode signing** (icmclab publisher) for bridge clients and update install.
- Pre-update vault backup runs automatically before Personal auto-update (`pre-update-backups/`, last 3 kept).

### Post-release user actions (when extension or Hello changed)

1. **Browser extension** — Fortiva → Settings → Browser extension → **Connect browser**, then **Reload extension** in each browser profile.
2. **Windows Hello** — Existing Hello users may re-enroll once in Settings so v4 hardware-backed credentials apply when KeyCredential is available.

---

## Key scripts

| Script | Purpose |
|--------|---------|
| `build-release.ps1` | MSBuild + publish Personal/Enterprise/Admin + bridge + extension |
| `build-installers.ps1` | Inno Setup installers + prerequisites |
| `scripts/publish-release-manifest.ps1` | Write `latest.personal.json` with SHA-256 |
| `scripts/bump-version.ps1` | Bump/sync version in props + extension manifest |
| `scripts/publish-release.ps1` | Push `main` (documents auto-release) |
| `scripts/test-browser-extension.ps1` | Verify extension staging + registry |
