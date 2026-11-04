# Manual Steam co-op test for prototypes/coop. You run this yourself: it uses YOUR Steam account,
# as app 480 (Valve's shared "Spacewar" test app). See prototypes/coop/README.md, "Test it over Steam".
#
#   .\tools\steam-coop-test.ps1 -FetchSteamApi     # once: fetch and verify Valve's steam_api64.dll
#   .\tools\steam-coop-test.ps1 -Smoke             # one-machine smoke test; nothing is sent to anyone
#   .\tools\steam-coop-test.ps1                    # title screen: Host (Steam) / Join a friend (Steam)
#   .\tools\steam-coop-test.ps1 -HostGame          # host a friends-only lobby straight away
#   .\tools\steam-coop-test.ps1 -Lobby <id>        # join a lobby straight away
#   -Name Pip                                      # your in-game name (default: your Steam name)

param(
    [switch]$FetchSteamApi,
    [switch]$Smoke,
    [switch]$HostGame,
    [string]$Lobby,
    [string]$Name
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\env.ps1"
$project = Join-Path $root "prototypes\coop"
$dll = Join-Path $project "steam_api64.dll"

# Valve's redistributable, exactly as shipped in the Steamworks.NET 2024.8.0 release (Steamworks SDK 1.60).
$SteamApiUrl = "https://raw.githubusercontent.com/rlabrecque/Steamworks.NET/2024.8.0/com.rlabrecque.steamworks.net/Plugins/steam_api64.dll"
$SteamApiSize = 300392
$SteamApiGitBlob = "9ad453cf6eae264db1b0f2aeded287ebc96afc19"

function Get-GitBlobSha1([string]$path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($bytes.Length)`0")
    $all = New-Object byte[] ($header.Length + $bytes.Length)
    [Array]::Copy($header, $all, $header.Length)
    [Array]::Copy($bytes, 0, $all, $header.Length, $bytes.Length)
    $sha = [Security.Cryptography.SHA1]::Create()
    -join ($sha.ComputeHash($all) | ForEach-Object { $_.ToString("x2") })
}

# True if the file is byte-for-byte the pinned redistributable and carries a valid Valve signature.
function Test-SteamApi([string]$path) {
    if (-not (Test-Path $path)) { return $false }
    $size = (Get-Item $path).Length
    $blob = Get-GitBlobSha1 $path
    $signature = Get-AuthenticodeSignature $path
    $signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { "(unsigned)" }
    Write-Host "  size $size bytes, git blob $blob"
    Write-Host "  signature: $($signature.Status), $signer"
    $ok = $size -eq $SteamApiSize -and $blob -eq $SteamApiGitBlob -and $signature.Status -eq "Valid" -and $signer -match "Valve"
    if ($ok) { Write-Host "  OK: Valve's signed steam_api64.dll, the exact Steamworks SDK 1.60 redistributable." -ForegroundColor Green }
    else { Write-Host "  NOT the expected redistributable." -ForegroundColor Red }
    return $ok
}

if ($FetchSteamApi) {
    $download = Join-Path $env:TEMP "steam_api64.download"
    Write-Host "Fetching steam_api64.dll ($SteamApiSize bytes) from the Steamworks.NET 2024.8.0 tag..."
    Invoke-WebRequest -Uri $SteamApiUrl -OutFile $download -UseBasicParsing
    if (-not (Test-SteamApi $download)) {
        Remove-Item $download
        throw "The download didn't match the pinned redistributable; nothing was installed."
    }
    Move-Item $download $dll -Force
    Write-Host "Installed $dll (git-ignored; never committed)."
    if (-not $Smoke -and -not $HostGame -and -not $Lobby) { exit 0 }
}

# ---- Preflight -------------------------------------------------------------------------------
Write-Host "Checking steam_api64.dll..."
if (-not (Test-SteamApi $dll)) { throw "Run .\tools\steam-coop-test.ps1 -FetchSteamApi first." }
$appId = (Get-Content (Join-Path $project "steam_appid.txt") -Raw).Trim()
if ($appId -ne "480") { throw "prototypes/coop/steam_appid.txt should say 480 (Spacewar), not '$appId'." }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
    Write-Host "Steam isn't running. Start Steam and log in first (this script never starts it for you)." -ForegroundColor Yellow
    exit 1
}

# From here on, native tools write to stderr (Steam does); that is output, not a failure.
$ErrorActionPreference = "Continue"
dotnet build (Join-Path $project "Coop.csproj") -nologo -v q
if ($LASTEXITCODE) { exit $LASTEXITCODE }

if ($Smoke) {
    & $GodotConsole --headless --path $project res://tests/SteamSmokeTest.tscn
    exit $LASTEXITCODE
}

# ---- Play with a friend ----------------------------------------------------------------------
$gameArgs = @()
if ($Name) { $gameArgs += "--name=$Name" }
if ($HostGame) { $gameArgs += "--steam-host" }
elseif ($Lobby) { $gameArgs += "--steam-join=$Lobby" }

Write-Host ""
Write-Host "What to check with your friend (both of you need this same commit):" -ForegroundColor Cyan
Write-Host "  1. Host: Host (Steam), then press I in game to invite your friend from the Steam overlay."
Write-Host "     Friend: with the game already open, accept the invite (or Join a friend (Steam) and pick you)."
Write-Host "  2. Both of you appear in the player list (top right) with a ping; you see each other's avatars."
Write-Host "  3. One sits in the pilot seat (F at the front) and flies; the other walks around inside. Press T for"
Write-Host "     stunts: the passenger must stay glued to the deck, and look smooth to the pilot (V: third person)."
Write-Host "  4. G toggles gravity for both; crates float the same on both screens."
Write-Host "  5. F6 sends you to the moon zone: the other player and the desert ships vanish for you, and back again."
Write-Host "  6. Friend presses F10 (leave) and rejoins: their old avatar is gone, the world is current."
Write-Host "  7. Host presses F10: the friend's game ends with 'The host ended the session'."
Write-Host ""
& $GodotConsole --path $project -- @gameArgs
