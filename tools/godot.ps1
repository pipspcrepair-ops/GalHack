# Builds a Godot project and opens it in the editor, or plays it with -Play.
#   .\tools\godot.ps1                       # open prototypes/slice in the editor
#   .\tools\godot.ps1 -Play                 # play it
#   .\tools\godot.ps1 -Project prototypes/other -Play
# For people at the keyboard (sound on, no queue). Agents run windowed scenes through
# .tools\queue\godot-q.ps1 instead, so the machine never has more than 4 Godot runs (owner, 22 Nov).

param(
    [string]$Project = "prototypes/slice",
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
