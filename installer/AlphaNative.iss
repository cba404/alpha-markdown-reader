#define MyAppName "α Markdown 编辑器"
#define MyAppVersion "1.2.8"
#define MyAppPublisher "Alpha"
#define MyAppExeName "α.exe"

[Setup]
AppId={{D6699756-61EA-4A0B-A649-22D7A4467C40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\α
DefaultGroupName={#MyAppName}
OutputDir=..\dist\installer
OutputBaseFilename=α-Markdown编辑器-Setup-x64
SetupIconFile=..\src\AlphaNative\Assets\Alpha.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务："; Flags: unchecked
Name: "associate"; Description: "将 .md 文件添加到“打开方式”列表"; GroupDescription: "文件关联："; Flags: checkedonce

[Files]
Source: "..\dist\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Classes\AlphaNative.Markdown"; ValueType: string; ValueData: "Markdown 文档"; Tasks: associate; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\AlphaNative.Markdown\DefaultIcon"; ValueType: string; ValueData: "{app}\{#MyAppExeName},0"; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\AlphaNative.Markdown\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Tasks: associate
Root: HKCU; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "AlphaNative.Markdown"; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "AlphaNative.Markdown"; ValueData: ""; Tasks: associate; Flags: uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; Flags: nowait postinstall skipifsilent
