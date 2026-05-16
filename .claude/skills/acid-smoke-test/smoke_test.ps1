<#
.SYNOPSIS
    End-to-end smoke test for the acid hazard.

.DESCRIPTION
    Launches GorelordsBrawler in debug mode with DebugServer + DebugDirectArena +
    DebugFastAcid, polls the HTTP debug server on :7777, and verifies the acid
    lifecycle: starts inactive, activates after the start delay, level rises
    monotonically, and players eventually take damage.

    Also fetches a screenshot at peak so you can eyeball the rendering.

    Exit codes:
        0 = all checks passed
        1 = build failure
        2 = game failed to start / debug server never came up
        3 = lifecycle assertion failed
        4 = HTTP error talking to debug server

.PARAMETER RepoRoot
    Repository root (default: walk up from script until we find GorelordsBrawler.slnx).

.PARAMETER ScreenshotPath
    Where to write the captured PNG (default: $RepoRoot/.smoke-test-screenshot.png).

.PARAMETER NoBuild
    Skip dotnet build (assumes Debug binaries are already current).
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $ScreenshotPath,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

# ── Locate repo root ────────────────────────────────────────────────────────

if (-not $RepoRoot) {
    $dir = $PSScriptRoot
    while ($dir -and -not (Test-Path (Join-Path $dir 'GorelordsBrawler.slnx'))) {
        $parent = Split-Path $dir -Parent
        if ($parent -eq $dir) { break }
        $dir = $parent
    }
    if (-not (Test-Path (Join-Path $dir 'GorelordsBrawler.slnx'))) {
        Write-Error 'Could not locate GorelordsBrawler.slnx — pass -RepoRoot explicitly.'
        exit 2
    }
    $RepoRoot = $dir
}

$ExePath = Join-Path $RepoRoot 'GorelordsBrawler\bin\Debug\net8.0\GorelordsBrawler.exe'
$ExeDir  = Split-Path $ExePath -Parent
if (-not $ScreenshotPath) {
    $ScreenshotPath = Join-Path $RepoRoot '.smoke-test-screenshot.png'
}

# ── Build ───────────────────────────────────────────────────────────────────

if (-not $NoBuild) {
    Write-Host '[smoke] Building GorelordsBrawler (Debug)...' -ForegroundColor Cyan
    & dotnet build (Join-Path $RepoRoot 'GorelordsBrawler\GorelordsBrawler.csproj') -c Debug | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Error '[smoke] Build failed.'
        exit 1
    }
}

if (-not (Test-Path $ExePath)) {
    Write-Error "[smoke] Game exe not found at $ExePath"
    exit 1
}

# ── Write smoke-test appsettings.json (backup existing) ─────────────────────

$SettingsPath = Join-Path $ExeDir 'appsettings.json'
$BackupPath   = "$SettingsPath.smoke-backup"
$BackupMade   = $false
if (Test-Path $SettingsPath) {
    Copy-Item $SettingsPath $BackupPath -Force
    $BackupMade = $true
}
@'
{
  "DebugServer":      true,
  "DebugDirectArena": true,
  "DebugFastAcid":    true
}
'@ | Set-Content -Path $SettingsPath -Encoding UTF8

# ── Launch game and ensure cleanup ──────────────────────────────────────────

$Game = $null
$Failed = $false
$FailCode = 0

function Stop-Game {
    if ($Game -and -not $Game.HasExited) {
        try { $Game.Kill($true) } catch {}
    }
    if ($BackupMade) {
        Move-Item $BackupPath $SettingsPath -Force
    }
    else {
        Remove-Item $SettingsPath -ErrorAction SilentlyContinue
    }
}

