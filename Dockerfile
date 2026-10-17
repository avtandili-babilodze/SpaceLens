# syntax=docker/dockerfile:1
#
# Reproducible build of SpaceLens in a container.
#
# SpaceLens is a WPF desktop app: it needs the Windows desktop to run, so it can't run inside a
# container. This image compiles the whole solution (app, core library and tests) on Linux with the
# .NET 9 SDK and exports the self-contained Windows executable:
#
#   docker build --output artifacts/docker .
#
# The result is artifacts/docker/SpaceLens.exe, the same single-file build as `dotnet publish` on
# Windows. Run the tests on Windows with `dotnet test SpaceLens.sln` (they use DPAPI and the file system).

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:9.0

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

# WPF reference assemblies are only resolved on Linux when Windows targeting is enabled.
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    EnableWindowsTargeting=true

# Restore first, from project files only, so the package layer is cached until a project changes.
COPY Directory.Build.props .editorconfig SpaceLens.sln ./
COPY src/SpaceLens.Core/SpaceLens.Core.csproj src/SpaceLens.Core/
COPY src/SpaceLens/SpaceLens.csproj src/SpaceLens/
COPY tests/SpaceLens.Tests/SpaceLens.Tests.csproj tests/SpaceLens.Tests/
RUN dotnet restore SpaceLens.sln \
 && dotnet restore src/SpaceLens/SpaceLens.csproj -r win-x64

COPY src/ src/
COPY tests/ tests/

# Compile everything with the same rules as on Windows (analyzers on, warnings are errors).
RUN dotnet build SpaceLens.sln -c Release --no-restore

RUN dotnet publish src/SpaceLens/SpaceLens.csproj \
        -c Release \
        -r win-x64 \
        --self-contained true \
        --no-restore \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -o /out

# Only the published files; `docker build --output <folder>` writes them to the host.
FROM scratch AS export
COPY --from=build /out/ /
