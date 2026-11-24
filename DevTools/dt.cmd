@echo off
rem Runs devtools, then loads the selected tools into the calling console when devtools changed its state.
"%~dp0devtools.exe" %*
set DT_RESULT=%ERRORLEVEL%
set "DT_NEWSTATE="
if exist "%~dp0cache\state" set /p DT_NEWSTATE=<"%~dp0cache\state"
if not "%DT_NEWSTATE%"=="%DT_STATE%" call "%~dp0init.cmd"
set DT_NEWSTATE=& set DT_RESULT=& exit /b %DT_RESULT%
