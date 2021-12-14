@echo off

if defined DT_INITIALIZED (
	goto exit
)

set PATH=%PATH%;%~dp0
set DT_BASEPATH=%PATH%

set DT_SETUP_FILENAME=%~dp0..\setup.txt
call %~dp0\..\devtools.exe setup > "%DT_SETUP_FILENAME%"

for /f "tokens=*" %%i in (%DT_SETUP_FILENAME%) do set %%i
set PATH=%DT_BASEPATH%;%DT_PATH%
set DT_INITIALIZED=true

:exit