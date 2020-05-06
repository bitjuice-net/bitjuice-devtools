@echo off
set PATH=%PATH%;%~dp0
set BASEPATH=%PATH%
call %~dp0\..\dt.exe path > "%~dp0\..\path.txt"
set /p DEVPATH=<"%~dp0\..\path.txt"
set PATH=%BASEPATH%;%DEVPATH%