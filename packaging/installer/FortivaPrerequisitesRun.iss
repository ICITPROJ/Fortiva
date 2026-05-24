; Run before the main app launch entry in each installer.
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Installing Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: FortivaPrereq_NeedsWebView2
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Installing Microsoft Visual C++ runtime (x64)..."; Flags: waituntilterminated; Check: FortivaPrereq_NeedsVcRedist
