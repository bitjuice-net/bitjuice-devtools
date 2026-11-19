@echo off
rem Runs devtools, then reloads PATH and variables into the calling console when devtools changed its state.
"%~dp0devtools.exe" %*
set DT_RESULT=%ERRORLEVEL%
set "DT_NEWSTATE="
if exist "%~dp0cache\state" set /p DT_NEWSTATE=<"%~dp0cache\state"
if not "%DT_NEWSTATE%"=="%DT_STATE%" for /f "delims=" %%i in ('""%~dp0devtools.exe" shell env cmd"') do %%i
set DT_NEWSTATE=& set DT_RESULT=& exit /b %DT_RESULT%
