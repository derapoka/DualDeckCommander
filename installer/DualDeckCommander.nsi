; =============================================================================
;  Dual Deck Commander – telepítő (NSIS 3)
;  Fordítás: a projekt gyökerében a "Telepito-keszitese.bat", vagy:
;      makensis installer\DualDeckCommander.nsi
; =============================================================================

Unicode true
ManifestDPIAware true
SetCompressor /SOLID lzma

!define APPNAME   "Dual Deck Commander"
!define EXENAME   "DualDeckCommander.exe"
!define PUBLISHER "Baranyi László"
!define REGKEY    "Software\DualDeckCommander"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\DualDeckCommander"
!define PROGID    "DualDeckCommander.Image"
!ifndef VERSION
  !define VERSION "0.6.0"
!endif
!ifndef SOURCEDIR
  !define SOURCEDIR "..\publish-installer"
!endif
!ifndef OUTFILE
  !define OUTFILE "..\dist\DualDeckCommander-${VERSION}-telepito.exe"
!endif

!include "MUI2.nsh"
!include "x64.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

Name "${APPNAME}"
Caption "${APPNAME} ${VERSION} – Telepítés"
UninstallCaption "${APPNAME} – Eltávolítás"
OutFile "${OUTFILE}"
InstallDir "$PROGRAMFILES64\${APPNAME}"
InstallDirRegKey HKLM "${REGKEY}" "InstallDir"
RequestExecutionLevel admin
BrandingText "${APPNAME} ${VERSION}  •  ${PUBLISHER}"
ShowInstDetails show
ShowUninstDetails show

; ---- Megjelenés --------------------------------------------------------------
!define MUI_ICON   "..\src\Assets\app.ico"
!define MUI_UNICON "..\src\Assets\app.ico"
!define MUI_WELCOMEFINISHPAGE_BITMAP   "wizard.bmp"
!define MUI_UNWELCOMEFINISHPAGE_BITMAP "wizard.bmp"
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_RIGHT
!define MUI_HEADERIMAGE_BITMAP   "header.bmp"
!define MUI_HEADERIMAGE_UNBITMAP "header.bmp"
!define MUI_ABORTWARNING
!define MUI_ABORTWARNING_TEXT "Biztosan megszakítod a Dual Deck Commander telepítését?"
!define MUI_COMPONENTSPAGE_SMALLDESC

; ---- Telepítő oldalak ----------------------------------------------------------
!define MUI_WELCOMEPAGE_TITLE "Üdvözöl a Dual Deck Commander telepítője"
!define MUI_WELCOMEPAGE_TEXT "Ez a varázsló végigvezet a Dual Deck Commander ${VERSION} telepítésén.$\r$\n$\r$\nA Dual Deck Commander ingyenes, kétpaneles fájlkezelő emulátorokhoz és lemezképekhez: Xbox 360 lemezképek, Amiga lemezképek (ADF, DMS, HDF) és archívumok (ZIP, RAR, 7z, LHA, LZX) közvetlen böngészésével.$\r$\n$\r$\nA folytatáshoz kattints a Tovább gombra."
!insertmacro MUI_PAGE_WELCOME

!define MUI_LICENSEPAGE_TEXT_TOP "Kérlek, olvasd el a felhasználási feltételeket és a jogi nyilatkozatot."
!insertmacro MUI_PAGE_LICENSE "license.rtf"

!define MUI_COMPONENTSPAGE_TEXT_TOP "Válaszd ki, mit telepítsen a varázsló."
!insertmacro MUI_PAGE_COMPONENTS

!define MUI_DIRECTORYPAGE_TEXT_TOP "A Dual Deck Commander a következő mappába kerül. Másik mappához (vagy másik meghajtóhoz) kattints a Tallózás gombra."
!insertmacro MUI_PAGE_DIRECTORY

Var StartMenuFolder
!define MUI_STARTMENUPAGE_REGISTRY_ROOT "HKLM"
!define MUI_STARTMENUPAGE_REGISTRY_KEY "${REGKEY}"
!define MUI_STARTMENUPAGE_REGISTRY_VALUENAME "StartMenuFolder"
!define MUI_STARTMENUPAGE_DEFAULTFOLDER "${APPNAME}"
!insertmacro MUI_PAGE_STARTMENU Application $StartMenuFolder