try {
    Write-Host '[smoke] Launching game...' -ForegroundColor Cyan
    $psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
    $psi.UseShellExecute  = $false
    $psi.WorkingDirectory = $ExeDir
    $Game = [System.Diagnostics.Process]::Start($psi)

    # ── Wait for debug server ──────────────────────────────────────────────
    Write-Host '[smoke] Waiting for debug server on :7777...' -ForegroundColor Cyan
    $deadline = (Get-Date).AddSeconds(20)
    $serverUp = $false
    while ((Get-Date) -lt $deadline) {
        try {
            $r = Invoke-WebRequest -Uri 'http://localhost:7777/state' -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $serverUp = $true; break }
        } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $serverUp) {
        Write-Error '[smoke] Debug server did not come up within 20s.'
        $Failed = $true; $FailCode = 2
        return
    }

    function Get-State {
        $json = Invoke-RestMethod -Uri 'http://localhost:7777/state' -TimeoutSec 2
        return $json
    }

    function Wait-For {
        param([scriptblock] $Cond, [int] $TimeoutMs, [string] $Description)
        $end = (Get-Date).AddMilliseconds($TimeoutMs)
        while ((Get-Date) -lt $end) {
            $s = Get-State
            if (& $Cond $s) { return $s }
            Start-Sleep -Milliseconds 250
        }
        throw "Timed out waiting for: $Description"
    }

    # ── Assertion 1: acid inactive at start ────────────────────────────────
    Write-Host '[smoke] Check 1: acid inactive at game start' -ForegroundColor Cyan
    $s0 = Get-State
    if ($s0.acidActive) {
        Write-Error "[smoke] FAIL: Acid was already active at startup (time=$($s0.time))."
        $Failed = $true; $FailCode = 3
        return
    }
    Write-Host "        OK (acidActive=false, time=$($s0.time))" -ForegroundColor Green

    # ── Assertion 2: acid activates within ~15s (DebugFastAcid → ~3s) ──────
    Write-Host '[smoke] Check 2: acid activates within 15s' -ForegroundColor Cyan
    $sActive = Wait-For { param($s) $s.acidActive } 15000 'acidActive == true'
    Write-Host "        OK (activated at time=$($sActive.time), level=$($sActive.acidLevel))" -ForegroundColor Green

    # ── Assertion 3: level decreases (rises on screen) over 3 seconds ──────
    Write-Host '[smoke] Check 3: acidLevel decreases over time' -ForegroundColor Cyan
    $sA = Get-State
    Start-Sleep -Seconds 3
    $sB = Get-State
    if ($sB.acidLevel -ge $sA.acidLevel) {
        Write-Error "[smoke] FAIL: acidLevel did not decrease ($($sA.acidLevel) -> $($sB.acidLevel))."
        $Failed = $true; $FailCode = 3
        return
    }
    $delta = $sA.acidLevel - $sB.acidLevel
    Write-Host "        OK (level $($sA.acidLevel) -> $($sB.acidLevel), drop=$delta px in 3s)" -ForegroundColor Green

    # ── Assertion 4: a player eventually takes damage (≤ 45s) ──────────────
    Write-Host '[smoke] Check 4: at least one player takes damage within 45s' -ForegroundColor Cyan
    $sDmg = Wait-For {
        param($s)
        foreach ($p in $s.players) { if ($p.hp -lt $p.maxHp) { return $true } }
        return $false
    } 45000 'any player.hp < player.maxHp'
    $hits = ($sDmg.players | Where-Object { $_.hp -lt $_.maxHp }).Count
    Write-Host "        OK (time=$($sDmg.time), $hits player(s) damaged)" -ForegroundColor Green

    # ── Soak: let the pool fill noticeably before snapping ─────────────────
    Write-Host '[smoke] Soak: letting acid rise for 10 s before screenshot...' -ForegroundColor Cyan
    Start-Sleep -Seconds 10
    $sShot = Get-State
    Write-Host "        Pool level at snapshot: y=$($sShot.acidLevel) (time=$($sShot.time))" -ForegroundColor Gray

    # ── Screenshot for visual eyeballing ───────────────────────────────────
    Write-Host '[smoke] Fetching screenshot...' -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri 'http://localhost:7777/screenshot' -OutFile $ScreenshotPath -TimeoutSec 10
        Write-Host "        OK ($ScreenshotPath)" -ForegroundColor Green
    } catch {
        Write-Warning "[smoke] Screenshot fetch failed: $_"
    }

    Write-Host ''
    Write-Host '[smoke] ALL CHECKS PASSED ✓' -ForegroundColor Green
}
catch {
    Write-Error "[smoke] Unhandled error: $_"
    $Failed = $true
    if ($FailCode -eq 0) { $FailCode = 4 }
}
finally {
    Stop-Game
}

if ($Failed) {
    Write-Host ''
    Write-Host "[smoke] FAILED (exit $FailCode)" -ForegroundColor Red
    exit $FailCode
}
exit 0
