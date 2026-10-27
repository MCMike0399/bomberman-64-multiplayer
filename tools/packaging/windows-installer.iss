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

[Run]
Filename: "{app}\BlastCircuit.exe"; Description: "Play Blast Circuit"; Flags: nowait postinstall skipifsilent
