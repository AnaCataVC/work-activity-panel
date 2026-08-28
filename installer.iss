; Inno Setup Script for Work Activity Panel
#define MyAppName "Work Activity Panel"
#define MyAppVersion "1.6.0"
#define MyAppPublisher "AnaCataVC"
#define MyAppExeName "WorkActivityPanel.exe"

[Setup]
AppId={{D9A8374E-57B2-4A2D-A3D8-5B1D2F7A8E9C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
DefaultDirName={localappdata}\Programs\WorkActivityPanel
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=releases
OutputBaseFilename=WorkActivityPanel-Setup-v1.6.0

SetupIconFile=Assets\AppIcon.ico
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
ShowLanguageDialog=no
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Payload published by:
;   dotnet publish WorkActivityPanel.csproj -c Release -r win-x64 --self-contained true \
;     -p:PublishSingleFile=false -o releases\WorkActivityPanel-win-x64
;
; It lands inside the repo tree, so the project excludes releases\ and artifacts\ from its
; item globs (see DefaultItemExcludes in WorkActivityPanel.csproj); otherwise each build
; globs the previous payload back in and the installer carries the one before it.
;
; Multi-file (no PublishSingleFile): this section copies the whole tree anyway, so bundling
; buys nothing, and the bundler pads a 227 MB payload out to 2 GB.
Source: "releases\WorkActivityPanel-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\Assets\AppIcon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "taskkill.exe"; Parameters: "/F /IM {#MyAppExeName}"; Flags: runhidden; RunOnceId: "KillAppBeforeUninstall"

[Registry]
; Clean up Autostart entry from Current User registry upon uninstallation
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#MyAppName}"; Flags: uninsdeletevalue

[UninstallDelete]
; Clean up runtime user data, settings, and sync hashes upon full uninstallation
Type: filesandordirs; Name: "{localappdata}\WorkActivityPanel\Data"
Type: dirifempty; Name: "{localappdata}\WorkActivityPanel"

[Code]
// Terminate running instance during setup initialization
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Terminate running instance during uninstall initialization
function InitializeUninstall(): Boolean;
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

// Clean up user data directory upon uninstall completion
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DelTree(ExpandConstant('{localappdata}\WorkActivityPanel'), True, True, True);
  end;
end;

