@echo off
rem Double-click to build and open the asset viewer on the core components: chairs, thrusters, generators, fabricators and the rest, live. Your notes are saved to G:\GalHack\asset-notes\asset-notes.json.
title GalHack components viewer
cd /d "G:\GalHack-wt\components"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\godot.ps1" -Project prototypes/asset-viewer -Play -GameArgs "--select=Components|component.command-chair"
if errorlevel 1 pause
