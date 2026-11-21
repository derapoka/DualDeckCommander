@echo off
cd /d "%~dp0src"
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o ..\publish
if errorlevel 1 (pause & exit /b 1)
echo.
echo Kesz: publish\DualDeckCommander.exe
pause
