@echo off
rem Usage: publish.cmd [version]
setlocal

set VERSION=%~1
if "%VERSION%"=="" set VERSION=0.0.0-dev

for %%r in (linux-x64 win-x64) do (
    if exist publish\%%r rmdir /s /q publish\%%r
    dotnet publish DevTools\DevTools.csproj -c Release -r %%r --self-contained false -p:PublishSingleFile=true -p:DebugType=none -p:Version=%VERSION% -o publish\%%r || exit /b 1
)
