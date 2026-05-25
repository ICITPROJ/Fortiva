# Fortiva update strategy



## What users should expect



| Event | Does Fortiva need an update? | Who acts? |

|-------|------------------------------|-----------|

| **Windows cumulative security patch** (Patch Tuesday, Defender, .NET servicing on OS) | **No** — Fortiva ships self-contained (.NET + Windows App SDK bundled). Windows updates do not replace Fortiva's runtime. | Nobody |

| **Fortiva bug fix or security fix** | **Yes** — new Fortiva build | **Automatic** for Personal (see below); Intune for Enterprise |

| **New Windows *generation*** (e.g. post–Windows 11) or breaking WinUI change | **Yes** — new Fortiva build with updated SDK | icmclab release train (CI), then auto-delivered like any other Fortiva update |



Personal users should **not** need to contact icmclab for routine Microsoft patches.



## Personal — automatic updates



1. On launch (at most once per 24 hours), Fortiva checks the latest GitHub Release manifest:  

   `https://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json`

2. If a newer version exists, the installer is downloaded from the same GitHub Release and verified with **SHA-256**.

3. The Inno Setup installer runs silently (`/VERYSILENT`); vault data in `%APPDATA%\Fortiva` is preserved.

4. Toggle **Settings → Automatic updates** to disable (manual check remains).



This is the **only** network call Fortiva Personal makes by design.



Hosting is **free** via [GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) — no Azure, IONOS, or Fasthosts web hosting required for updates.



### Publishing a release (developers)



Releases are **automatic** when you push to `main` (see [`ARCHITECTURE.md`](ARCHITECTURE.md) and [`RELEASE-PIPELINE.md`](RELEASE-PIPELINE.md)).



```powershell
git add -A
git commit -m "Your change description"
git push origin main
# Wait ~8–10 min for GitHub Actions Release workflow
# Users: Settings → Check for updates
```



CI auto-bumps the patch version from the latest git tag, builds installers, publishes `latest.personal.json`, and creates the GitHub Release. **Manual git tags are optional** (override only).



Local manifest generation (dry run, no publish):



```powershell
./build-release.ps1 -Version 1.0.7
./build-installers.ps1 -Version 1.0.7
powershell -File scripts/publish-release-manifest.ps1 -Version 1.0.7
```



## Enterprise / Admin



- Updated through **Microsoft Intune** (Win32 app supersedence) — no public internet check from the client.

- IT controls rollout; icmclab publishes signed MSIs/EXEs to the customer tenant or GitHub Releases.



## Winget (optional)



Users who installed via `winget install Fortiva.Personal` also receive upgrades when the winget manifest is updated and `winget upgrade --all` (or winget's background policy) runs.



## Windows version gating



- **Minimum:** build `19041` (Windows 10 2004+) — enforced in installer and manifest.

- **Tested up to:** `maxWindowsBuildTested` in the release manifest — if the PC is newer, Fortiva still runs but recommends installing the latest Fortiva build (which extends tested range).



## When icmclab *does* get involved



Only for **platform generation changes** or **WinApp SDK major bumps** — not for every Microsoft security Tuesday. Those releases are built from the same pipeline; users receive them via auto-update without reinstalling manually.


