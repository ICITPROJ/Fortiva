# Extension CRX signing key

Enterprise **ExtensionInstallForcelist** requires a signed `.crx` whose ID matches
`extension/manifest.json` (`llkpcnbhmhpenahlcdnbbfmkdfkgnpnj`).

Place the PEM private key here (never commit):

```
packaging/extension-keys/fortiva-extension.pem
```

Or set environment variable `EXTENSION_PRIVATE_KEY_PEM` to the PEM file path (CI secret).

## If the original private key is lost

Generating a new key pair changes the extension ID and breaks existing sideloaded
installs. Only do this with a coordinated release:

1. `openssl genrsa -out fortiva-extension.pem 2048`
2. Export public key into `extension/manifest.json` `key` field (see `scripts/rotate-extension-key.ps1` when added).
3. Rebuild bridge manifests and republish CRX + update `updates.xml`.

Until a valid PEM exists, `scripts/pack-extension-crx.ps1` skips CRX output and Enterprise
force-install policy points at a manifest that will 404 until the first signed CRX release.
