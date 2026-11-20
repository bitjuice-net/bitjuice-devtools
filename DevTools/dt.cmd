@echo off
rem Runs devtools, then loads cache\env.cmd into the calling console when devtools changed its state.
rem A missing or outdated env.cmd is written again by `devtools shell env cmd`.
"%~dp0devtools.exe" %*
set DT_RESULT=%ERRORLEVEL%
set "DT_NEWSTATE="
if exist "%~dp0cache\state" set /p DT_NEWSTATE=<"%~dp0cache\state"
if not "%DT_NEWSTATE%"=="%DT_STATE%" if exist "%~dp0cache\env.cmd" call "%~dp0cache\env.cmd"
if not "%DT_NEWSTATE%"=="%DT_STATE%" ("%~dp0devtools.exe" shell env cmd >nul & call "%~dp0cache\env.cmd")
set DT_NEWSTATE=& set DT_RESULT=& exit /b %DT_RESULT%
