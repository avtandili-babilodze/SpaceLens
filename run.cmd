@echo off
rem Starts SpaceLens. Builds it first when it hasn't been built yet, with the .NET 9 SDK
rem or, if that isn't installed, with Docker.
rem
rem   run.cmd              start SpaceLens (build only if needed)
rem   run.cmd --rebuild    build again from the current source, then start
rem   run.cmd --scan C:\   any other options are passed to SpaceLens

setlocal
cd /d "%~dp0"
set "OUT=artifacts\publish\win-x64"
set "EXE=%OUT%\SpaceLens.exe"
set "ARGS=%*"

if /i "%~1"=="--rebuild" (
    set "ARGS="
    goto build
)
if exist "%EXE%" goto run

:build
tasklist /fi "imagename eq SpaceLens.exe" 2>nul | find /i "SpaceLens.exe" >nul
if not errorlevel 1 (
    echo SpaceLens is running. Close it first, then run this again.
    goto failed
)

dotnet --list-sdks 2>nul | findstr /b "9." >nul
if not errorlevel 1 goto build_dotnet

where docker >nul 2>&1
if not errorlevel 1 goto build_docker

echo SpaceLens needs to be built once, which requires one of:
echo   - the .NET 9 SDK:  https://dotnet.microsoft.com/download/dotnet/9.0
echo   - Docker Desktop:  https://www.docker.com/products/docker-desktop/
goto failed

:build_dotnet
echo Building SpaceLens with the .NET SDK (first time takes a minute)...
dotnet publish src\SpaceLens\SpaceLens.csproj -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
    -o "%OUT%" -nologo -v q
if errorlevel 1 goto failed
goto run

:build_docker
echo Building SpaceLens in Docker (first time downloads the .NET SDK image, a few minutes)...
docker build --output "%OUT%" .
if errorlevel 1 goto failed
goto run

:run
if not exist "%EXE%" (
    echo The build finished but %EXE% is missing.
    goto failed
)
start "" "%EXE%" %ARGS%
exit /b 0

:failed
echo.
pause
exit /b 1
