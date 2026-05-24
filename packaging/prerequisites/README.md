# Installer prerequisites

Fortiva installers bundle and silently install these when missing on the target PC:

| Prerequisite | Purpose | Installer |
|--------------|---------|-----------|
| **Microsoft Edge WebView2 Runtime** | Required by WinUI 3 / Windows App SDK | `MicrosoftEdgeWebview2Setup.exe` (Evergreen Bootstrapper) |
| **Visual C++ 2015–2022 Redistributable (x64)** | Native dependencies used by self-contained runtime | `vc_redist.x64.exe` |

.NET 8 and the Windows App SDK are **already bundled** in the publish output (`WindowsAppSDKSelfContained=true`).

## Fetch before building installers

```powershell
./scripts/fetch-installer-prerequisites.ps1
./build-installers.ps1
```

`build-installers.ps1` runs the fetch script automatically.

## Sources

- WebView2: https://go.microsoft.com/fwlink/p/?LinkId=2124703
- VC++ x64: https://aka.ms/vs/17/release/vc_redist.x64.exe
