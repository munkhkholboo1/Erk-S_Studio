@echo off
rem Drive Erk-S Studio from cmd through its bridge - docs\STU-BRIDGE.md
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0stu.ps1" %*
exit /b %ERRORLEVEL%
