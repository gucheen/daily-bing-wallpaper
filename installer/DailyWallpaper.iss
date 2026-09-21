; Built by scripts/build.py --installer using Inno Setup 6.7.3 or later.
#if Ver < EncodeVer(6, 7, 3)
  #error Inno Setup 6.7.3 or later is required
#endif
#ifndef AppId
  #define AppId "6408BD85-DA87-45D5-B05F-B11BACDF8B65"
#endif
#ifndef AppName
  #define AppName "每日壁纸"
#endif
#ifndef StartupValueName
  #define StartupValueName "DailyWallpaper"
#endif

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\DailyWallpaper
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitecture}
ArchitecturesInstallIn64BitMode={#AllowedArchitecture}
MinVersion=10.0.17763
DisableProgramGroupPage=yes
OutputDir=..\artifacts\installer
OutputBaseFilename=DailyWallpaper-{#Runtime}-Setup
SetupIconFile=..\src\DailyWallpaper.Windows\app.ico
UninstallDisplayIcon={app}\DailyWallpaper.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "zhcn"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "startup"; Description: "登录时自动启动每日壁纸"

[Files]
Source: "..\artifacts\{#Runtime}-framework-dependent\DailyWallpaper.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\DailyWallpaper.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#StartupValueName}"; ValueData: """{app}\DailyWallpaper.exe"""; Tasks: startup

[Run]
Filename: "{app}\DailyWallpaper.exe"; Description: "运行每日壁纸"; Flags: nowait postinstall skipifsilent

[Code]
const
  StartupKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  DotNetKey = 'SOFTWARE\dotnet\Setup\InstalledVersions\{#DotNetArchitecture}';
var
  DownloadPage: TDownloadWizardPage;

function FrameworkInstalled(const Framework, AssemblyName: String): Boolean;
var
  Versions: TArrayOfString;
  Location, Version: String;
  I: Integer;
begin
  Result := False;
  { .NET records its install location in the 32-bit registry view on all architectures. }
  if not RegQueryStringValue(HKLM32, DotNetKey, 'InstallLocation', Location) then Exit;
  if not RegGetValueNames(HKLM32, DotNetKey + '\sharedfx\' + Framework, Versions) then Exit;
  for I := 0 to GetArrayLength(Versions) - 1 do
  begin
    Version := Versions[I];
    { Accept stable 10.0 patches, but not previews or a different major version. }
    if (Copy(Version, 1, 5) = '10.0.') and
       (StrToIntDef(Copy(Version, 6, Length(Version)), -1) >= 0) and
       FileExists(AddBackslash(Location) + 'shared\' + Framework + '\' + Version + '\' + AssemblyName) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function RuntimeInstalled: Boolean;
begin
  Result := FrameworkInstalled('Microsoft.WindowsDesktop.App', 'System.Windows.Forms.dll') and
            FrameworkInstalled('Microsoft.NETCore.App', 'System.Private.CoreLib.dll');
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage('下载 .NET 桌面运行时',
    '正在从微软下载运行每日壁纸所需的组件…', nil);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if RuntimeInstalled then
  begin
    Log('Compatible .NET 10 Desktop Runtime detected; download skipped.');
    Exit;
  end;
  Log('Compatible .NET 10 Desktop Runtime missing.');
  if SuppressibleMsgBox('每日壁纸需要 .NET 10 桌面运行时（{#DotNetArchitecture}）。' + #13#10 +
    '是否从微软下载并安装？安装运行时需要管理员权限。', mbConfirmation, MB_YESNO, IDNO) <> IDYES then
  begin
    Result := '尚未安装 .NET 10 桌面运行时。请安装后重试。';
    Exit;
  end;
  try
    DownloadPage.Clear;
    DownloadPage.Add('{#RuntimeUrl}', 'windowsdesktop-runtime.exe', '{#RuntimeSHA256}');
    DownloadPage.Show;
    try
      DownloadPage.Download;
    finally
      DownloadPage.Hide;
    end;
    if not ShellExec('runas', ExpandConstant('{tmp}\windowsdesktop-runtime.exe'),
      '/install /passive /norestart', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ExitCode) then
    begin
      Result := '无法启动运行时安装程序：' + SysErrorMessage(ExitCode);
      Exit;
    end;
    if (ExitCode = 3010) or (ExitCode = 1641) then
    begin
      NeedsRestart := True;
      Result := '运行时安装需要重启。请重启电脑后重新运行每日壁纸安装包。';
      Exit;
    end;
    if ExitCode <> 0 then
      Result := Format('运行时安装未完成（退出码 %d）。请重试。', [ExitCode])
    else if not RuntimeInstalled then
      Result := '未检测到可用的 .NET 10 桌面运行时。请修复运行时后重试。';
  except
    Result := '无法下载或安装 .NET 桌面运行时：' + GetExceptionMessage;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and not WizardIsTaskSelected('startup') then
    RegDeleteValue(HKCU, StartupKey, '{#StartupValueName}');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Command: String;
begin
  if CurUninstallStep = usUninstall then
    if RegQueryStringValue(HKCU, StartupKey, '{#StartupValueName}', Command) then
      if CompareText(Command, '"' + ExpandConstant('{app}\DailyWallpaper.exe') + '"') = 0 then
        RegDeleteValue(HKCU, StartupKey, '{#StartupValueName}');
end;
