@echo off
title Guardian Parent Web Server
echo ========================================================
echo         GUARDIAN PARENT WEB SERVER HOST
echo ========================================================
echo.
echo Starting Guardian Web Dashboard server...
echo.
cd /d "%~dp0Guardian.Server"
dotnet run --configuration Release
pause
