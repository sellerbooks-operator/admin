#define MyAppName "SellerBooks Operator"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "SellerBooks"
#define MyAppExeName "SellerBooks Operator.exe"

[Setup]
AppId={{D1C7F5C5-0B37-4A8A-9D1A-4C9C5B7B9D20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SellerBooks Operator
DefaultGroupName=SellerBooks Operator
OutputDir=..\artifacts
OutputBaseFilename=SellerBooks Operator Setup
SetupIconFile=SellerBooks Operator.ico
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\SellerBooks Operator.exe

[Files]
Source: "..\publish-operator\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall
Source: "..\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\SellerBooks Operator"; Filename: "{app}\SellerBooks Operator.exe"
Name: "{autodesktop}\SellerBooks Operator"; Filename: "{app}\SellerBooks Operator.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Shortcut:"

[Run]
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "Memasang Visual C++ Runtime..."; Flags: waituntilterminated
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Memasang Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated
Filename: "{app}\SellerBooks Operator.exe"; Description: "Jalankan SellerBooks Operator"; Flags: nowait postinstall skipifsilent