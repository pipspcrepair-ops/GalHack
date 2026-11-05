# Runs one headless test scene again and again while other prototypes' headless tests keep the
# CPU busy in parallel loops, the way a merge gate does with other agents building beside it.
# A test that passes here doesn't depend on timing. Exits non-zero if any run fails.
#   .\tools\test-under-load.ps1                                  # coop NetTest x10 under 4 load loops
#   .\tools\test-under-load.ps1 -Runs 3 -LoadCopies 1
#   .\tools\test-under-load.ps1 -Project prototypes/other -Scene res://tests/OtherTest.tscn

param(
    [string]$Project = "prototypes/coop",
    [string]$Scene = "res://tests/NetTest.tscn",
    [int]$Runs = 10,
    # Each entry is "prototype folder:scene"; every entry gets -LoadCopies loops of its own.
    [string[]]$Load = @("prototypes/wildlife:res://tests/WildlifeTest.tscn", "prototypes/invasions:res://tests/InvasionTest.tscn"),
    [int]$LoadCopies = 2
)

. "$PSScriptRoot\env.ps1"
$ErrorActionPreference = "Continue"
$logs = Join-Path $env:TEMP ("under-load-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
New-Item -ItemType Directory -Force -Path $logs | Out-Null

# Build everything first, so the loops only run tests.
$projects = @($Project) + ($Load | ForEach-Object { $_.Split(":")[0] }) | Select-Object -Unique
foreach ($p in $projects) {
    dotnet build (Join-Path $root $p) -nologo -v q | Out-Null
    if ($LASTEXITCODE) { Write-Host "BUILD FAILED  $p"; exit 1 }
}

# The load: each loop runs its test scene back to back until stopped.
$jobs = @()
foreach ($entry in $Load) {
    $path, $res = $entry.Split(":", 2)
    for ($i = 0; $i -lt $LoadCopies; $i++) {
        $jobs += Start-Job -ArgumentList $GodotConsole, (Join-Path $root $path), $res -ScriptBlock {
            param($godot, $project, $scene)
            while ($true) { & $godot --headless --path $project $scene *> $null }
        }
    }
}
Write-Host "Load: $($jobs.Count) loops ($($Load -join ', '), $LoadCopies each). Logs in $logs"

$failed = 0
try {
    for ($run = 1; $run -le $Runs; $run++) {
        $log = Join-Path $logs "run$run.txt"
        $start = Get-Date
        & $GodotConsole --headless --path (Join-Path $root $Project) $Scene *> $log
        $code = $LASTEXITCODE
        $minutes = ((Get-Date) - $start).TotalMinutes
        $busy = @(Get-Process -Name "Godot*" -ErrorAction SilentlyContinue).Count
        $fails = @(Select-String -Path $log -Pattern "FAIL  " | ForEach-Object { $_.Line -replace '^.*?(FAIL  )', '$1' })
        if ($code -eq 0) { Write-Host ("run {0}: PASS  ({1:F1} min, {2} Godot processes running at the end)" -f $run, $minutes, $busy) }
        else {
            $failed++
            Write-Host ("run {0}: FAIL  exit {1} ({2:F1} min)" -f $run, $code, $minutes)
            $fails | Select-Object -First 8 | ForEach-Object { Write-Host "    $_" }
        }
    }
}
finally {
    $jobs | Stop-Job -PassThru | Remove-Job -Force
    # Stop the load's Godot processes (only those for the load projects in this checkout).
    $loadPaths = $Load | ForEach-Object { (Join-Path $root $_.Split(":")[0]) }
    Get-CimInstance Win32_Process -Filter "Name like 'Godot%'" | Where-Object {
        $cmd = $_.CommandLine; $loadPaths | Where-Object { $cmd -like "*$_*" }
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

Write-Host ("`n{0} of {1} runs passed under load. Logs: {2}" -f ($Runs - $failed), $Runs, $logs)
exit ([int]($failed -gt 0))
