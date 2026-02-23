#define MyAppName "EasyKataGo Launcher (Dev)"
#define MyAppExeName "EasyKataGoLauncher.exe"

#ifndef SourceDir
  #define SourceDir "."
#endif

#ifndef MyOutputDir
  #define MyOutputDir "."
#endif

#ifndef MyOutputBaseFilename
  #define MyOutputBaseFilename "EasyKataGoLauncher-TauriDev-Setup"
#endif

[Setup]
AppId={{2D4DF5DF-503E-4CEF-8F2A-8803EAC46E6D}
AppName={#MyAppName}
AppVersion=0.1.0-dev
AppPublisher=easykatago contributors
AppPublisherURL=https://github.com/easykatago/easykatago
DefaultDirName={localappdata}\EasyKataGoLauncherDev
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
#ifexist "compiler:Languages\ChineseSimplified.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#else
  #ifexist "compiler:Languages\ChineseSimplified-12-0.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified-12-0.isl"
  #endif
#endif

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\Start-EasyKataGoLauncher.cmd"
Name: "{autodesktop}\EasyKataGo Dev"; Filename: "{app}\Start-EasyKataGoLauncher.cmd"; Tasks: desktopicon

[Run]
Filename: "{app}\Start-EasyKataGoLauncher.cmd"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
