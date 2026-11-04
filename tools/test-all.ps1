# Builds every prototype and runs all of its headless test scenes. Exits non-zero if any fail.
# A headless test scene is any prototypes/<name>/tests/*Test.tscn or *Probe.tscn. A matching
# *.args file beside a scene adds Godot arguments for that scene only.
#   .\tools\test-all.ps1

. "$PSScriptRoot\env.ps1"
$failed = @()
$ran = 0

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
        # Optional extra Godot arguments for one scene, e.g. tests/SoakTest.args holding "--fixed-fps 60".
        $argsFile = [IO.Path]::ChangeExtension($scene.FullName, ".args")
        $extra = @()
        if (Test-Path $argsFile) { $extra = (Get-Content $argsFile -Raw).Trim() -split '\s+' }
        $out = & $GodotConsole --headless @extra --path $proto.FullName $res 2>&1
        $code = $LASTEXITCODE
        $ran++
        $summary = ($out | Select-String -Pattern 'ALL PASSED|FAIL' | Select-Object -Last 1)
        if ($code) {
            Write-Host "FAIL  $($proto.Name) $res (exit $code)"
            $out | Select-String -Pattern 'FAIL' | Select-Object -First 10 | ForEach-Object { Write-Host "      $_" }
            $failed += "$($proto.Name) $res"
        } else {
            Write-Host "PASS  $($proto.Name) $res  $summary"
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
