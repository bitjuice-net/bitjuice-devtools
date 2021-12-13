@echo off

call %~dp0\..\devtools.exe %*
call %~dp0\..\devtools.exe setup > "%DT_SETUP_FILENAME%"

for /f "tokens=*" %%i in (%DT_SETUP_FILENAME%) do set %%i
set PATH=%DT_BASEPATH%;%DT_PATH%