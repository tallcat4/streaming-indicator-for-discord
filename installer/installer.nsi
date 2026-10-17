; Streaming Indicator for Discord のインストーラー（NSIS 3）。
; 使い方: makensis /DVERSION=1.0.0 /DPUBLISH_DIR=..\publish /DOUT_FILE=..\dist\setup.exe installer.nsi
; 管理者権限なしで、ユーザーごとに %LOCALAPPDATA%\Programs へインストールする。

Unicode true
!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "0.0.0"
!endif
!ifndef PUBLISH_DIR
  !define PUBLISH_DIR "..\publish"
!endif
!ifndef OUT_FILE
  !define OUT_FILE "StreamingIndicatorForDiscord-${VERSION}-setup.exe"
!endif

!define APP_NAME "Streaming Indicator for Discord"
!define APP_ID "StreamingIndicatorForDiscord"
!define APP_EXE "StreamingIndicatorForDiscord.exe"
; アプリのトレイメニュー「Windows 起動時に実行」と同じ値を使う。
!define RUN_KEY "Software\Microsoft\Windows\CurrentVersion\Run"
!define SETTINGS_KEY "Software\StreamingIndicatorForDiscord"
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define DESKTOP_RUNTIME_KEY "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"
!define DOTNET_DOWNLOAD_URL "https://dotnet.microsoft.com/download/dotnet/10.0"

Name "${APP_NAME}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_ID}"
InstallDirRegKey HKCU "${UNINSTALL_KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma

VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=0 "ProductName" "${APP_NAME}"
VIAddVersionKey /LANG=0 "FileDescription" "${APP_NAME} Setup"
VIAddVersionKey /LANG=0 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=0 "LegalCopyright" "GPL-3.0"

!define MUI_ICON "..\app.ico"
!define MUI_UNICON "..\app.ico"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
; インストール時に選んだ言語を覚えておき、アンインストーラーでも使う。
!define MUI_LANGDLL_REGISTRY_ROOT HKCU
!define MUI_LANGDLL_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\LICENSE"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

; 最初に挙げた言語が既定。Windows の言語に合わせて自動で選ばれる。
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Japanese"

LangString SecAppName ${LANG_ENGLISH} "${APP_NAME} (required)"
LangString SecAppName ${LANG_JAPANESE} "${APP_NAME}（必須）"
LangString SecStartupName ${LANG_ENGLISH} "Run at Windows startup"
LangString SecStartupName ${LANG_JAPANESE} "Windows 起動時に実行"
LangString DotNetMissing ${LANG_ENGLISH} "The .NET 10 Desktop Runtime is required but was not found.$\r$\n$\r$\nOpen the download page now?"
LangString DotNetMissing ${LANG_JAPANESE} ".NET 10 デスクトップ ランタイムが必要ですが、見つかりませんでした。$\r$\n$\r$\nダウンロードページを開きますか？"
LangString ClipsNotice ${LANG_ENGLISH} "Note: turn OFF Discord's Clips feature (Settings > Clips).$\r$\nWhile Clips is on, Discord is always capturing the screen and the red border stays on."
LangString ClipsNotice ${LANG_JAPANESE} "注意: Discord のクリップ機能（設定 > クリップ）をオフにしてください。$\r$\nオンのままだと Discord が常に画面をキャプチャするため、赤枠が出たままになります。"

Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

; インストール中・上書き前に、起動中のアプリを終了させる。
!macro CloseRunningApp
  nsExec::Exec 'taskkill /F /IM "${APP_EXE}"'
  Pop $0
  Sleep 500
!macroend

Function CheckDesktopRuntime
  StrCpy $0 0
  loop:
    EnumRegValue $1 HKLM "${DESKTOP_RUNTIME_KEY}" $0
    StrCmp $1 "" missing
    StrCpy $2 $1 3
    StrCmp $2 "10." found
    IntOp $0 $0 + 1
    Goto loop
  missing:
    MessageBox MB_YESNO|MB_ICONINFORMATION "$(DotNetMissing)" /SD IDNO IDNO found
    ExecShell "open" "${DOTNET_DOWNLOAD_URL}"
  found:
FunctionEnd

Section "!$(SecAppName)" SecApp
  SectionIn RO
  !insertmacro CloseRunningApp

  SetOutPath "$INSTDIR"
  File "${PUBLISH_DIR}\${APP_EXE}"
  File "..\LICENSE"
  File "..\README.md"

  CreateShortcut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "tallcat4"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1

  Call CheckDesktopRuntime
  MessageBox MB_OK|MB_ICONINFORMATION "$(ClipsNotice)" /SD IDOK
SectionEnd

Section "$(SecStartupName)" SecStartup
  WriteRegStr HKCU "${RUN_KEY}" "${APP_ID}" '"$INSTDIR\${APP_EXE}"'
SectionEnd

Function un.onInit
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd

Section "Uninstall"
  !insertmacro CloseRunningApp

  Delete "$INSTDIR\${APP_EXE}"
  Delete "$INSTDIR\LICENSE"
  Delete "$INSTDIR\README.md"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\${APP_NAME}.lnk"

  DeleteRegValue HKCU "${RUN_KEY}" "${APP_ID}"
  DeleteRegKey HKCU "${SETTINGS_KEY}"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
