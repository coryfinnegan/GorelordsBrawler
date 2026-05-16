<#
.SYNOPSIS
    End-to-end smoke test for the acid hazard.

.DESCRIPTION
    Launches GorelordsBrawler in debug mode with DebugServer + DebugDirectArena +
    DebugFastAcid, polls the HTTP debug server on :7777, and verifies the acid
    lifecycle: starts inactive, activates after the start delay, level rises
    monotonically, and players eventually take damage.

    Also fetches a screenshot at peak so you can eyeball the rendering, and —
    if ffmpeg is on PATH — records a short MP4 of the gameplay, uploads it to
    catbox.moe (free, no expiration), and prints the URL.

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

.PARAMETER RecordingPath
    Where to write the captured MP4 (default: $RepoRoot/.smoke-test-recording.mp4).

.PARAMETER NoBuild
    Skip dotnet build (assumes Debug binaries are already current).

.PARAMETER NoRecord
    Skip video recording + upload, even if ffmpeg is available.

.PARAMETER RecordSeconds
    How long to record. Defaults to 20s — long enough to catch acid activation
    (~3s in) plus a healthy stretch of pouring/pooling.
#>

[CmdletBinding()]
param(
    [string] $RepoRoot,
    [string] $ScreenshotPath,
    [string] $RecordingPath,
    [switch] $NoBuild,
    [switch] $NoRecord,
    [int]    $RecordSeconds = 20
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
if (-not $RecordingPath) {
    $RecordingPath = Join-Path $RepoRoot '.smoke-test-recording.mp4'
}
$RecordingUrlPath = Join-Path $RepoRoot '.smoke-test-recording-url.txt'

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

# ── Recording capability check ──────────────────────────────────────────────

$ffmpegCmd = if (-not $NoRecord) { Get-Command ffmpeg -ErrorAction SilentlyContinue } else { $null }
$canRecord = $null -ne $ffmpegCmd
if ($NoRecord) {
    Write-Host '[smoke] Recording disabled by -NoRecord.' -ForegroundColor Yellow
} elseif (-not $canRecord) {
    Write-Host '[smoke] ffmpeg not on PATH — recording will be skipped. (winget install Gyan.FFmpeg)' -ForegroundColor Yellow
}

# Win32 surface used to clip the recording to the game window. SetForegroundWindow +
# ShowWindow are essential — gdigrab captures whatever is at the desktop coords
# (including overlapping windows like Claude Code or a terminal), so we MUST bring
# the game to the foreground first or the video shows the wrong app entirely.
if ($canRecord) {
    if (-not ('Win32GetRect' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Win32GetRect {
    public const int SW_RESTORE = 9;
    public const int SW_SHOW    = 5;

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
    // AttachThreadInput trick lets us SetForegroundWindow from a background thread
    // (PowerShell often is, especially when invoked non-interactively from a script).
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
}
"@
    }
}

function Bring-WindowToFront([IntPtr] $hWnd) {
    # Restore if minimized, then use the AttachThreadInput trick to reliably steal
    # foreground from a script context.
    $null = [Win32GetRect]::ShowWindow($hWnd, [Win32GetRect]::SW_RESTORE)
    $null = [Win32GetRect]::BringWindowToTop($hWnd)

    $fg = [Win32GetRect]::GetForegroundWindow()
    $tidCurrent = [Win32GetRect]::GetCurrentThreadId()
    $procId = [uint32]0
    $tidFg = [Win32GetRect]::GetWindowThreadProcessId($fg, [ref] $procId)

    [void][Win32GetRect]::AttachThreadInput($tidCurrent, $tidFg, $true)
    try {
        $null = [Win32GetRect]::SetForegroundWindow($hWnd)
        $null = [Win32GetRect]::BringWindowToTop($hWnd)
    } finally {
        [void][Win32GetRect]::AttachThreadInput($tidCurrent, $tidFg, $false)
    }
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

$Game        = $null
$RecordProc  = $null
$Failed      = $false
$FailCode    = 0
$UploadedUrl = $null

function Stop-All {
    if ($script:RecordProc -and -not $script:RecordProc.HasExited) {
        try { $script:RecordProc.Kill() } catch {}
    }
    if ($script:Game -and -not $script:Game.HasExited) {
        try { $script:Game.Kill($true) } catch {}
    }
    if ($script:BackupMade) {
        Move-Item $script:BackupPath $script:SettingsPath -Force
    } else {
        Remove-Item $script:SettingsPath -ErrorAction SilentlyContinue
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

    # ── Start screen recording (Nez rewrites its window title every frame,
    #    so capture by window-rect instead of title=match) ─────────────────
    if ($canRecord) {
        # Poll for the window handle — Nez/MonoGame may take a moment to
        # finish creating the OS window even after the HTTP server is up.
        $hWnd = [IntPtr]::Zero
        $handleDeadline = (Get-Date).AddSeconds(5)
        while ((Get-Date) -lt $handleDeadline) {
            $Game.Refresh()
            $hWnd = $Game.MainWindowHandle
            if ($hWnd -ne [IntPtr]::Zero) { break }
            Start-Sleep -Milliseconds 200
        }
        if ($hWnd -eq [IntPtr]::Zero) {
            Write-Warning '[smoke] Could not resolve game window handle after 5 s — recording disabled.'
            $canRecord = $false
        } else {
            # CRITICAL: bring the game to the foreground before reading its rect
            # AND before starting ffmpeg. gdigrab captures the desktop pixels at
            # those coords, so any overlapping window (Claude Code, the terminal
            # running this script) would end up in the video instead of the game.
            Bring-WindowToFront $hWnd
            Start-Sleep -Milliseconds 400  # let the WM finish raising the window
            $rect = New-Object Win32GetRect+RECT
            $null = [Win32GetRect]::GetWindowRect($hWnd, [ref]$rect)
            $w = $rect.Right  - $rect.Left
            $h = $rect.Bottom - $rect.Top
            # gdigrab needs even dimensions for libx264 yuv420p
            if ($w % 2 -ne 0) { $w -= 1 }
            if ($h % 2 -ne 0) { $h -= 1 }
            Remove-Item $RecordingPath -ErrorAction SilentlyContinue
            Write-Host "[smoke] Recording $($w)x$($h) at ($($rect.Left),$($rect.Top)) for $RecordSeconds s..." -ForegroundColor Cyan
            $ffmpegArgs = @(
                '-y', '-hide_banner', '-loglevel', 'error',
                '-f',  'gdigrab',
                '-framerate', '30',
                '-offset_x',  $rect.Left,
                '-offset_y',  $rect.Top,
                '-video_size', "$($w)x$($h)",
                '-i', 'desktop',
                '-t', $RecordSeconds,
                '-c:v', 'libx264',
                '-pix_fmt', 'yuv420p',
                '-preset', 'ultrafast',
                '-crf', '23',
                '-an',
                $RecordingPath
            )
            $RecordProc = Start-Process -FilePath $ffmpegCmd.Source `
                -ArgumentList $ffmpegArgs -NoNewWindow -PassThru `
                -RedirectStandardError ([System.IO.Path]::GetTempFileName())
        }
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

    # ── Wait for the recording to finish (it self-stops via -t) ───────────
    if ($RecordProc -ne $null) {
        Write-Host '[smoke] Waiting for recording to finish...' -ForegroundColor Cyan
        if (-not $RecordProc.HasExited) {
            $null = $RecordProc.WaitForExit(($RecordSeconds + 5) * 1000)
        }
        if (Test-Path $RecordingPath) {
            $mb = [math]::Round((Get-Item $RecordingPath).Length / 1MB, 2)
            Write-Host "        OK ($mb MB at $RecordingPath)" -ForegroundColor Green
        } else {
            Write-Warning '[smoke] Recording finished but file is missing.'
        }
    }

    # ── Screenshot for the still-frame quality check ───────────────────────
    Write-Host '[smoke] Fetching screenshot...' -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri 'http://localhost:7777/screenshot' -OutFile $ScreenshotPath -TimeoutSec 10
        Write-Host "        OK ($ScreenshotPath)" -ForegroundColor Green
    } catch {
        Write-Warning "[smoke] Screenshot fetch failed: $_"
    }

    # ── Upload recording to catbox.moe (free, no expiration) ───────────────
    if ($RecordProc -ne $null -and (Test-Path $RecordingPath)) {
        Write-Host '[smoke] Uploading recording to catbox.moe...' -ForegroundColor Cyan
        try {
            $form = @{
                reqtype      = 'fileupload'
                fileToUpload = Get-Item $RecordingPath
            }
            $resp = Invoke-RestMethod -Uri 'https://catbox.moe/user/api.php' `
                -Method POST -Form $form -TimeoutSec 120
            $resp = ($resp | Out-String).Trim()
            if ($resp -match '^https?://\S+$') {
                $UploadedUrl = $resp
                Write-Host "        $UploadedUrl" -ForegroundColor Green
                Set-Content -Path $RecordingUrlPath -Value $UploadedUrl -NoNewline
            } else {
                Write-Warning "[smoke] Unexpected catbox response: $resp"
            }
        } catch {
            Write-Warning "[smoke] Upload failed: $_"
        }
    }

    Write-Host ''
    Write-Host '[smoke] ALL CHECKS PASSED ✓' -ForegroundColor Green
    if ($UploadedUrl) {
        Write-Host "[smoke] Recording: $UploadedUrl" -ForegroundColor Green
    }
}
catch {
    Write-Error "[smoke] Unhandled error: $_"
    $Failed = $true
    if ($FailCode -eq 0) { $FailCode = 4 }
}
finally {
    Stop-All
}

if ($Failed) {
    Write-Host ''
    Write-Host "[smoke] FAILED (exit $FailCode)" -ForegroundColor Red
    exit $FailCode
}
exit 0
