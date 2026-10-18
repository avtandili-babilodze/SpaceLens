@echo off
rem Starts the newest SpaceLens: builds it first when it hasn't been built yet or the source
rem changed since the last build, with the .NET 9 SDK or, if that isn't installed, with Docker.
rem
rem   run.cmd              start SpaceLens (build only if needed)
rem   run.cmd --rebuild    build again even if nothing changed, then start
rem   run.cmd --scan C:\   any other options are passed to SpaceLens

setlocal
cd /d "%~dp0"
set "OUT=artifacts\publish\win-x64"
set "EXE=%OUT%\SpaceLens.exe"
rem Touched after every successful build; a source file newer than this means the build is out of date.
set "STAMP=artifacts\publish\last-build.stamp"
set "ARGS=%*"

if /i "%~1"=="--rebuild" (
    set "ARGS="
    goto build
)
if not exist "%EXE%" goto build
if not exist "%STAMP%" goto build
powershell -NoProfile -Command ^
    "$built = (Get-Item '%STAMP%').LastWriteTime;" ^
    "$changed = Get-ChildItem src, Directory.Build.props, global.json -Recurse -File |" ^
    "    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTime -gt $built } | Select-Object -First 1;" ^
    "exit [int][bool]$changed"
if not errorlevel 1 goto run
echo The source changed since the last build.

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
goto built

:build_docker
echo Building SpaceLens in Docker (first time downloads the .NET SDK image, a few minutes)...
docker build --output "%OUT%" .
if errorlevel 1 goto failed

:built
type nul > "%STAMP%"

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
