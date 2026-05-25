# Fortiva brand source

## Taskbar / window icon (Logo Icon 3)

Place **`fortiva-icon-source.png`** here — your **Logo Icon 3** with a transparent background.
This drives the taskbar icon, `.exe` embedded icon, installer icon, and browser extension icons.

## UI logo (optional)

**`fortiva-logo-source.png`** — optional larger logo for marketing screens. If omitted, the icon
source is used for in-app UI logos too (`fortiva-logo.png`).

Regenerate all assets:

```powershell
python scripts/update-brand-assets.py
```

With a separate UI logo:

```powershell
python scripts/update-brand-assets.py --logo-source src/Fortiva.AppHost/Assets/source/fortiva-logo-source.png
```

Outputs:

| File | Use |
|------|-----|
| `fortiva-logo.png` | UI logo (transparent PNG) — title bar, Unlock, Onboarding, Settings |
| `fortiva-logo-paranoia.png` | Paranoia Mode variant |
| `fortiva.ico` / `fortiva-paranoia.ico` | **Taskbar + window icon** (from Logo Icon 3) |
| `packaging/assets/fortiva-setup.ico` | Inno Setup installer icon |
| `extension/icon16.png` … `icon128.png` | Browser extension toolbar icons |

Transparency is preserved when the source PNG already has an alpha channel.
