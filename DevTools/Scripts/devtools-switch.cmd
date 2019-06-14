@echo off
devtools.exe --save --update --variant %*
set /p DEVPATH=<"%~dp0\..\path.txt"
set PATH=%BASEPATH%;%DEVPATH%