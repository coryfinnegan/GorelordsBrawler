# claude_capture.ps1 — grab a screenshot + state snapshot from the running game
# Usage:  pwsh tools/claude_capture.ps1 [-Out C:\temp\frame.png] [-Base http://localhost:7777]
param(
    [string]$Out  = "$env:TEMP\game_frame.png",
    [string]$Base = "http://localhost:7777"
)

$ErrorActionPreference = "Stop"

# Screenshot
try {
    $resp = Invoke-WebRequest "$Base/screenshot" -TimeoutSec 10
    [IO.File]::WriteAllBytes($Out, $resp.Content)
    Write-Host "Screenshot saved: $Out"
} catch {
    Write-Host "Screenshot failed: $_"
    $Out = $null
}

# State
try {
    $state = (Invoke-WebRequest "$Base/state" -TimeoutSec 3).Content
    Write-Host "State: $state"
} catch {
    Write-Host "State failed: $_"
}

if ($Out) { Write-Host "READ_PATH: $Out" }
