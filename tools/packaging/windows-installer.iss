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
Name: "{group}\Blast Circuit (DirectX 12)"; Filename: "{app}\BlastCircuit.exe"; Parameters: "--rendering-driver d3d12"; WorkingDir: "{app}"

[Run]
Filename: "{app}\BlastCircuit.exe"; Description: "Play Blast Circuit"; Flags: nowait postinstall skipifsilent

[Code]
function GetFileAttributesW(FileName: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';

function OwnedFile(Relative: String; var FullName: String): Boolean;
var
  Root, Parent: String;
  Attributes: Cardinal;
begin
  Result := False;
  if Relative = '' then Exit;
  if (Relative[1] = '\') or (Relative[1] = '/') or
     (Pos(':', Relative) > 0) or (Pos('*', Relative) > 0) or (Pos('?', Relative) > 0) then Exit;
  StringChangeEx(Relative, '/', '\', True);
  Root := AddBackslash(ExpandFileName(ExpandConstant('{app}')));
  FullName := ExpandFileName(Root + Relative);
  if CompareText(Copy(FullName, 1, Length(Root)), Root) <> 0 then Exit;
  Parent := ExtractFileDir(FullName);
  while Length(AddBackslash(Parent)) >= Length(Root) do begin
    Attributes := GetFileAttributesW(Parent);
    if (Attributes <> $FFFFFFFF) and ((Attributes and $400) <> 0) then Exit;
    if CompareText(AddBackslash(Parent), Root) = 0 then Break;
    Parent := ExtractFileDir(Parent);
  end;
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Files: TArrayOfString;
  Index: Integer;
  FullName, Parent, Root: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  { Read the current signed payload's inventory before the original install log
    removes it. An update may have introduced files absent from that old log. }
  if not LoadStringsFromFile(ExpandConstant('{app}\installed-files.txt'), Files) then Exit;
  Root := AddBackslash(ExpandFileName(ExpandConstant('{app}')));
  for Index := 0 to GetArrayLength(Files) - 1 do begin
    if OwnedFile(Files[Index], FullName) then begin
      DeleteFile(FullName);
      Parent := ExtractFileDir(FullName);
      while CompareText(AddBackslash(Parent), Root) <> 0 do begin
        { Only empty directories are removed; unrelated user files survive. }
        if not RemoveDir(Parent) then Break;
        Parent := ExtractFileDir(Parent);
      end;
    end;
  end;
end;