!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_TITLE "A telepítés befejeződött"
!define MUI_FINISHPAGE_TEXT "A Dual Deck Commander telepítése sikeres volt.$\r$\n$\r$\nAz asztalon és a Start menüben található parancsikonnal indíthatod."
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXENAME}"
!define MUI_FINISHPAGE_RUN_TEXT "A Dual Deck Commander indítása most"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\Licenc.txt"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "A licencfeltételek megjelenítése"
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED
!insertmacro MUI_PAGE_FINISH

; ---- Eltávolító oldalak --------------------------------------------------------
!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "Hungarian"

; ---- Fájl-tulajdonságok (jobb klikk → Tulajdonságok → Részletek) -----------------
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "CompanyName" "${PUBLISHER}"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "FileDescription" "${APPNAME} telepítő"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=${LANG_HUNGARIAN} "LegalCopyright" "Freeware – ${PUBLISHER}"

; ---- Fájltársítás makrók -----------------------------------------------------------
!macro AssocAdd EXT
  WriteRegStr HKLM "Software\Classes\${EXT}\OpenWithProgids" "${PROGID}" ""
  WriteRegStr HKLM "Software\Classes\${EXT}" "" "${PROGID}"
  WriteRegStr HKLM "Software\Classes\Applications\${EXENAME}\SupportedTypes" "${EXT}" ""
!macroend

!macro AssocRemove EXT
  DeleteRegValue HKLM "Software\Classes\${EXT}\OpenWithProgids" "${PROGID}"
  DeleteRegKey /ifempty HKLM "Software\Classes\${EXT}\OpenWithProgids"
  ReadRegStr $0 HKLM "Software\Classes\${EXT}" ""
  ${If} $0 == "${PROGID}"
    DeleteRegValue HKLM "Software\Classes\${EXT}" ""
  ${EndIf}
  DeleteRegKey /ifempty HKLM "Software\Classes\${EXT}"
!macroend

; =============================================================================
;  Összetevők
; =============================================================================

Section "!Dual Deck Commander (program)" SecMain
  SectionIn RO
  SetShellVarContext all
  SetOutPath "$INSTDIR"
  SetOverwrite on

  DetailPrint "Programfájlok másolása..."
  File "${SOURCEDIR}\${EXENAME}"
  File "/oname=Licenc.txt" "license.txt"
  WriteUninstaller "$INSTDIR\Eltavolitas.exe"

  ; telepítési adatok
  WriteRegStr HKLM "${REGKEY}" "InstallDir" "$INSTDIR"
  WriteRegStr HKLM "${REGKEY}" "Version" "${VERSION}"

  ; Programok és szolgáltatások (Vezérlőpult / Gépház → Alkalmazások)
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr   HKLM "${UNINSTKEY}" "Publisher" "${PUBLISHER}"
  WriteRegStr   HKLM "${UNINSTKEY}" "Comments" "Kétpaneles fájlkezelő emulátorokhoz és lemezképekhez"
  WriteRegStr   HKLM "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXENAME},0"
  WriteRegStr   HKLM "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTKEY}" "UninstallString" "$\"$INSTDIR\Eltavolitas.exe$\""
  WriteRegStr   HKLM "${UNINSTKEY}" "QuietUninstallString" "$\"$INSTDIR\Eltavolitas.exe$\" /S"
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKLM "${UNINSTKEY}" "NoRepair" 1
  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKLM "${UNINSTKEY}" "EstimatedSize" "$0"

  ; Start menü
  !insertmacro MUI_STARTMENU_WRITE_BEGIN Application
    CreateDirectory "$SMPROGRAMS\$StartMenuFolder"
    CreateShortcut "$SMPROGRAMS\$StartMenuFolder\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0 SW_SHOWNORMAL "" "Kétpaneles fájlkezelő emulátorokhoz és lemezképekhez"
    CreateShortcut "$SMPROGRAMS\$StartMenuFolder\${APPNAME} eltávolítása.lnk" "$INSTDIR\Eltavolitas.exe"
  !insertmacro MUI_STARTMENU_WRITE_END

  ; "Megnyitás ezzel..." listához
  WriteRegStr HKLM "Software\Classes\Applications\${EXENAME}" "FriendlyAppName" "${APPNAME}"
  WriteRegStr HKLM "Software\Classes\Applications\${EXENAME}\shell\open\command" "" "$\"$INSTDIR\${EXENAME}$\" $\"%1$\""
