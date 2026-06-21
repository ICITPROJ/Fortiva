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

## Publisher lockup (icmclab studio)

Place **`icmclab-logo-source.png`** here (horizontal ICMCLAB mark; black background is OK).
The script removes the black matte and writes a **transparent** PNG for in-app use plus installer assets.

| File | Use |
|------|-----|
| `fortiva-logo.png` | Fortiva UI logo — title bar, Unlock, Onboarding |
| `fortiva-logo-paranoia.png` | Paranoia Mode Fortiva variant |
| `fortiva.ico` / `fortiva-paranoia.ico` | Taskbar + window icon |
| `Assets/icmclab-logo.png` | Settings → About (transparent PNG) |
| `packaging/assets/icmclab-logo.png` | Installer source copy |
| `packaging/assets/icmclab-setup.ico` | Inno Setup **setup EXE** icon |
| `packaging/assets/wizard-sidebar.bmp` | Installer wizard left banner |
| `packaging/assets/wizard-small.bmp` | Installer wizard top-right mark |
| `packaging/assets/fortiva-setup.ico` | Legacy Fortiva-only icon (optional) |
| `extension/icon16.png` … `icon128.png` | Browser extension toolbar icons |

Transparency is preserved when the source PNG already has an alpha channel.
