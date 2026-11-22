@echo off
rem Build the Godot client and start it. Extra arguments go to the game: play-game.cmd -- --seed 7
cd /d "%~dp0"
"C:\Program Files\dotnet\dotnet.exe" build game\Instinct.Game.csproj -v q --nologo
if errorlevel 1 (pause & exit /b 1)
set GODOT=
for /d %%D in ("%LOCALAPPDATA%\Microsoft\WinGet\Packages\GodotEngine.GodotEngine.Mono*") do for /d %%E in ("%%D\Godot_v*") do for %%F in ("%%E\Godot_v*_mono_win64.exe") do set GODOT=%%F
if not defined GODOT (echo Godot 4 .NET was not found. Install it: winget install GodotEngine.GodotEngine.Mono & pause & exit /b 1)
start "" "%GODOT%" --path game %*
