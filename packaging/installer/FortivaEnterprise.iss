; Fortiva Enterprise Client — Inno Setup installer script

#define AppName        "Fortiva Enterprise"
#ifndef AppVersion
  #define AppVersion     "1.0.0"
#endif
#define AppPublisher   "icmclab studio"
#define AppURL         "https://studio.icmclab.cloud"
#define AppExeName     "Fortiva.Enterprise.exe"
#define SourceDir      "..\..\dist\Fortiva.Enterprise"
#define OutputDir      "..\..\dist\installers"

[Setup]
AppId={{D3E9A4C5-6F7B-4C8D-0E1F-2A3B4C5D6E7F}
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
DefaultDirName={autopf}\icmclab studio\{#AppName}
DefaultGroupName=Fortiva (icmclab studio)
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=FortivaEnterprise-{#AppVersion}-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
WizardImageFile=..\assets\wizard-sidebar.bmp
WizardSmallImageFile=..\assets\wizard-small.bmp
MinVersion=10.0.19041
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible
CloseApplications=yes
CloseApplicationsFilter={#AppExeName}
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\dist\BrowserBridge\*"; DestDir: "{app}\BrowserBridge"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\dist\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs
#include "FortivaPrerequisitesFiles.iss"

; Native messaging registration is handled by Fortiva on first launch (BrowserBridgeInstallService).

[Icons]
Name: "{group}\{#AppName}";           Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; \
      Comment: "Remove {#AppName} from this computer"
Name: "{commondesktop}\{#AppName}";   Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
#include "FortivaPrerequisitesRun.iss"
Filename: "{app}\{#AppExeName}"; \
    Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; \
    Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"
Type: filesandordirs; Name: "{app}\temp"
Type: dirifempty;     Name: "{app}"

[Code]
#include "FortivaPrerequisitesCode.iss"

function InitializeSetup(): Boolean;
begin
  Result := FortivaPrereq_Initialize();
end;

procedure DeleteFileIfExists(const Path: String);
begin
  if FileExists(Path) then DeleteFile(Path);
end;

procedure DeleteAuditLogs;
var AuditDir: String;
begin
  AuditDir := ExpandConstant('{commonappdata}\Fortiva\audit');
  if DirExists(AuditDir) then DelTree(AuditDir, True, True, True);
end;

procedure ClearHelloCredential;
var HelloDir: String;
begin
  HelloDir := ExpandConstant('{localappdata}\FortivaEnterprise\Hello');
  if DirExists(HelloDir) then DelTree(HelloDir, True, True, True);
end;

procedure DeleteEnterpriseVault(const Dir: String);
var
  FindRec: TFindRec;
begin
  DeleteFileIfExists(Dir + '\vault.fva');
  DeleteFileIfExists(Dir + '\local.state');
  if FindFirst(Dir + '\vault.fva.snapshot*', FindRec) then
  begin
    try
      repeat
        DeleteFile(Dir + '\' + FindRec.Name);
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

procedure DeleteCrashLogs;
var LogDir: String;
begin
  LogDir := ExpandConstant('{localappdata}\FortivaEnterprise');
  if DirExists(LogDir) then DelTree(LogDir, True, True, True);
end;

procedure KillFortivaProcesses();
var ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM Fortiva.Enterprise.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM Fortiva.BrowserBridge.Host.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

// Enterprise vault + Hello live in %PROGRAMDATA%\Fortiva alongside admin config files.
// Uninstall removes vault only when requested; policies/license are preserved.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ConfigDir, Msg: String;
  Answer: Integer;
begin
  if (CurUninstallStep = usUninstall) or (CurUninstallStep = usPostUninstall) then
  begin
    KillFortivaProcesses();
    DeleteCrashLogs();
    if CurUninstallStep <> usPostUninstall then Exit;

    ConfigDir := ExpandConstant('{commonappdata}\Fortiva');
    if DirExists(ConfigDir) then
    begin
      Msg := 'Delete your enterprise vault (passwords and snapshots)?' + #13#10 +
             'Location: ' + ConfigDir + #13#10#13#10 +
             'Policies and license files will be kept for the Admin Console.' + #13#10 +
             'Click YES to delete vault data (IRREVERSIBLE).' + #13#10 +
             'Click NO to keep vault data; Windows Hello will be reset.';
      Answer := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if Answer = IDYES then
      begin
        DeleteEnterpriseVault(ConfigDir);
        ClearHelloCredential;
        if MsgBox('Also delete enterprise audit logs in ' + ConfigDir + '\audit?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
          DeleteAuditLogs;
      end
      else
        ClearHelloCredential;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  FortivaPrereq_AfterInstall();
end;
