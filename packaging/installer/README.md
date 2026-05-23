# EXE installer (MSIX wrapper)

Production builds should:

1. Build signed MSIX (`Fortiva.Personal.msix`, `Fortiva.Enterprise.msix`, `Fortiva.Admin.msix`).
2. Wrap with bootstrapper EXE (WiX or MSIX App Installer offline bundle) for environments without Microsoft Store or App Installer.
3. Deploy Enterprise/Admin via Intune Win32/MSIX LOB apps.

No custom update service for Enterprise — use Intune (Win32 supersedence).

Personal: automatic HTTPS update check (see `docs/UPDATE-STRATEGY.md`) or Microsoft Store / winget.
