@echo off
call %~dp0\..\devtools.exe %*
call %~dp0\..\devtools.exe path > "%~dp0\..\path.txt"
set /p DEVPATH=<"%~dp0\..\path.txt"
set PATH=%BASEPATH%;%DEVPATH%