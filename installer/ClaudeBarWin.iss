; Instalador per-user de ClaudeBar for Windows (sin admin).
; La versión llega por /DMyVersion=x.y.z desde scripts/release.ps1.
#ifndef MyVersion
  #define MyVersion "0.0.0"
#endif

[Setup]
AppId={{A7C3E1F2-5B8D-4E9A-9C1F-3D2E4F6A8B0C}
AppName=ClaudeBar for Windows
AppVersion={#MyVersion}
AppPublisher=Yovan Castro
AppPublisherURL=https://github.com/Yovancas/claudebar-win
DefaultDirName={localappdata}\Programs\ClaudeBarWin
DefaultGroupName=ClaudeBar
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=ClaudeBarWin-Setup-{#MyVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Detecta/cierra la instancia en marcha por el mutex de instancia única de la app.
CloseApplications=yes
RestartApplications=no
AppMutex=ClaudeBarWin_SingleInstance

[Files]
Source: "..\publish\ClaudeBarWin.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"
Name: "{userdesktop}\ClaudeBar"; Filename: "{app}\ClaudeBarWin.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Crear acceso directo en el escritorio"; Flags: unchecked

[Run]
; Relanzar tras instalar (interactivo y silencioso/auto-update).
Filename: "{app}\ClaudeBarWin.exe"; Description: "Iniciar ClaudeBar"; Flags: nowait postinstall skipifsilent runascurrentuser
Filename: "{app}\ClaudeBarWin.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent

[UninstallDelete]
; Solo limpia la carpeta de la app. NO toca %APPDATA%\ClaudeBarWin (config/history.db).
Type: filesandordirs; Name: "{app}"
