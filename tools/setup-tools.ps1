# Makes sure .tools holds Godot 4.7.2 (.NET) and the .NET 10 SDK that tools\godot.ps1 builds and plays with.
# Downloads whatever is missing into this checkout's .tools folder, after asking. Nothing is installed system-wide.
#   .\tools\setup-tools.ps1

. "$PSScriptRoot\env.ps1"
$ErrorActionPreference = "Stop"

$needGodot = -not (Test-Path $GodotEditor)
$needDotnet = -not (Test-Path (Join-Path $env:DOTNET_ROOT "dotnet.exe"))
if (-not ($needGodot -or $needDotnet)) { return }

Write-Host "First run: GalHack needs Godot 4.7.2 (.NET) and the .NET 10 SDK."
Write-Host "They come from the official sites (github.com/godotengine, dot.net): about 420 MB to download, about 1 GB on disk, into $tools"
if ((Read-Host "Download now? [Y/N]") -notmatch '^[Yy]') { exit 1 }

$ProgressPreference = "SilentlyContinue"   # Invoke-WebRequest crawls with its progress bar on
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if ($needGodot) {
    Write-Host "Downloading Godot (116 MB)..."
    $zip = Join-Path $env:TEMP "godot.zip"
    Invoke-WebRequest "https://github.com/godotengine/godot/releases/download/4.7.2-stable/Godot_v4.7.2-stable_mono_win64.zip" -OutFile $zip
    Write-Host "Unpacking Godot..."
    if (Test-Path $GodotDir) { Remove-Item $GodotDir -Recurse -Force }   # left half-unpacked by an earlier try
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($zip, (Join-Path $tools "godot"))
    Remove-Item $zip
    if (-not (Test-Path $GodotEditor)) { throw "Godot unpacked to an unexpected layout: $GodotEditor is missing." }
}

if ($needDotnet) {
    Write-Host "Installing the .NET SDK (300 MB)..."
    $installer = Join-Path $env:TEMP "dotnet-install.ps1"
    Invoke-WebRequest "https://dot.net/v1/dotnet-install.ps1" -OutFile $installer
    & $installer -Version 10.0.403 -InstallDir $env:DOTNET_ROOT -NoPath
    if (-not (Test-Path (Join-Path $env:DOTNET_ROOT "dotnet.exe"))) { throw "The .NET SDK did not install." }
}
