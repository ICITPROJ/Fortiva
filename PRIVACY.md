# Fortiva Privacy Policy

**Last updated:** 2026-06-06
**Publisher:** icmclab studio — https://fortiva.studio.icmclab.cloud/
**Canonical web policy:** https://fortiva.studio.icmclab.cloud/privacy.html

Fortiva is a local-first, zero-knowledge password manager for Windows. This
policy explains exactly what data Fortiva handles and what it does **not** do.

## Summary

- **We never see your data.** Your vault, entries, and master password stay on
  your device. icmclab studio operates no servers that receive your vault.
- **No telemetry, analytics, or tracking.** Fortiva contains no usage analytics,
  no advertising, and no third-party tracking SDKs.
- **No account required.** Fortiva Personal does not require sign-up or login to
  any online service.

## What Fortiva stores on your device

All of the following are stored locally (in your Windows user profile, or on a
USB drive you choose for portable mode), never transmitted to us:

- The encrypted vault (`vault.fva`) and its rolling snapshots. Contents are
  encrypted with AES-256-GCM; the key is derived from your master password with
  Argon2id.
- Local rollback/integrity state (`local.state`), protected with Windows DPAPI.
- Windows Hello key-protection material (`hello.keyprotect`), if you enable Hello unlock.
- Non-secret preferences (e.g. auto-lock timeout, theme, last portable vault
  location) in `%AppData%\Fortiva\user.prefs.json`.
- A local crash log (`fortiva-crash.log`) and, for Enterprise, a local audit
  log. These are stored on your device and are never uploaded. They are written
  to record errors and security-relevant events and are designed not to contain
  passwords, keys, or vault contents.

## The only network connection Fortiva makes

**Fortiva Personal** checks for application updates over HTTPS:

- It fetches a small release manifest from the project's GitHub release feed
  (and a `raw.githubusercontent.com` fallback).
- If an update is available and you choose to install it (or auto-update is
  enabled), it downloads the installer over HTTPS and verifies it against a
  SHA-256 hash before running it.

This update check sends only what is required to make an ordinary HTTPS request
(such as your IP address and a standard user agent, as seen by GitHub). It does
**not** send your vault, entries, master password, or any personal identifiers
created by Fortiva. You can disable automatic update checks in
**Settings → Updates**.

**Fortiva Enterprise / Admin** do not contact the public update feed; they are
updated through your organization's IT tooling.

## Browser extension

The optional Fortiva browser extension communicates only with the local Fortiva
application on your own machine:

- **Preferred:** loopback HTTP to `http://127.0.0.1:7847` while Fortiva is running (session token auth).
- **Fallback:** Windows named pipes via a one-shot native messaging host (`Fortiva.BrowserBridge.Host.exe`).

The extension does not make its own network requests to icmclab studio or any third party. Credentials are only released after host (domain) verification and a single-use, host-bound fill token.

## Data sharing

We do not sell, rent, or share your data, because we do not have it. Fortiva
performs no server-side processing of your vault.

## Children

Fortiva is not directed to children under the age required by your local law to
consent to data processing.

## Changes to this policy

If this policy changes, the "Last updated" date above will change and the
current version will be published with the application and at
https://fortiva.studio.icmclab.cloud/privacy.html.

## Contact

Questions: contact@studio.icmclab.cloud (see https://fortiva.studio.icmclab.cloud/)
