#define MyAppName "Hammerspoon (Windows Edition)"
#define MyAppPublisher "nocdn"
#define MyAppUrl "https://github.com/nocdn/hammerspoon-win"
#define MyAppExeName "Hammerspoon (Windows Edition).exe"
#define MyInstallFolderName "Hammerspoon (Windows Edition)"
#define MyAppVersion GetEnv("HSWIN_VERSION")
#define MyVersionInfoVersion GetEnv("HSWIN_VERSION_INFO_VERSION")
#define MyPublishDir GetEnv("HSWIN_PUBLISH_DIR")
#define MyOutputDir GetEnv("HSWIN_OUTPUT_DIR")

#if MyAppVersion == ""
  #define MyAppVersion "0.1.0"
#endif

#if MyVersionInfoVersion == ""
  #define MyVersionInfoVersion "0.1.0.0"
#endif

#if MyPublishDir == ""
  #define MyPublishDir "..\artifacts\publish\installer\Release\win-x64"
#endif

#if MyOutputDir == ""
  #define MyOutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{5E65F5D3-AC46-43D2-B31C-B98F8757C640}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}
AppUpdatesURL={#MyAppUrl}
DefaultDirName={autopf}\{#MyInstallFolderName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir={#MyOutputDir}
OutputBaseFilename=hswin-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyVersionInfoVersion}
VersionInfoVersion={#MyVersionInfoVersion}
; Broadcasts WM_SETTINGCHANGE after uninstall removes the hspn PATH entry.
ChangesEnvironment=yes

[Files]
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autoprograms}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Hammerspoon (Windows Edition)"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  EnvironmentKey = 'Environment';

{ The app writes these at runtime (Start at login, Install hspn CLI), so Inno never tracked them. }
function RemoveDirectoryFromPath(const PathValue, Directory: String): String;
var
  Remaining, Entry, Normalized: String;
  Separator: Integer;
begin
  Result := '';
  Remaining := PathValue;
  Normalized := Lowercase(RemoveBackslashUnlessRoot(Directory));
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Entry := Remaining;
      Remaining := '';
    end
    else
    begin
      Entry := Copy(Remaining, 1, Separator - 1);
      Remaining := Copy(Remaining, Separator + 1, Length(Remaining));
    end;

    if (Trim(Entry) <> '') and (Lowercase(RemoveBackslashUnlessRoot(RemoveQuotes(Trim(Entry)))) <> Normalized) then
    begin
      if Result <> '' then
        Result := Result + ';';
      Result := Result + Entry;
    end;
  end;
end;

procedure RemoveCliFromUserPath();
var
  PathValue, Updated: String;
begin
  if RegQueryStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', PathValue) then
  begin
    Updated := RemoveDirectoryFromPath(PathValue, ExpandConstant('{app}'));
    if Updated <> PathValue then
      RegWriteExpandStringValue(HKEY_CURRENT_USER, EnvironmentKey, 'Path', Updated);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, '{#MyAppName}');
    RegDeleteValue(HKEY_CURRENT_USER, RunKey, 'HsWin');
    RemoveCliFromUserPath();
  end;
end;
