# Fortiva Brand & Consistency Audit (2026-05-24)

## Website palette applied

| Token | Dark (default) | Light |
|-------|----------------|-------|
| Background | `#0A0C10` | `#F4F7FA` |
| Accent | `#40BCF4` | `#40BCF4` |
| Heading | `#FFFFFF` | `#0A0C10` |
| Body | `#A0AAB2` | `#5A6570` |
| Accent button text | `#0A0C10` | `#0A0C10` |

Implemented in `src/Fortiva.AppHost/Resources/FortivaTheme.xaml` with WinUI accent overrides.

## Edition consistency

| Area | Personal | Enterprise | Admin | Status |
|------|----------|------------|-------|--------|
| Theme / glass UI | Yes | Yes | Yes (aligned this pass) | OK |
| Logo (transparent PNG) | Yes | Yes | Yes | OK |
| Default dark theme | Yes | Yes | Yes | OK |
| FortivaAccentButton | Yes | Yes | Yes | OK |
| License gate page | N/A | Branded | N/A | OK |
| Audit status colors | Themed | Themed | Themed | OK |
| Browser extension popup | Branded CSS | — | — | OK |

## Security & operations (unchanged, verified)

- Zero-knowledge vault crypto unchanged
- Enterprise license + seat enforcement active
- Bridge token in-memory + secured pipe
- Policy enforced in Core export + vault security level
- 147 automated tests (run before release)

## UX improvements this pass

- Admin Console uses same glass panels, typography, and buttons as Personal/Enterprise
- Ambient glow orbs match website hero on main shell
- New installs default to **Dark** theme (website-first)
- Semantic text styles: `FortivaBodyText`, `FortivaMutedText`, `FortivaSectionTitle`

## Remaining optional polish

- Health/Onboarding password-strength bars still use functional RGB (intentional traffic-light colors)
- Light theme available in Settings for users who prefer it
- Presentation deck colors (`docs/presentation/`) not yet synced to `#40BCF4`

## Verify locally

```powershell
.\build-release.ps1
.\dist\Fortiva.Personal\Fortiva.Personal.exe
```

Check: title bar, unlock, onboarding, settings about, enterprise license page, admin tabs, extension popup.
