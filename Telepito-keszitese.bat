@echo off
setlocal EnableDelayedExpansion
cd /d "%~dp0"
title Dual Deck Commander - telepito keszitese
echo.
echo   ===========================================================
echo     Dual Deck Commander  -  telepito keszitese
echo   ===========================================================
echo.
echo   Ez a parancsfajl mindent elvegez: ha kell, telepiti a szukseges
echo   ingyenes eszkozoket (.NET 8 SDK, NSIS), leforditja a programot,
echo   es elkesziti a telepitot a "dist" mappaba.
echo.

rem ---- winget ellenorzese ---------------------------------------------------
where winget >nul 2>nul
if errorlevel 1 (
  echo [!] A "winget" nem talalhato. Frissitsd a Windowst, vagy telepitsd kezzel:
  echo     .NET 8 SDK:  https://dotnet.microsoft.com/download/dotnet/8.0
  echo     NSIS:        https://nsis.sourceforge.io/Download
  echo.
)

rem ---- .NET 8 SDK -----------------------------------------------------------------
set "HAS_SDK="
for /f "tokens=1" %%v in ('dotnet --list-sdks 2^>nul') do (
  set "V=%%v"
  if "!V:~0,2!"=="8." set "HAS_SDK=1"
)
if not defined HAS_SDK (
  echo [1/4] .NET 8 SDK telepitese...
  winget install --id Microsoft.DotNet.SDK.8 -e --accept-source-agreements --accept-package-agreements
  set "PATH=%ProgramFiles%\dotnet;%PATH%"
) else (
  echo [1/4] .NET 8 SDK: rendben
)

rem ---- NSIS -------------------------------------------------------------------------
call :findnsis
if not defined MAKENSIS (
  echo [2/4] NSIS telepitese...
  winget install --id NSIS.NSIS -e --accept-source-agreements --accept-package-agreements
  call :findnsis
)
if not defined MAKENSIS (
  echo.
  echo [!] Az NSIS nem talalhato. Telepitsd innen, majd futtasd ujra ezt a fajlt:
  echo     https://nsis.sourceforge.io/Download
  goto :error
)
echo [2/4] NSIS: rendben

rem ---- program forditasa -----------------------------------------------------------
echo [3/4] Program forditasa (onallo exe, a celgepen nem kell hozza .NET)...
dotnet publish src\DualDeckCommander.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish-installer
if errorlevel 1 goto :error

rem ---- telepito ---------------------------------------------------------------------
echo [4/4] Telepito keszitese...
if not exist dist mkdir dist
"%MAKENSIS%" /V2 installer\DualDeckCommander.nsi
if errorlevel 1 goto :error

echo.
echo   ===========================================================
echo     KESZ!  dist\DualDeckCommander-0.6.0-telepito.exe
echo   ===========================================================
echo.
explorer dist
pause
exit /b 0

:findnsis
set "MAKENSIS="
if exist "%ProgramFiles(x86)%\NSIS\makensis.exe" set "MAKENSIS=%ProgramFiles(x86)%\NSIS\makensis.exe"
if exist "%ProgramFiles%\NSIS\makensis.exe" set "MAKENSIS=%ProgramFiles%\NSIS\makensis.exe"
exit /b 0

:error
echo.
echo [!] Hiba tortent - lasd a fenti uzeneteket. Ha nem egyertelmu, kuldd el a kepernyokepet.
pause
exit /b 1
