[Setup]
AppId=io.github.mcmike0399.blastcircuit
AppName=Blast Circuit
AppVersion={#GameVersion}
VersionInfoVersion={#GameVersion}.{#GameBuild}
DefaultDirName={localappdata}\Programs\Blast Circuit
DefaultGroupName=Blast Circuit
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\BlastCircuit.exe
OutputDir={#OutputDirectory}
OutputBaseFilename=Blast-Circuit-windows-x64-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Files]
Source: "{#GameDirectory}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Blast Circuit"; Filename: "{app}\BlastCircuit.exe"; WorkingDir: "{app}"

[UninstallDelete]
; Automatic updates can add runtime files after the installer log was written.
; Restrict recursive removal to these product-owned subdirectories.
Type: filesandordirs; Name: "{app}\data_Bomberman64Multiplayer_windows_x86_64"
Type: filesandordirs; Name: "{app}\updater"
Type: filesandordirs; Name: "{app}\Notices"

[Run]
Filename: "{app}\BlastCircuit.exe"; Description: "Play Blast Circuit"; Flags: nowait postinstall skipifsilent
