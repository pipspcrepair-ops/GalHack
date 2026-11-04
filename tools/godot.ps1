# Builds a Godot project and opens it in the editor, or plays it with -Play.
#   .\tools\godot.ps1                       # open prototypes/walkable-ship in the editor
#   .\tools\godot.ps1 -Play                 # play it
#   .\tools\godot.ps1 -Project prototypes/other -Play

param(
    [string]$Project = "prototypes/walkable-ship",
    [switch]$Play
)

. "$PSScriptRoot\env.ps1"
$path = Join-Path $root $Project
$csproj = Get-ChildItem -Path $path -Filter *.csproj | Select-Object -First 1
if ($csproj) {
    dotnet build $csproj.FullName -nologo -v q
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

if ($Play) { & $GodotEditor --path $path }
else { & $GodotEditor --editor --path $path }
