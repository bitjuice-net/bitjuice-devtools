@echo off

set PATH_FILENAME=%~dp0..\path.txt
set ENVS_FILENAME=%~dp0..\envs.txt

call %~dp0\..\devtools.exe %*
call %~dp0\..\devtools.exe path > "%PATH_FILENAME%"
call %~dp0\..\devtools.exe envs > "%ENVS_FILENAME%"

set /p DEVPATH=<"%PATH_FILENAME%"

set PATH=%BASEPATH%;%DEVPATH%
for /f "tokens=*" %%i in (%ENVS_FILENAME%) do set %%i

set PATH_FILENAME=
set ENVS_FILENAME=
set DEVPATH=