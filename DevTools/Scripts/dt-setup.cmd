@echo off
set PATH=%PATH%;%~dp0;%~dp0..
set BASEPATH=%PATH%
set /p DEVPATH=<"%~dp0\..\path.txt"
set PATH=%BASEPATH%;%DEVPATH%