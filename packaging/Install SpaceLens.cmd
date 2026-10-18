@echo off
rem Installs SpaceLens for this Windows user: copies it to %LOCALAPPDATA%\Programs\SpaceLens, adds desktop and
rem Start menu shortcuts and an entry in Settings > Apps, then opens it. No administrator rights are needed.

cd /d "%~dp0"
if not exist "SpaceLens.exe" (
    echo SpaceLens.exe isn't next to this file.
    echo Unzip the whole SpaceLens.zip first ^(right-click it, Extract All^), then run this again.
    echo.
    pause
    exit /b 1
)

echo Installing SpaceLens...
start "" "SpaceLens.exe" --install
