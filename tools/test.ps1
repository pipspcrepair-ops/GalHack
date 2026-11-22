# Builds a prototype and runs its headless test scene through the machine-wide queue (at most 4 Godot
# runs at once, owner 22 Nov). Exits non-zero on failure.
#   .\tools\test.ps1
#   .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/StartTest.tscn

param(
    [string]$Project = "prototypes/slice",
    [string]$Scene = "res://tests/StartTest.tscn"
)

. "$PSScriptRoot\env.ps1"
$path = Join-Path $root $Project
$csproj = Get-ChildItem -Path $path -Filter *.csproj | Select-Object -First 1
dotnet build $csproj.FullName -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Set-Location $root   # the queue reads tools\env.ps1 from the current folder
& (Join-Path $tools "queue\godot-q.ps1") -Console -Headless --path $path $Scene
exit $LASTEXITCODE
