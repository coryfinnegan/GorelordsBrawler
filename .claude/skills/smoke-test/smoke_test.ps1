<#
.SYNOPSIS
    Feature-level smoke test + visual quality gate for GorelordsBrawler.

.DESCRIPTION
    Generic end-to-end harness:
        1. Build the game (Debug).
        2. Launch with DebugServer + DebugDirectArena + per-feature flags,
           injected as env vars on the game process (no appsettings.json written).
        3. Bring the game window to the foreground.
        4. Start an ffmpeg gdigrab recording of the game window.
        5. Run the chosen feature's check sequence against http://localhost:7777/state.
        6. Wait for the recording to finish (self-stops via -t).
        7. Fetch /screenshot.
        8. Upload the MP4 to catbox.moe (free, no expiration).
        9. Print the URL — paste it into the PR as the visual quality gate.

    Each feature lives in features/<name>.ps1 as a hashtable describing its
    appsettings overrides, record length, and Invoke scriptblock. The Invoke
    block receives a [SmokeCtx] with Check / GetState / WaitFor helpers.

    See features/acid.ps1 for the reference implementation.

    Exit codes:
        0 = all checks passed
        1 = build failure / missing feature module / bad descriptor
        2 = game failed to start / debug server never came up
        3 = a feature check failed
        4 = HTTP / upload / unhandled error
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Feature,
    [string] $RepoRoot,
    [string] $ScreenshotPath,
    [string] $RecordingPath,
    [switch] $NoBuild,
    [switch] $NoRecord,
    [int]    $RecordSeconds = 0,  # 0 = use feature default
    # When set, on success Start-Process the worktree's .slnx file so the
    # user's registered IDE (Visual Studio) opens against THIS branch's code,
    # ready for them to F5 into a normal session for manual functional
    # testing. The smoke-test still kills its own debug-mode game instance
    # in the finally block; this is a separate handoff to the IDE.
    [switch] $OpenIde
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
        exit 1
    }
    $RepoRoot = $dir
}

# ── Load feature module ────────────────────────────────────────────────────

$FeaturePath = Join-Path $PSScriptRoot "features/$Feature.ps1"
if (-not (Test-Path $FeaturePath)) {
    $available = Get-ChildItem -Path (Join-Path $PSScriptRoot 'features') -Filter '*.ps1' -ErrorAction SilentlyContinue
    $names = if ($available) { ($available | ForEach-Object { $_.BaseName }) -join ', ' } else { '(none)' }
    Write-Error "Feature module not found: $FeaturePath`nAvailable features: $names"
    exit 1
}
$Feat = & $FeaturePath
if ($null -eq $Feat -or -not $Feat.Name -or -not $Feat.Invoke) {
    Write-Error "Feature module $FeaturePath did not return a valid descriptor (need Name + Invoke)."
    exit 1
}

Write-Host "[smoke] Feature: $($Feat.Name)" -ForegroundColor Magenta
if ($Feat.Description) { Write-Host "        $($Feat.Description)" -ForegroundColor Gray }

# ── Paths ──────────────────────────────────────────────────────────────────

$ExePath = Join-Path $RepoRoot 'GorelordsBrawler\bin\Debug\net8.0\GorelordsBrawler.exe'
$ExeDir  = Split-Path $ExePath -Parent
if (-not $ScreenshotPath) {
    $ScreenshotPath = Join-Path $RepoRoot ".smoke-test-screenshot.$($Feat.Name).png"
}
if (-not $RecordingPath) {
    $RecordingPath = Join-Path $RepoRoot ".smoke-test-recording.$($Feat.Name).mp4"
}
$RecordingUrlPath = Join-Path $RepoRoot ".smoke-test-recording-url.$($Feat.Name).txt"

