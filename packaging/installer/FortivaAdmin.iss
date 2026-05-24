; Fortiva Admin Console — Inno Setup installer script

#define AppName        "Fortiva Admin Console"
#define AppVersion     "1.0.0"
#define AppPublisher   "icmclab studio"
#define AppURL         "https://studio.icmclab.cloud"
#define AppExeName     "Fortiva.Admin.exe"
#define SourceDir      "..\..\dist\Fortiva.Admin"
#define OutputDir      "..\..\dist\installers"

[Setup]
AppId={{C2D8F3B4-5E6A-4B7C-9D0E-1F2A3B4C5D6E}
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
OutputBaseFilename=FortivaAdmin-{#AppVersion}-Setup
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
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\..\dist\LicenseTool\*"; DestDir: "{app}\LicenseTool"; Flags: ignoreversion recursesubdirs createallsubdirs
#include "FortivaPrerequisitesFiles.iss"

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

procedure DeleteAdminConfig(const Dir: String);
begin
  DeleteFileIfExists(Dir + '\policies.json');
  DeleteFileIfExists(Dir + '\license.dat');
  DeleteFileIfExists(Dir + '\shared-vaults.json');
end;

procedure DeleteCrashLogs;
var LogDir: String;
begin
  LogDir := ExpandConstant('{localappdata}\FortivaAdmin');
  if DirExists(LogDir) then DelTree(LogDir, True, True, True);
end;

procedure KillFortivaProcesses();
var ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM Fortiva.Admin.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM Fortiva.Enterprise.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec('taskkill.exe', '/F /IM Fortiva.BrowserBridge.Host.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(1500);
end;

// Admin Console manages policy/license/shared-vault config in %PROGRAMDATA%\Fortiva.
// Never delete enterprise vault files (vault.fva) from this uninstaller.
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
    if (FileExists(ConfigDir + '\policies.json')) or
       (FileExists(ConfigDir + '\license.dat')) or
       (FileExists(ConfigDir + '\shared-vaults.json')) then
    begin
      Msg := 'Delete Fortiva admin configuration (policies, license, shared vault settings)?' + #13#10 +
             'Location: ' + ConfigDir + #13#10#13#10 +
             'Enterprise vault data will NOT be deleted.' + #13#10 +
             'Click YES to delete admin config (IRREVERSIBLE).' + #13#10 +
             'Click NO to keep configuration.';
      Answer := MsgBox(Msg, mbConfirmation, MB_YESNO or MB_DEFBUTTON2);
      if Answer = IDYES then
      begin
        DeleteAdminConfig(ConfigDir);
        if MsgBox('Also delete enterprise audit logs in ' + ConfigDir + '\audit?', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
          DeleteAuditLogs;
      end;
    end;
  end;
end;
