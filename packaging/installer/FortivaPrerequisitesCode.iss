// Shared Pascal helpers for prerequisite detection and OS validation.

function FortivaPrereq_RegVersionPresent(const Root: Integer; const SubKey, ValueName: String): Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(Root, SubKey, ValueName, Version) and (Version <> '');
end;

function FortivaPrereq_WebView2Installed(): Boolean;
begin
  Result :=
    FortivaPrereq_RegVersionPresent(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv') or
    FortivaPrereq_RegVersionPresent(HKLM, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv') or
    FortivaPrereq_RegVersionPresent(HKCU, 'Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv');
end;

function FortivaPrereq_VcRedistInstalled(): Boolean;
var
  Version: String;
begin
  Result :=
    RegQueryStringValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Version', Version) and
    (Version <> '');
end;

function FortivaPrereq_NeedsWebView2(): Boolean;
begin
  Result := not FortivaPrereq_WebView2Installed();
end;

function FortivaPrereq_NeedsVcRedist(): Boolean;
begin
  Result := not FortivaPrereq_VcRedistInstalled();
end;

function FortivaPrereq_ValidateEnvironment(): Boolean;
var
  Version: TWindowsVersion;
begin
  GetWindowsVersionEx(Version);
  if Version.Major < 10 then
  begin
    MsgBox(
      'Fortiva requires Windows 10 version 2004 (build 19041) or later.' + #13#10 +
      'Please update Windows and run the installer again.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  if (Version.Major = 10) and (Version.Build < 19041) then
  begin
    MsgBox(
      'Fortiva requires Windows 10 build 19041 or later (Windows 10 version 2004+).' + #13#10 +
      'Your build: ' + IntToStr(Version.Build) + #13#10#13#10 +
      'Please install the latest Windows updates.',
      mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Result := True;
end;

procedure FortivaPrereq_LogPrerequisitePlan();
begin
  if FortivaPrereq_NeedsWebView2() then
    Log('Prerequisite plan: install Microsoft Edge WebView2 Runtime')
  else
    Log('Prerequisite check: WebView2 Runtime already installed');

  if FortivaPrereq_NeedsVcRedist() then
    Log('Prerequisite plan: install Visual C++ 2015-2022 Redistributable (x64)')
  else
    Log('Prerequisite check: VC++ x64 runtime already installed');
end;

function FortivaPrereq_Initialize(): Boolean;
begin
  Result := FortivaPrereq_ValidateEnvironment();
  if not Result then Exit;

  FortivaPrereq_LogPrerequisitePlan();
  Result := True;
end;

procedure FortivaPrereq_AfterInstall();
begin
  if FortivaPrereq_NeedsWebView2() then
  begin
    if not FortivaPrereq_WebView2Installed() then
      Log('Warning: WebView2 Runtime may not have installed successfully.');
  end;
end;
