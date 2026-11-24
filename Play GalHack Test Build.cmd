@echo off
rem Double-click to build and play the GalHack test build. The first run offers to download Godot and the .NET SDK into this folder's .tools.
title GalHack test build
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\setup-tools.ps1" && echo Building GalHack. The first build takes a few minutes, then the game opens. && powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\godot.ps1" -Project prototypes/slice -Play && exit /b 0
echo.
echo The test build did not start. Send a screenshot of this window to whoever gave you the build.
pause
