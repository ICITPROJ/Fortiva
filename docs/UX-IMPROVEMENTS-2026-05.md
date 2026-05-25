# Fortiva UX & Quality Pass — May 2026

## Summary

Focused pass on **fewer clicks**, **visible trust**, and **audit backlog fixes** from the competitive analysis review.

## Click reduction

| Before | After |
|--------|-------|
| Add entry → full form → generate → save (4+ steps) | **Quick add** dialog: title + optional fields, auto password, **Save** (1 dialog) |
| Generate from vault → navigate to generator page | **Ctrl+G** / toolbar **Generate** → dialog → create entry or copy |
| New entry with empty password | **Auto-generated** strong password on open |
| Generator page copy-only | **Create entry** button pre-fills entry editor |
| Save entry mouse-only | **Ctrl+S** on entry page |
| Copy with no feedback | Status bar + **clipboard countdown** in title bar |

## Trust UX

- Title bar shows `Clipboard clears in Ns` after any clipboard copy (vault, entry, generator).
- Vault list copy updates status message.
- Empty vault state with Quick add / Full form shortcuts.

## Audit / bug fixes

| ID | Fix |
|----|-----|
| P1 | Portable dialog **Use local vault** now calls `SwitchToLocalVault()` (clears saved USB path) |
| P1 | Portable onboarding skips leftover `%APPDATA%` check when portable mode active |
| P1 | Mandatory Hello hides Skip when Hello unavailable on onboarding |
| P2 | Enterprise unlicensed hides **Audit Log** nav (Settings retained for future prefs) |
| P2 | Enterprise auto-lock applied on unlock via policy-aware timeout |
| P2 | Shared vault / org vault status messages in shell |
| P2 | `ResumeAutoLock()` cannot go negative |
| P2 | Import/Export buttons refresh on vault lock via `StateChanged` |
| P2 | Onboarding `BrandAppearanceChanged` unsubscribed on navigate away |
| P2 | Entry page password copy wrapped in try/catch |

## Test results

| Suite | Result |
|-------|--------|
| Fortiva.Core.Tests | **143 passed** |
| Fortiva.AppHost.Tests | **4 passed** |
| `build-release.ps1` | **Success** |

## Not in this pass (roadmap)

- Global hotkey overlay (Ctrl+Shift+F search)
- Browser extension v2 / TOTP fill
- Shared PC per-user vault routing
- Passkeys ceremony
- HIBP breach check

## Files touched (primary)

- `QuickAddEntryDialog.cs`, `EntryDraft.cs`, `KeyboardHelpers.cs`
- `VaultPage.xaml(.cs)`, `EntryPage.xaml(.cs)`, `PasswordGeneratorPage.xaml(.cs)`
- `ClipboardService.cs`, `MainWindow.xaml.cs`
- `ShellViewModel.cs`, `VaultSession.cs`
- `OnboardingPage.xaml.cs`, `ImportExportPage.xaml.cs`
- `PasswordGeneratorPanel.cs`
