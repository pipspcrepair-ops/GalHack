@echo off
rem Double-click to build and open the asset viewer: every creature, place, block, item, weather and more. Your notes are saved to G:\GalHack\asset-notes\asset-notes.json.
title GalHack asset viewer
cd /d "G:\GalHack-wt\asset-viewer"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\tools\godot.ps1" -Project prototypes/asset-viewer -Play
if errorlevel 1 pause
