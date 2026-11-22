@echo off
rem Starts the Instinct screen. The browser opens by itself. Press Ctrl+C in this window to stop.
cd /d "%~dp0"
set "DOTNET=dotnet"
where dotnet >nul 2>nul || set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
"%DOTNET%" run --project src\Instinct.Web -c Release
pause
