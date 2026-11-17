@echo off
rem Runs devtools, then reloads PATH and variables into the calling console.
"%~dp0devtools.exe" %*
set DT_RESULT=%ERRORLEVEL%
for /f "delims=" %%i in ('""%~dp0devtools.exe" shell env cmd"') do %%i
set DT_RESULT=& exit /b %DT_RESULT%
