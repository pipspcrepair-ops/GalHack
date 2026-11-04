# Points .NET, NuGet and Godot at the project's local .tools folder so nothing lands on C:.
# Dot-source it:  . .\tools\env.ps1

$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root ".tools"

$env:DOTNET_ROOT = Join-Path $tools "dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"
$env:DOTNET_CLI_HOME = Join-Path $tools "dotnet-home"
$env:NUGET_PACKAGES = Join-Path $tools "nuget\packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $tools "nuget\http-cache"
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $tools "nuget\plugins-cache"
$env:DOTNET_NOLOGO = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:TEMP = Join-Path $tools "tmp"
$env:TMP = $env:TEMP
New-Item -ItemType Directory -Force -Path $env:TEMP | Out-Null

$GodotDir = Join-Path $tools "godot\Godot_v4.7.2-stable_mono_win64"
$GodotEditor = Join-Path $GodotDir "Godot_v4.7.2-stable_mono_win64.exe"
$GodotConsole = Join-Path $GodotDir "Godot_v4.7.2-stable_mono_win64_console.exe"
