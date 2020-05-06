@echo off
dt.exe --variant %* --save --update 
set /p DEVPATH=<"%~dp0\..\path.txt"
set PATH=%BASEPATH%;%DEVPATH%