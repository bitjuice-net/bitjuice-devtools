@rem Loads the selected tools into this console: PATH and the variables of cache\env.cmd. AutoRun runs it in every
@rem new console, it can also be run by hand, such as in a console started with cmd /d. dt.cmd runs it after changes.
@rem No echo off, AutoRun runs it in the interactive console. A missing or outdated env.cmd is written by devtools first.
@set "DT_INITIALIZED=1"
@if not exist "%~dp0cache\env.cmd" "%~dp0devtools.exe" shell env cmd >nul
@if exist "%~dp0cache\env.cmd" call "%~dp0cache\env.cmd"
@set "DT_NEWSTATE="
@if exist "%~dp0cache\state" set /p DT_NEWSTATE=<"%~dp0cache\state"
@if not "%DT_NEWSTATE%"=="%DT_STATE%" ("%~dp0devtools.exe" shell env cmd >nul & call "%~dp0cache\env.cmd")
@set "DT_NEWSTATE="
