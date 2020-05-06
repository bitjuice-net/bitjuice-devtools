@echo off
reg add "HKEY_CURRENT_USER\Software\Microsoft\Command Processor" /v AutoRun /t REG_SZ /d %~dp0dt-setup.cmd /f