SectionEnd

Section "Asztali ikon" SecDesktop
  SetShellVarContext all
  CreateShortcut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\${EXENAME}" "" "$INSTDIR\${EXENAME}" 0 SW_SHOWNORMAL "" "Kétpaneles fájlkezelő emulátorokhoz és lemezképekhez"
SectionEnd

Section /o "Fájltársítás: Amiga lemezképek és archívumok" SecAssoc
  WriteRegStr HKLM "Software\Classes\${PROGID}" "" "Amiga lemezkép / archívum"
  WriteRegStr HKLM "Software\Classes\${PROGID}\DefaultIcon" "" "$\"$INSTDIR\${EXENAME}$\",0"
  WriteRegStr HKLM "Software\Classes\${PROGID}\shell\open\command" "" "$\"$INSTDIR\${EXENAME}$\" $\"%1$\""
  !insertmacro AssocAdd ".adf"
  !insertmacro AssocAdd ".adz"
  !insertmacro AssocAdd ".dms"
  !insertmacro AssocAdd ".hdf"
  !insertmacro AssocAdd ".lha"
  !insertmacro AssocAdd ".lzh"
  !insertmacro AssocAdd ".lzx"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'
SectionEnd

!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SecMain}    "A Dual Deck Commander program (kötelező)."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecDesktop} "Parancsikon az asztalon."
  !insertmacro MUI_DESCRIPTION_TEXT ${SecAssoc}   "Dupla kattintásra a Dual Deck Commander nyitja meg az .adf, .adz, .dms, .hdf, .lha, .lzh és .lzx fájlokat."
!insertmacro MUI_FUNCTION_DESCRIPTION_END

; =============================================================================
;  Indítás
; =============================================================================

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK|MB_ICONSTOP "A Dual Deck Commander 64 bites Windowst igényel (Windows 10 vagy újabb)."
    Abort
  ${EndIf}
  SetRegView 64
  ; korábbi telepítés helye
  ReadRegStr $0 HKLM "${REGKEY}" "InstallDir"
  ${If} $0 != ""
    StrCpy $INSTDIR $0
  ${EndIf}
FunctionEnd

; =============================================================================
;  Eltávolítás
; =============================================================================

Function un.onInit
  SetRegView 64
FunctionEnd

Section "Uninstall"
  SetShellVarContext all

  Delete "$INSTDIR\${EXENAME}"
  Delete "$INSTDIR\Licenc.txt"
  Delete "$INSTDIR\Eltavolitas.exe"
  RMDir "$INSTDIR"

  Delete "$DESKTOP\${APPNAME}.lnk"
  !insertmacro MUI_STARTMENU_GETFOLDER Application $StartMenuFolder
  Delete "$SMPROGRAMS\$StartMenuFolder\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\$StartMenuFolder\${APPNAME} eltávolítása.lnk"
  RMDir "$SMPROGRAMS\$StartMenuFolder"

  ; fájltársítások
  !insertmacro AssocRemove ".adf"
  !insertmacro AssocRemove ".adz"
  !insertmacro AssocRemove ".dms"
  !insertmacro AssocRemove ".hdf"
  !insertmacro AssocRemove ".lha"
  !insertmacro AssocRemove ".lzh"
  !insertmacro AssocRemove ".lzx"
  DeleteRegKey HKLM "Software\Classes\${PROGID}"
  DeleteRegKey HKLM "Software\Classes\Applications\${EXENAME}"
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  DeleteRegKey HKLM "${UNINSTKEY}"
  DeleteRegKey HKLM "${REGKEY}"

  ; a felhasználó beállításai (kedvencek, fülek) csak kérésre törlődnek
  MessageBox MB_YESNO|MB_ICONQUESTION "Törlöd a Dual Deck Commander beállításait is (kedvencek, fülek, ablakméret)?" /SD IDNO IDNO keepSettings
    SetShellVarContext current
    RMDir /r "$APPDATA\DualDeckCommander"
    RMDir /r "$TEMP\DualDeckCommander"
  keepSettings:
SectionEnd
