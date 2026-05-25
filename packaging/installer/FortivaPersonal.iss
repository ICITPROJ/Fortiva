; Fortiva Personal — Inno Setup installer script

; Packages the self-contained publish output from dist\Fortiva.Personal\



#define AppName        "Fortiva Personal"

#ifndef AppVersion
  #define AppVersion     "1.0.0"
#endif

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

Source: "..\..\dist\extension\*"; DestDir: "{app}\extension"; Flags: ignoreversion recursesubdirs createallsubdirs
#include "FortivaPrerequisitesFiles.iss"

; Native messaging registration is handled by Fortiva on first launch (BrowserBridgeInstallService).



[Icons]

Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"

Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"; Comment: "Remove {#AppName} from this computer"

Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon



[Run]
#include "FortivaPrerequisitesRun.iss"
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
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -Command ""Remove-Item -LiteralPath ($env:APPDATA+'\Fortiva\Personal'),($env:APPDATA+'\Fortiva'),($env:LOCALAPPDATA+'\FortivaPersonal'),($env:LOCALAPPDATA+'\Fortiva') -Recurse -Force -ErrorAction SilentlyContinue; Get-ChildItem -LiteralPath $env:TEMP -Filter 'FortivaPersonal-*-Setup.exe' -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue; $parent=Join-Path $env:LOCALAPPDATA 'Programs\icmclab studio'; if ((Test-Path -LiteralPath $parent) -and -not (Get-ChildItem -LiteralPath $parent -ErrorAction SilentlyContinue)) {{ Remove-Item -LiteralPath $parent -Force -ErrorAction SilentlyContinue }}"""; Flags: runascurrentuser waituntilterminated; RunOnceId: "FortivaPersonalUserDataCleanup"

[Code]
#include "FortivaPrerequisitesCode.iss"

procedure KillFortivaProcesses();

var

  ResultCode: Integer;

begin

  Exec('taskkill.exe', '/F /IM Fortiva.Personal.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exec('taskkill.exe', '/F /IM Fortiva.BrowserBridge.Host.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Sleep(2000);

end;



function PersonalVaultExists(): Boolean;

var

  VaultFile: String;

begin

  VaultFile := ExpandConstant('{userappdata}\Fortiva\vault.fva');

  if FileExists(VaultFile) then

  begin

    Result := True;

    Exit;

  end;

  VaultFile := ExpandConstant('{userappdata}\Fortiva\Personal\vault.fva');

  Result := FileExists(VaultFile);

end;



procedure TryDeleteFile(const Path: String);

var

  I: Integer;

begin

  if not FileExists(Path) then

    Exit;

  for I := 1 to 5 do

  begin

    if DeleteFile(Path) then

      Exit;

    if not FileExists(Path) then

      Exit;

    Sleep(400);

  end;

end;



procedure DeletePersonalVaultFilesInDir(const Dir: String);

var

  I: Integer;

begin

  if not DirExists(Dir) then

    Exit;

  TryDeleteFile(Dir + '\vault.fva');

  TryDeleteFile(Dir + '\local.state');

  TryDeleteFile(Dir + '\hello.keyprotect');

  TryDeleteFile(Dir + '\hello.binding');

  TryDeleteFile(Dir + '\user.prefs.json');

  for I := 0 to 4 do

    TryDeleteFile(Dir + '\vault.fva.snapshot' + IntToStr(I));

end;



procedure DeletePath(const Path: String);

begin

  if DirExists(Path) then

    DelTree(Path, True, True, True);

end;



procedure ForceDeletePersonalUserData();

var

  Attempt: Integer;

  AppData, Legacy, LocalPersonal, LocalLegacy: String;

begin

  AppData := ExpandConstant('{userappdata}\Fortiva');

  Legacy := ExpandConstant('{userappdata}\Fortiva\Personal');

  LocalPersonal := ExpandConstant('{localappdata}\FortivaPersonal');

  LocalLegacy := ExpandConstant('{localappdata}\Fortiva');



  for Attempt := 1 to 3 do

  begin

    KillFortivaProcesses();

    DeletePersonalVaultFilesInDir(AppData);

    DeletePersonalVaultFilesInDir(Legacy);

    DeletePath(Legacy);

    DeletePath(AppData);

    DeletePath(LocalPersonal);

    DeletePath(LocalLegacy);

    if not PersonalVaultExists() then

      Exit;

    Sleep(1500);

  end;

end;



function InitializeSetup(): Boolean;
var
  VaultFile: String;
begin
  if not FortivaPrereq_Initialize() then
  begin
    Result := False;
    Exit;
  end;

  if PersonalVaultExists() then

  begin

    VaultFile := ExpandConstant('{userappdata}\Fortiva\vault.fva');

    if not FileExists(VaultFile) then

      VaultFile := ExpandConstant('{userappdata}\Fortiva\Personal\vault.fva');



    if MsgBox(

      'Fortiva password data was found on this PC from a previous install:' + #13#10 +

      VaultFile + #13#10#13#10 +

      'This usually means the last uninstall left files behind (often because Fortiva was still running), ' +

      'or you previously chose to keep your vault.' + #13#10#13#10 +

      'Yes — remove the old vault and start fresh (new master password).' + #13#10 +

      'No  — keep the existing vault (you will see the unlock screen).',

      mbConfirmation, MB_YESNO or MB_DEFBUTTON1) = IDYES then

    begin

      ForceDeletePersonalUserData();

      if PersonalVaultExists() then

        MsgBox(

          'Could not delete the existing vault — files may be in use.' + #13#10 +

          'Close Fortiva and Fortiva.BrowserBridge.Host, then run the installer again, ' +

          'or delete this folder manually:' + #13#10 + VaultFile,

          mbError, MB_OK);

    end;

  end;

  Result := True;

end;



function InitializeUninstall(): Boolean;

var

  VaultFile: String;

begin

  MsgBox(

    'Uninstalling Fortiva Personal will permanently delete:' + #13#10 +

    '  - Your encrypted vault and all saved passwords' + #13#10 +

    '  - Windows Hello unlock credential' + #13#10 +

    '  - Local settings and crash logs' + #13#10#13#10 +

    'Close Fortiva before continuing. If the app or browser bridge is still running, ' +

    'some files may survive uninstall.',

    mbInformation, MB_OK);

  Result := True;

end;



procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);

var

  VaultFile: String;

begin

  { Delete at start (after CloseApplications) and again at end in case files were locked }

  if (CurUninstallStep = usUninstall) or (CurUninstallStep = usPostUninstall) then

    ForceDeletePersonalUserData();



  if CurUninstallStep = usPostUninstall then

  begin

    if PersonalVaultExists() then

    begin

      VaultFile := ExpandConstant('{userappdata}\Fortiva\vault.fva');

      if not FileExists(VaultFile) then

        VaultFile := ExpandConstant('{userappdata}\Fortiva\Personal\vault.fva');

      MsgBox(

        'Some Fortiva data could not be removed (files were in use or locked).' + #13#10#13#10 +

        'Before reinstalling, delete this folder manually or restart Windows and run uninstall again:' + #13#10 +

        VaultFile,

        mbError, MB_OK);

    end;

  end;

end;



procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep <> ssPostInstall then
    Exit;

  FortivaPrereq_AfterInstall();
end;


