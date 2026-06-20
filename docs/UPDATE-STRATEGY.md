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

### In-app update lifecycle (Personal)

Manual **Check for updates → Install now** and the 24-hour auto-update path share the same pipeline:

1. Download installer from GitHub → verify **SHA-256** against `latest.personal.json`.
2. Copy vault + Hello sidecars to `%LocalAppData%\FortivaPersonal\pre-update-backups\` (best effort).
3. **Lock vault** (scrub secrets from memory).
4. Stop `Fortiva.BrowserBridge.Host.exe` if running.
5. Launch a **detached** update helper (`start` via ShellExecute, not a child of Fortiva) that waits for Fortiva to exit, runs Inno Setup to completion, then relaunches if needed; Fortiva exits after the helper starts.
6. Installer upgrades files under `%LocalAppData%\Programs\icmclab studio\Fortiva Personal\`.
7. Installer **relaunches** `Fortiva.Personal.exe` when setup was silent (`ShouldLaunchAfterSilentInstall`, `runascurrentuser`).
8. The update helper and installer `DeinitializeSetup` both reopen Fortiva if no instance is running after setup finishes.

Diagnostic log: `%LocalAppData%\FortivaPersonal\logs\update.log`

**Preserved across updates:** `vault.fva`, snapshots, `local.state`, Hello blobs, `user.prefs.json`, `appearance.json`, extension staging under `%LocalAppData%\FortivaPersonal\`, browser native-messaging registration (refreshed on next launch).

**Not preserved in-place:** files under the app install directory (replaced by the new build). User data never lives there.

**Guarantees (enforced in code):**
- Inno Setup uses a fixed `AppId` + `UsePreviousAppDir=yes` — upgrades only replace `{app}` binaries.
- Silent `/VERYSILENT` in-app updates never run the “delete old vault” prompt (`MustPreserveUserDataOnInstall`).
- `Deploy-FortivaPersonal.ps1` refuses targets that overlap `%APPDATA%\Fortiva` or `%LOCALAPPDATA%\FortivaPersonal`.
- Pre-update backup copies vault sidecars, `user.prefs.json`, and `appearance.json` before the installer runs.



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

Code signing: Personal installers currently ship **unsigned** (see `docs/CODESIGNING.md`). The release workflow Authenticode-signs published EXEs/installers only when `CODESIGN_PFX_*` secrets are configured; until then, first-run installs may show a Windows SmartScreen "Unknown publisher" prompt. Update integrity does not depend on Authenticode — the manifest is fetched over HTTPS and each installer is verified by SHA-256 hash before launch.

Legacy update host `studio.icmclab.cloud` is accepted only until **2026-09-01 UTC**; GitHub Releases is the canonical feed.



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


