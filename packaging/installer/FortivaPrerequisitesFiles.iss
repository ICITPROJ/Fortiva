; Shared prerequisite payloads — included from FortivaPersonal/Enterprise/Admin .iss
Source: "..\prerequisites\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: FortivaPrereq_NeedsWebView2
Source: "..\prerequisites\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: FortivaPrereq_NeedsVcRedist