if ($RecordSeconds -le 0) {
    $RecordSeconds = if ($Feat.RecordSeconds) { [int]$Feat.RecordSeconds } else { 20 }
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

# ── Recording capability ────────────────────────────────────────────────────
# Capture by polling the game's own /screenshot endpoint (which returns the
# live back-buffer encoded as JPEG) into a temp dir, then stitch into MP4
# with ffmpeg. We tried ffmpeg gdigrab against the game window's rect — the
# captured client area was always black because MonoGame disables vsync
# when its window thinks it's not focused, and Windows hides the rendered
# frames from DWM even when the window is HWND_TOPMOST. SetForegroundWindow
# from a script context is unreliable.  Polling the game's own screenshot
# endpoint sidesteps all of it: we always get the live frame, regardless of
# what app is foreground or what DWM is doing.
#
# The /screenshot endpoint now returns JPEG (was PNG) which is ~5× faster
# to encode and 5–10× smaller per frame, getting the polling rate up to
# ~30 fps. Still needs ffmpeg to stitch the JPEGs into one MP4.

$ffmpegCmd = if (-not $NoRecord) { Get-Command ffmpeg -ErrorAction SilentlyContinue } else { $null }
$canRecord = $null -ne $ffmpegCmd
if ($NoRecord) {
    Write-Host '[smoke] Recording disabled by -NoRecord.' -ForegroundColor Yellow
} elseif (-not $canRecord) {
    Write-Host '[smoke] ffmpeg not on PATH — recording will be skipped. (winget install Gyan.FFmpeg)' -ForegroundColor Yellow
}

# ── Debug settings → game-process environment (no file written) ────────────
# Baseline: DebugServer + DebugDirectArena. Feature can add/override anything.
# These are injected as environment variables on the GAME child process at
# launch (see below), never by writing appsettings.json in the build output —
# that file is the player's own config, and overwriting it used to leave debug
# flags behind for the next manual run. Env vars die with the game process.

$Settings = @{
    DebugServer      = $true
    DebugDirectArena = $true
}
if ($Feat.AppSettings) {
    foreach ($k in $Feat.AppSettings.Keys) { $Settings[$k] = $Feat.AppSettings[$k] }
}

# PascalCase setting name -> GLB_UPPER_SNAKE env var (mirrors AppSettings.Env*
# and GameDriver's psi.Environment keys). E.g. DebugDirectArena ->
# GLB_DEBUG_DIRECT_ARENA.
function ConvertTo-GameEnvName([string] $Key) {
    'GLB_' + (($Key -creplace '([a-z0-9])([A-Z])', '$1_$2').ToUpperInvariant())
}

# ── Smoke context: passed to feature's Invoke block ────────────────────────

class SmokeCtx {
    [string] $ServerUrl
    [int]    $ChecksPassed
    [string] $LastCheckName

    SmokeCtx([string] $url) {
        $this.ServerUrl    = $url
        $this.ChecksPassed = 0
    }

    [object] GetState() {
        return Invoke-RestMethod -Uri "$($this.ServerUrl)/state" -TimeoutSec 2
    }

    [object] WaitFor([scriptblock] $Cond, [int] $TimeoutMs, [string] $Description) {
        $end = (Get-Date).AddMilliseconds($TimeoutMs)
        while ((Get-Date) -lt $end) {
            $s = $this.GetState()
            if (& $Cond $s) { return $s }
            Start-Sleep -Milliseconds 250
        }
        throw "Timed out waiting for: $Description"
    }

    [void] Check([string] $Name, [scriptblock] $Body) {
        $this.LastCheckName = $Name
        Write-Host "[smoke] Check $($this.ChecksPassed + 1): $Name" -ForegroundColor Cyan
        $result = & $Body $this
        if ($result) {
            Write-Host "        OK ($result)" -ForegroundColor Green
        } else {
            Write-Host "        OK" -ForegroundColor Green
        }
        $this.ChecksPassed++
    }
}

# ── Launch + cleanup wrapper ───────────────────────────────────────────────

$Game        = $null
$CaptureJob  = $null
$FrameDir    = $null
$Failed      = $false
$FailCode    = 0
$UploadedUrl = $null

function Stop-All {
    if ($script:CaptureJob) {
        try { Stop-Job   $script:CaptureJob -ErrorAction SilentlyContinue } catch {}
        try { Remove-Job $script:CaptureJob -Force -ErrorAction SilentlyContinue } catch {}
    }
    if ($script:FrameDir -and (Test-Path $script:FrameDir)) {
        Remove-Item $script:FrameDir -Recurse -Force -ErrorAction SilentlyContinue
    }
    if ($script:Game -and -not $script:Game.HasExited) {
        try { $script:Game.Kill($true) } catch {}
    }
}

try {
    Write-Host '[smoke] Launching game...' -ForegroundColor Cyan
    $psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
    $psi.UseShellExecute  = $false
    $psi.WorkingDirectory = $ExeDir
    # Scope the debug flags to THIS game process via env vars — not a written
    # appsettings.json, and NOT the script-global $env: (which the -OpenIde
    # Start-Process would inherit, leaking debug mode into the user's IDE/F5
    # session). $psi.Environment starts seeded from the current process, so PATH
    # etc. survive; we only add the GLB_ overrides.
    foreach ($k in $Settings.Keys) {
        $val = if ($Settings[$k]) { '1' } else { '0' }
        $psi.Environment[(ConvertTo-GameEnvName $k)] = $val
    }
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

    # ── Start screen recording via /screenshot polling job ────────────────
    if ($canRecord) {
        $FrameDir = Join-Path $RepoRoot ".smoke-frames-$($Feat.Name)"
        Remove-Item $FrameDir -Recurse -Force -ErrorAction SilentlyContinue
        $null = New-Item -Path $FrameDir -ItemType Directory
        Write-Host "[smoke] Recording via /screenshot polling for $RecordSeconds s..." -ForegroundColor Cyan
        # Inline System.Net.Http.HttpClient keeps a single keep-alive connection
        # open for the whole job (vs Invoke-WebRequest, which opens fresh
        # WebRequests each iteration). Combined with the JPEG screenshot
        # encoding on the game side, the polling rate gets ~3× higher.
        $CaptureJob = Start-Job -ScriptBlock {
            param($url, $dir, $maxSec)
            Add-Type -AssemblyName System.Net.Http
            $http = [System.Net.Http.HttpClient]::new()
            $http.Timeout = [TimeSpan]::FromSeconds(5)
            $deadline = (Get-Date).AddSeconds($maxSec)
            $i = 0
            while ((Get-Date) -lt $deadline) {
                try {
                    $bytes = $http.GetByteArrayAsync("$url/screenshot").GetAwaiter().GetResult()
                    $path  = Join-Path $dir ("frame_{0:D5}.jpg" -f $i)
                    [System.IO.File]::WriteAllBytes($path, $bytes)
                    $i++
                } catch { }
            }
            $http.Dispose()
            return $i
        } -ArgumentList 'http://localhost:7777', $FrameDir, $RecordSeconds
    }

    # ── Run feature checks ─────────────────────────────────────────────────
    $Ctx = [SmokeCtx]::new('http://localhost:7777')
    try {
        & $Feat.Invoke $Ctx
    } catch {
        Write-Error "[smoke] FAIL at check '$($Ctx.LastCheckName)': $_"
        $Failed = $true
        $FailCode = 3
    }

    # ── Wait for capture job, then stitch JPEGs into one MP4 with ffmpeg ──
    if ($CaptureJob -ne $null) {
        Write-Host '[smoke] Waiting for capture job to finish...' -ForegroundColor Cyan
        $null = Wait-Job $CaptureJob -Timeout ($RecordSeconds + 10)
        $frameCount = Receive-Job $CaptureJob -ErrorAction SilentlyContinue
        Remove-Job $CaptureJob -Force -ErrorAction SilentlyContinue
        $CaptureJob = $null

        $frames = Get-ChildItem -Path $FrameDir -Filter 'frame_*.jpg' -ErrorAction SilentlyContinue
        if (-not $frames -or $frames.Count -lt 2) {
            Write-Warning "[smoke] Capture job produced only $($frames.Count) frame(s) — recording skipped."
        } else {
            # Output at the rate we actually captured so playback matches wall-clock.
            $framerate = [int][math]::Round($frames.Count / [math]::Max(1, $RecordSeconds))
            if ($framerate -lt 1) { $framerate = 1 }
            Write-Host "        captured $($frames.Count) frames; stitching at $framerate fps..." -ForegroundColor Gray
            Remove-Item $RecordingPath -ErrorAction SilentlyContinue
            $ffmpegArgs = @(
                '-y', '-hide_banner', '-loglevel', 'error',
                '-framerate', $framerate,
                '-i', (Join-Path $FrameDir 'frame_%05d.jpg'),
                '-c:v', 'libx264',
                '-pix_fmt', 'yuv420p',
                '-preset', 'ultrafast',
                '-crf', '23',
                $RecordingPath
            )
            & $ffmpegCmd.Source @ffmpegArgs
            if (Test-Path $RecordingPath) {
                $mb = [math]::Round((Get-Item $RecordingPath).Length / 1MB, 2)
                Write-Host "        OK ($mb MB at $RecordingPath)" -ForegroundColor Green
            } else {
                Write-Warning '[smoke] ffmpeg stitching produced no output.'
            }
        }
        Remove-Item $FrameDir -Recurse -Force -ErrorAction SilentlyContinue
    }

    # ── Screenshot ─────────────────────────────────────────────────────────
    Write-Host '[smoke] Fetching screenshot...' -ForegroundColor Cyan
    try {
        Invoke-WebRequest -Uri 'http://localhost:7777/screenshot' -OutFile $ScreenshotPath -TimeoutSec 10
        Write-Host "        OK ($ScreenshotPath)" -ForegroundColor Green
    } catch {
        Write-Warning "[smoke] Screenshot fetch failed: $_"
    }

    # ── Upload (only on success — don't pollute catbox with failure clips) ─
    if (-not $Failed -and $canRecord -and (Test-Path $RecordingPath)) {
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

    if (-not $Failed) {
        $n = $Ctx.ChecksPassed
        Write-Host ''
        Write-Host "[smoke] $($Feat.Name.ToUpper()) — $n/$n CHECKS PASSED ✓" -ForegroundColor Green
        if ($UploadedUrl) {
            Write-Host "[smoke] Recording: $UploadedUrl" -ForegroundColor Green
        }

        # ── Hand off to the IDE for manual functional testing ──────────────
        # Only on -OpenIde and only after success — never want to flip the
        # user's IDE open in the middle of an iteration loop that just
        # failed. The .slnx opens via the OS handler (Visual Studio on
        # Windows), pinned to the worktree so they're inspecting THIS
        # branch's code. If no handler is registered (e.g. headless CI),
        # warn and continue rather than failing the test.
        if ($OpenIde) {
            $SlnPath = Join-Path $RepoRoot 'GorelordsBrawler.slnx'
            if (Test-Path $SlnPath) {
                Write-Host "[smoke] Opening IDE: $SlnPath" -ForegroundColor Cyan
                try {
                    Start-Process -FilePath $SlnPath
                } catch {
                    Write-Warning "[smoke] Could not open .slnx (is Visual Studio installed and registered for .slnx files?): $_"
                }
            } else {
                Write-Warning "[smoke] -OpenIde requested but no .slnx at $SlnPath"
            }
        }
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
