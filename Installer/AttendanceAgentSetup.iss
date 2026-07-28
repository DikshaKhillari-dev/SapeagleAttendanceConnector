[Setup]
AppName=Sapeagle Attendance Connector
AppVersion=1.0
DefaultDirName={autopf}\SapeagleAttendanceConnector
DefaultGroupName=Sapeagle Attendance Connector
OutputDir=Output
OutputBaseFilename=AttendanceAgentSetup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Attendance Connector"; Filename: "{app}\SapeagleAttendanceConnector.exe"
Name: "{commonstartup}\Attendance Connector"; Filename: "{app}\SapeagleAttendanceConnector.exe"
Name: "{autodesktop}\Attendance Connector"; Filename: "{app}\SapeagleAttendanceConnector.exe"

[Run]
Filename: "{app}\SapeagleAttendanceConnector.exe"; Description: "Launch Attendance Connector"; Flags: nowait postinstall skipifsilent