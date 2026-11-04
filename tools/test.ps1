# Builds a prototype and runs its headless test scene. Exits non-zero on failure.
#   .\tools\test.ps1
#   .\tools\test.ps1 -Project prototypes/walkable-ship -Scene res://tests/FrameTest.tscn

param(
    [string]$Project = "prototypes/walkable-ship",
    [string]$Scene = "res://tests/FrameTest.tscn"
)

. "$PSScriptRoot\env.ps1"
$path = Join-Path $root $Project
$csproj = Get-ChildItem -Path $path -Filter *.csproj | Select-Object -First 1
dotnet build $csproj.FullName -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }

& $GodotConsole --headless --path $path $Scene
exit $LASTEXITCODE
