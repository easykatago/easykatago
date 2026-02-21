#define MyAppName "EasyKataGo Launcher"
#define MyAppExeName "EasyKataGoLauncher.exe"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "."
#endif

#ifndef MyOutputDir
  #define MyOutputDir AddBackslash(SourceDir) + "dist"
#endif

[Setup]
AppId={{6C5164C0-51B4-4A56-BD06-2A1D318FC220}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=easykatago contributors
AppPublisherURL=https://github.com/ayumomocha/easykatago
DefaultDirName={localappdata}\EasyKataGoLauncher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=EasyKataGoLauncher-Setup-v{#MyAppVersion}
SetupIconFile={#SourceDir}\Launcher.App\Assets\app.ico
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
Source: "{#SourceDir}\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
