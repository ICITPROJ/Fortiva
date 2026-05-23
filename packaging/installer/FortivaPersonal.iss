; Fortiva Personal — Inno Setup installer script

; Packages the self-contained publish output from dist\Fortiva.Personal\



#define AppName        "Fortiva Personal"

#define AppVersion     "1.0.0"

#define AppPublisher   "icmclab studio"

#define AppURL         "https://studio.icmclab.cloud"

#define AppExeName     "Fortiva.Personal.exe"

#define SourceDir      "..\..\dist\Fortiva.Personal"

#define OutputDir      "..\..\dist\installers"



[Setup]

AppId={{B1C7E2A3-4D5F-4A6B-8C9D-0E1F2A3B4C5D}

AppName={#AppName}

AppVersion={#AppVersion}

AppVerName={#AppName} {#AppVersion}

AppPublisher={#AppPublisher}

AppPublisherURL={#AppURL}

AppSupportURL={#AppURL}/support

AppUpdatesURL={#AppURL}/fortiva/releases

UninstallDisplayName={#AppName} {#AppVersion} (icmclab studio)

UninstallDisplayIcon={app}\Assets\fortiva.ico

SetupIconFile=..\assets\fortiva-setup.ico

DefaultDirName={localappdata}\Programs\icmclab studio\{#AppName}

DefaultGroupName=Fortiva (icmclab studio)

AllowNoIcons=yes

OutputDir={#OutputDir}

OutputBaseFilename=FortivaPersonal-{#AppVersion}-Setup

Compression=lzma2/max

SolidCompression=yes

WizardStyle=modern

WizardImageFile=..\assets\wizard-sidebar.bmp

WizardSmallImageFile=..\assets\wizard-small.bmp

WizardSizePercent=100

DisableDirPage=no

DisableProgramGroupPage=no

MinVersion=10.0.19041

ArchitecturesInstallIn64BitMode=x64compatible

ArchitecturesAllowed=x64compatible

CloseApplications=yes

CloseApplicationsFilter={#AppExeName}

RestartApplications=no

; Per-user vault lives in %APPDATA% — do not require admin (avoids wrong-profile deletes)

PrivilegesRequired=lowest

PrivilegesRequiredOverridesAllowed=dialog



[Languages]

Name: "english"; MessagesFile: "compiler:Default.isl"



[Tasks]

Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked



[Files]

Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\..\dist\BrowserBridge\*"; DestDir: "{app}\BrowserBridge"; Flags: ignoreversion recursesubdirs createallsubdirs

Source: "..\..\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "com.fortiva.browserbridge.json"



[Registry]

Root: HKCU; Subkey: "Software\Google\Chrome\NativeMessagingHosts\com.fortiva.browserbridge.personal"; \
  ValueType: string; ValueName: ""; ValueData: "{app}\extension\com.fortiva.browserbridge.personal.json"; Flags: uninsdeletekey

Root: HKCU; Subkey: "Software\Microsoft\Edge\NativeMessagingHosts\com.fortiva.browserbridge.personal"; \
  ValueType: string; ValueName: ""; ValueData: "{app}\extension\com.fortiva.browserbridge.personal.json"; Flags: uninsdeletekey



[Icons]

Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; Comment: "Remove {#AppName} from this computer"

Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon



[Run]

Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent



[UninstallDelete]

Type: filesandordirs; Name: "{app}\logs"

Type: filesandordirs; Name: "{app}\temp"

Type: dirifempty; Name: "{app}"

; User data — also deleted in [Code] (kill process + retry). Listed here as first pass.

Type: filesandordirs; Name: "{userappdata}\Fortiva"

Type: filesandordirs; Name: "{userappdata}\Fortiva\Personal"

Type: filesandordirs; Name: "{localappdata}\FortivaPersonal"

Type: filesandordirs; Name: "{localappdata}\Fortiva"



[UninstallRun]
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath ($env:APPDATA+'\Fortiva\Personal'),($env:APPDATA+'\Fortiva'),($env:LOCALAPPDATA+'\FortivaPersonal'),($env:LOCALAPPDATA+'\Fortiva') -Recurse -Force -ErrorAction SilentlyContinue"""; Flags: runascurrentuser waituntilterminated; RunOnceId: "FortivaPersonalUserDataCleanup"

[Code]

procedure KillFortivaProcesses();

var

  ResultCode: Integer;

begin

  Exec('taskkill.exe', '/F /IM Fortiva.Personal.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Sleep(1500);

end;



procedure DeletePath(const Path: String);

begin

  if DirExists(Path) then

    DelTree(Path, True, True, True);

end;



procedure ForceDeletePersonalUserData();

begin

  KillFortivaProcesses();

  DeletePath(ExpandConstant('{userappdata}\Fortiva\Personal'));

  DeletePath(ExpandConstant('{userappdata}\Fortiva'));

  DeletePath(ExpandConstant('{localappdata}\FortivaPersonal'));

  DeletePath(ExpandConstant('{localappdata}\Fortiva'));

end;



function InitializeSetup(): Boolean;

var

  VaultFile: String;

begin

  VaultFile := ExpandConstant('{userappdata}\Fortiva\vault.fva');

  if FileExists(VaultFile) then

  begin

    if MsgBox(

      'Existing Fortiva vault data was found on this PC.' + #13#10#13#10 +

      'Yes — delete the old vault and create a new master password on first launch.' + #13#10 +

      'No  — keep your existing vault (you will see the unlock screen).',

      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then

    begin

      ForceDeletePersonalUserData();

    end;

  end;

  Result := True;

end;



function InitializeUninstall(): Boolean;

begin

  MsgBox(

    'Uninstalling Fortiva Personal will permanently delete:' + #13#10 +

    '  - Your encrypted vault and all saved passwords' + #13#10 +

    '  - Windows Hello unlock credential' + #13#10 +

    '  - Local settings and crash logs' + #13#10#13#10 +

    'After reinstall you will need to create a new master password.',

    mbInformation, MB_OK);

  Result := True;

end;



procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);

begin

  { Delete at start (after CloseApplications) and again at end in case files were locked }

  if (CurUninstallStep = usUninstall) or (CurUninstallStep = usPostUninstall) then

    ForceDeletePersonalUserData();

end;



procedure CurStepChanged(CurStep: TSetupStep);

var

  BridgeExe, JsonPath, JsonContent: String;

begin

  if CurStep <> ssPostInstall then

    Exit;

  BridgeExe := ExpandConstant('{app}\BrowserBridge\Fortiva.BrowserBridge.Host.exe');

  JsonPath := ExpandConstant('{app}\extension');

  ForceDirectories(JsonPath);

  JsonPath := JsonPath + '\com.fortiva.browserbridge.personal.json';

  JsonContent :=

    '{' + #13#10 +

    '  "name": "com.fortiva.browserbridge.personal",' + #13#10 +

    '  "description": "Fortiva local credential bridge",' + #13#10 +

    '  "path": "' + BridgeExe + '",' + #13#10 +

    '  "type": "stdio",' + #13#10 +

    '  "allowed_origins": [' + #13#10 +

    '    "chrome-extension://REPLACE_WITH_EXTENSION_ID/"' + #13#10 +

    '  ]' + #13#10 +

    '}';

  SaveStringToFile(JsonPath, JsonContent, False);

end;


