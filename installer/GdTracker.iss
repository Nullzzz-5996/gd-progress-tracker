; Скрипт инсталлятора GD Progress Tracker для Inno Setup 6.
; Собирается через installer\build.ps1 — он публикует приложение и вызывает ISCC
; с уже подставленными AppVersion и PublishDir. Вручную:
;   ISCC.exe /DAppVersion=1.0.1.1 /DPublishDir=..\artifacts\publish installer\GdTracker.iss

#define AppName       "GD Progress Tracker"
#define AppExe        "GdTracker.App.exe"
#define AppPublisher  "snenashev"
#define AppUrl        "https://github.com/snenashev/gd-progress-tracker"

; Значения по умолчанию на случай запуска ISCC без /D — build.ps1 их переопределяет.
#ifndef AppVersion
  #define AppVersion "1.0.1.1"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish"
#endif
#ifndef OutputDir
  #define OutputDir "output"
#endif

[Setup]
; AppId менять нельзя: по нему Windows опознаёт установленную версию и обновляет её поверх.
AppId={{ECD48566-0801-4A6D-8E81-3EC383D19D1A}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

; Приложение x64: и сам инсталлятор, и распаковка идут в 64-битном режиме.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; По умолчанию ставим для текущего пользователя — без UAC и без прав администратора.
; Кнопка в диалоге позволяет выбрать установку для всех пользователей.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\GdTracker.App\Assets\app.ico

OutputDir={#OutputDir}
OutputBaseFilename=GdTrackerSetup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Публикация self-contained: рантайм .NET лежит рядом, отдельно ставить его не нужно.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; База и настройки живут в %LocalAppData% и удаление их не трогает —
; чистим только то, что приложение создаёт внутри своей папки.
Type: filesandordirs; Name: "{app}\logs"
Type: dirifempty; Name: "{app}"
