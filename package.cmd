@echo off
rem Builds dist\SpaceLens.zip, the file to send to someone: SpaceLens.exe (self-contained, so they don't need .NET),
rem "Install SpaceLens.cmd" (installs it for their Windows user with desktop and Start menu shortcuts) and a README.
rem Needs the .NET 9 SDK or, if that isn't installed, Docker, like run.cmd.

setlocal
cd /d "%~dp0"
set "STAGE=artifacts\package\SpaceLens"
set "ZIP=dist\SpaceLens.zip"

if exist "%STAGE%" rmdir /s /q "%STAGE%"
mkdir "%STAGE%"

dotnet --list-sdks 2>nul | findstr /b "9." >nul
if not errorlevel 1 goto build_dotnet

where docker >nul 2>&1
if not errorlevel 1 goto build_docker

echo Packaging SpaceLens needs one of:
echo   - the .NET 9 SDK:  https://dotnet.microsoft.com/download/dotnet/9.0
echo   - Docker Desktop:  https://www.docker.com/products/docker-desktop/
goto failed

:build_dotnet
echo Building SpaceLens with the .NET SDK...
dotnet publish src\SpaceLens\SpaceLens.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=none -o "%STAGE%" -nologo -v q
if errorlevel 1 goto failed
goto package

:build_docker
echo Building SpaceLens in Docker...
docker build --output "%STAGE%" .
if errorlevel 1 goto failed

:package
del /q "%STAGE%\*.pdb" 2>nul
copy /y "packaging\Install SpaceLens.cmd" "%STAGE%\" >nul
copy /y "packaging\README.txt" "%STAGE%\" >nul
if not exist dist mkdir dist
powershell -NoProfile -Command "Compress-Archive -Path '%STAGE%\*' -DestinationPath '%ZIP%' -Force"
if errorlevel 1 goto failed

echo.
echo Created %ZIP%
echo Send it to anyone with Windows 10 or 11 (64-bit). They unzip it and double-click "Install SpaceLens.cmd".
exit /b 0

:failed
echo.
pause
exit /b 1
