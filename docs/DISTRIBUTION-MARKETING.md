# Fortiva — distribution & discovery

How users find, download, and install Fortiva. Complements [`RELEASE-PIPELINE.md`](RELEASE-PIPELINE.md) (how builds ship) and [`UPDATE-STRATEGY.md`](UPDATE-STRATEGY.md) (how updates reach installed clients).

For system context see [`ARCHITECTURE.md`](ARCHITECTURE.md). Marketing site lives in **ICITPROJ/Fortiva-Website** (Azure Static Web Apps — separate repo).

---

## Current distribution model (recommended for launch)

| Channel | Role | Status |
|---------|------|--------|
| **GitHub Releases** | Host installers + update manifest | **Primary** — CI on push to `main` |
| **Inno Setup EXE** | Personal install + silent in-app update | **Primary** |
| **Fortiva-Website** | Landing page + download button | **Recommended funnel** |
| **winget** | `winget install Fortiva.Personal` | **High-value next step** — optional, not in CI yet |
| **Microsoft Store** | Consumer discovery | **Not ready** — see [Store vs direct install](#microsoft-store-vs-direct-install) |
| **Enterprise** | IT / Intune / manual EXE | Sales-led, not self-serve download |

**Canonical Personal download (technical):**

- Latest release: https://github.com/ICITPROJ/Fortiva/releases/latest
- Update manifest: https://github.com/ICITPROJ/Fortiva/releases/latest/download/latest.personal.json

End users should hit a **website download button** that redirects here — not raw GitHub unless the audience is technical.

---

## Microsoft Store vs direct install

### Recommendation: direct install first

For Fortiva **today**, **GitHub + Inno Setup** is the better fit:

1. **Architecture match** — Silent updates, vault in `%APPDATA%`, browser bridge, extension staging are built and tested for a normal Win32 per-user install.
2. **Store review** — Password managers face extra scrutiny; native messaging, helper EXE, and custom update URLs need explanation and can delay approval.
3. **Update control** — Push to `main` → ~8–10 min → users **Check for updates**. Store adds review lag and policy that Store builds should not bypass Store with a custom updater.
4. **Audience fit** — Zero-knowledge / local-first appeals to users who prefer vendor direct download over a Store middleman.
5. **Enterprise** — IT uses Intune/EXE anyway; Personal on GitHub does not block Enterprise.

MSIX scaffolding exists under `packaging/msix/` but MSIX tooling is **disabled** in `Directory.Build.props` and Release CI builds **Inno installers only**. Store is a separate project, not a toggle.

### When Store may be worth it later

- Casual users who refuse to download “from the internet”
- Store badge as trust signal for non-technical home users
- Willingness to ship a **Store-only SKU** (Store updates, no GitHub updater, bridge/extension re-validated under Store paths)

### Practical phased channels

| Phase | Channel |
|-------|---------|
| **Now (launch)** | GitHub Releases + installer + in-app update |
| **Soon (optional)** | **winget** — same EXE, more discoverability, less Store friction |
| **Later (if demand)** | Store listing as a **second build flavor** |

If the goal is more discovery without Store constraints, **winget + a clear download page** is usually the best step after GitHub.

---

## Download funnel (single path)

Everything should converge on one flow:

```text
Marketing site: https://fortiva.studio.icmclab.cloud/ (privacy: /privacy.html, terms: /terms.html)
        │
        ▼
  “Download for Windows”
        │
        ▼
  GitHub Releases latest installer
  (FortivaPersonal-{version}-Setup.exe)
        │
        ▼
  Install → vault in %APPDATA%\Fortiva\
        │
        ▼
  Future updates: Settings → Check for updates (or auto-check on launch)
```

### Website should include

- One-line value prop: *Zero-knowledge password manager — vault stays on your PC*
- Primary **Download for Windows** CTA → latest GitHub release (or short URL you control)
- Link to [`UserManual.md`](UserManual.md) or hosted quick start
- Screenshots or short demo video
- **Enterprise** — separate CTA (contact / request license), not the same as Personal download

### GitHub README should include

- What Fortiva is (one paragraph)
- Screenshot or GIF
- **Download latest release** link
- Link to user manual and security docs (optional: summary of audit posture)

---

## Target audiences (realistic)

Fortiva will not win mass market against Bitwarden/1Password on day one. Focus on people who want **exactly** what Fortiva is.

| Audience | Why they care | Where to reach them |
|----------|---------------|---------------------|
| Privacy / local-first users | No cloud vault by default | Reddit, HN, Mastodon, security forums |
| Windows power users | Native WinUI app | r/Windows, r/privacy, dev communities |
| Small business / solo IT | Enterprise edition, policy, audit | LinkedIn, MSP forums |
| Existing icmclab network | Trust already established | Clients, colleagues, direct outreach |

### Positioning message (lead with differentiation)

> **Fortiva** — Your passwords encrypted on **your** Windows PC. Zero-knowledge vault, optional browser fill, no cloud account required for Personal.

This attracts the right users and filters out those who need free multi-device cloud sync (a different product category).

---

## Low-cost discovery channels

### 1. GitHub as credibility

Public repo, releases, and security documentation build trust with technical users. Keep README and release notes clear.

### 2. winget (recommended next technical step)

Submit a manifest to [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) so users can install via:

```powershell
winget install Fortiva.Personal
```

Same signed installer as GitHub; adds legitimacy beyond “EXE from a repo.” Document the manifest in-repo when added (e.g. `packaging/winget/`).

### 3. Content, not ads

One strong post beats months of silence. Ideas:

- Why a vault that never leaves the PC
- Summary of security audit / pentest (public-facing, non-exploitative)
- Short demo: unlock → save entry → browser fill

Channels: Hacker News (Show HN), Reddit (r/privacy, r/selfhosted — follow sub rules), Dev.to, LinkedIn.

### 4. Enterprise = sales, not viral download

Personal is self-serve. Enterprise needs a **contact / demo / license** path on the website — IT rarely adopts from a Reddit thread.

### 5. SEO on owned site

Example page topics:

- Local password manager for Windows
- Honest comparison vs cloud password managers
- Enterprise password manager for Windows (Intune)

### 6. Word of mouth

Make sharing easy: stable download URL, clear free/paid story, concrete reasons to recommend (local vault, Windows Hello, optional extension, etc.).

---

## What not to rely on at first

| Channel | Why |
|---------|-----|
| Microsoft Store | Weak discovery for unknown brands; significant packaging/policy work |
| Paid search (“password manager”) | Expensive, dominated by incumbents |
| Random download aggregators | Low trust; malware reputation risk |
| Viral growth | Security tools grow slowly without a unique hook or press |

---

## Suggested 30-day launch checklist

| Week | Action |
|------|--------|
| **1** | Website: landing page + Download → latest GitHub release |
| **1** | GitHub README: screenshot, features, download link |
| **2** | Submit **winget** manifest (after a stable tagged release) |
| **2** | 2–3 minute demo video (unlock, add entry, optional extension) |
| **3** | One launch post (HN / Reddit / LinkedIn) — technical, honest |
| **4** | 5–10 trusted users: feedback + optional quote for the site |

---

## Enterprise vs Personal (messaging)

| Edition | Discovery model |
|---------|-------------------|
| **Personal** | Website, GitHub, winget, community posts |
| **Enterprise** | Direct sales, IT channels, Intune packages, GitHub release assets for IT |
| **Admin** | Bundled with Enterprise deployment |

See [`ONBOARDING-RECOVERY.md`](ONBOARDING-RECOVERY.md) for IT deployment notes (MSIX/Intune mentioned for Enterprise).

---

## Related docs

| Doc | Topic |
|-----|--------|
| [`RELEASE-PIPELINE.md`](RELEASE-PIPELINE.md) | CI, versioning, GitHub Releases |
| [`UPDATE-STRATEGY.md`](UPDATE-STRATEGY.md) | Personal auto-update behaviour |
| [`ARCHITECTURE.md`](ARCHITECTURE.md) | Fortiva-Website repo, components |
| [`SECURITY-PENTEST-REPORT.md`](SECURITY-PENTEST-REPORT.md) | Public summary source for trust content |
| [`UserManual.md`](UserManual.md) | End-user documentation |

---

*Last updated: 2026-05-24 — distribution strategy for post-launch Personal edition; revise when winget or Store channels ship.*
