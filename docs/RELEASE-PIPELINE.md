# Fortiva release pipeline



Fully automated path from **git tag** → **client auto-update** via **GitHub Releases** (no hosting fees).



```text

git tag v1.0.1 && git push origin v1.0.1

        │

        ▼

GitHub Actions (release.yml)

  • unit tests

  • build-release.ps1 + Inno Setup installers

  • latest.personal.json (version + SHA-256 + GitHub asset URLs)

  • GitHub Release assets attached to tag

        │

        ▼

Fortiva Personal clients (within 24h)

  GET https://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json

  download installer from GitHub Releases + verify SHA-256 + silent install

```



## Cost



| Service | Role | Cost |

|---------|------|------|

| **GitHub Actions** | Build + publish | Free tier (public repos: unlimited minutes) |

| **GitHub Releases** | Host manifest + installers | **Free** — [no bandwidth cap on release assets](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases) |

| **IONOS / Fasthosts / Azure** | Not used for updates | What you already pay (domains/site only) |



## One-time setup



Push this repo to GitHub. The workflow file is `.github/workflows/release.yml`.



No FTP secrets, storage accounts, or CDN required.



If you rename the repository, update `ReleaseManifestUrls.GitHubRepository` in  

`src/Fortiva.Core/Updates/ReleaseManifest.cs` and add the new `owner/repo` to  

`UpdateUrlPolicy` if needed.



## Releasing a version



### Automated (recommended)



```bash

git tag v1.0.1

git push origin v1.0.1

```



GitHub Actions runs automatically. Watch **Actions → Release**.



### Manual trigger



**Actions → Release → Run workflow** and enter e.g. `1.0.1`.



### Local dry run



```powershell

./build-release.ps1

./build-installers.ps1 -Version 1.0.1

./scripts/publish-release-manifest.ps1 -Version 1.0.1

# inspect packaging/releases/latest.personal.json

```



## Release assets (per tag)



Each GitHub Release includes:



| Asset | Purpose |

|-------|---------|

| `latest.personal.json` | Update manifest (also used by `/releases/latest/download/`) |

| `FortivaPersonal-{version}-Setup.exe` | Personal auto-update installer |

| `FortivaEnterprise-{version}-Setup.exe` | Optional manual / IT download |

| `FortivaAdmin-{version}-Setup.exe` | Optional manual / IT download |



Verify in a browser:



- https://github.com/ICITPROJ/az-700-prep/releases/latest/download/latest.personal.json



## Enterprise clients



Enterprise edition does **not** poll the public URL. IT installs from:



- GitHub Release assets, or

- Intune / manual distribution



## Troubleshooting



| Symptom | Fix |

|---------|-----|

| Update check fails | Rebuild client from this repo (uses GitHub Releases URL) |

| Manifest 404 | Publish a GitHub Release with `latest.personal.json` attached |

| SHA mismatch | Regenerate manifest with `publish-release-manifest.ps1` after rebuilding installer |

| Wrong repo in URLs | Set `GITHUB_REPOSITORY` in CI or pass `-Repository owner/repo` to manifest script |



## Security



Only GitHub release URLs from `ICITPROJ/Fortiva` are accepted. Installers must be named `FortivaPersonal-{version}-Setup.exe`. Legacy `studio.icmclab.cloud` URLs remain allowed for older builds.


