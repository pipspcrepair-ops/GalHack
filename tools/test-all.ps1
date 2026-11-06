# Builds every prototype and runs all of its headless test scenes. Exits non-zero if any fail.
# A headless test scene is any prototypes/<name>/tests/*Test.tscn or *Probe.tscn. A matching
# *.args file beside a scene adds Godot arguments for that scene only; a matching *.manual file
# skips it (its first line says why).
#   .\tools\test-all.ps1

. "$PSScriptRoot\env.ps1"
$failed = @()
$ran = 0

# House rule: no Kenney assets of any kind. Any file whose path names Kenney, or a Kenney credit,
# fails the gate. (Mentions in prose, like "no Kenney audio is used", are fine.)
$kenneyFiles = @(git -C $root ls-files | Where-Object { $_ -match '(?i)kenney' })
$kenneyCredits = @(Select-String -Path (Join-Path $root "CREDITS.md") -Pattern 'kenney' -SimpleMatch -ErrorAction SilentlyContinue)
if ($kenneyFiles -or $kenneyCredits) {
    Write-Host "FAIL  asset rule: Kenney assets found"
    $kenneyFiles | ForEach-Object { Write-Host "      file: $_" }
    $kenneyCredits | ForEach-Object { Write-Host "      CREDITS.md: $($_.Line.Trim())" }
    $failed += "asset rule (Kenney)"
} else {
    Write-Host "PASS  asset rule: no Kenney files or credits"
}

foreach ($proto in Get-ChildItem -Path (Join-Path $root "prototypes") -Directory) {
    $scenes = Get-ChildItem -Path (Join-Path $proto.FullName "tests") -Include *Test.tscn, *Probe.tscn -File -Recurse -ErrorAction SilentlyContinue
    if (-not $scenes) { continue }

    $csproj = Get-ChildItem -Path $proto.FullName -Filter *.csproj | Select-Object -First 1
    dotnet build $csproj.FullName -nologo -v q | Out-Null
    if ($LASTEXITCODE) {
        Write-Host "BUILD FAILED  $($proto.Name)"
        $failed += "$($proto.Name) (build)"
        continue
    }

    foreach ($scene in $scenes) {
        $res = "res://tests/" + $scene.Name
        # A *.manual file beside a scene keeps it out of this run (e.g. tests that need a real Steam login).
        if (Test-Path ([IO.Path]::ChangeExtension($scene.FullName, ".manual"))) {
            Write-Host "SKIP  $($proto.Name) $res  (manual: $((Get-Content ([IO.Path]::ChangeExtension($scene.FullName, '.manual')) -TotalCount 1)))"
            continue
        }
        # Optional extra Godot arguments for one scene, e.g. tests/SoakTest.args holding "--fixed-fps 60".
        $argsFile = [IO.Path]::ChangeExtension($scene.FullName, ".args")
        $extra = @()
        if (Test-Path $argsFile) { $extra = (Get-Content $argsFile -Raw).Trim() -split '\s+' }
        $out = & $GodotConsole --headless @extra --path $proto.FullName $res 2>&1
        $code = $LASTEXITCODE
        $ran++
        $summary = ($out | Select-String -Pattern 'ALL PASSED|FAIL' | Select-Object -Last 1)
        # Random-world tests print "world: seed N, type"; show them so a run can be replayed with --seed.
        $worlds = ($out | Select-String -Pattern '^world: ' | ForEach-Object { $_.Line -replace '^world: ', '' } | Select-Object -Unique) -join '; '
        if ($code) {
            Write-Host "FAIL  $($proto.Name) $res (exit $code)"
            $out | Select-String -Pattern 'FAIL' | Select-Object -First 10 | ForEach-Object { Write-Host "      $_" }
            if ($worlds) { Write-Host "      on: $worlds" }
            $failed += "$($proto.Name) $res"
        } else {
            Write-Host "PASS  $($proto.Name) $res  $summary"
            if ($worlds) { Write-Host "      on: $worlds" }
        }
    }
}

if ($failed) {
    Write-Host "`n$($failed.Count) of $ran test scenes failed:"
    $failed | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "`nAll $ran test scenes passed."
exit 0
