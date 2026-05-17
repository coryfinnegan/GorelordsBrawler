<#
.SYNOPSIS
    Acid hazard smoke-test feature module.

.DESCRIPTION
    Asserts the acid hazard lifecycle: inactive at start → activates within
    the start delay → level rises monotonically → players take damage when
    submerged. With DebugFastAcid the whole arc completes in ~6 s.

    State keys this module reads from /state (provided by ArenaScene's
    DebugStateExporter registrations):
        acidActive : bool   — true once AcidSurface.IsRising
        acidLevel  : int    — current world-Y of the acid surface (smaller = higher on screen)
        players[]  : { hp, maxHp, ... } — used to detect damage
#>

return @{
    Name        = 'acid'
    Description = 'Acid hazard lifecycle (inactive → active → rises → damages players).'

    # Feature-specific appsettings layered on top of the harness baseline
    # (DebugServer + DebugDirectArena are always set).
    AppSettings = @{
        DebugFastAcid = $true   # collapse 30 s start delay to 3 s for fast iteration
    }

    RecordSeconds = 20

    Invoke = {
        param([SmokeCtx] $Ctx)

        $Ctx.Check('acid inactive at game start', {
            param($c)
            $s = $c.GetState()
            if ($s.acidActive) {
                throw "acid was already active at startup (time=$($s.time))"
            }
            "acidActive=false, time=$($s.time)"
        })

        $Ctx.Check('acid activates within 15 s', {
            param($c)
            $s = $c.WaitFor({ param($x) $x.acidActive }, 15000, 'acidActive == true')
            "activated at time=$($s.time), level=$($s.acidLevel)"
        })

        $Ctx.Check('acid level rises over time', {
            param($c)
            $a = $c.GetState()
            Start-Sleep -Seconds 3
            $b = $c.GetState()
            if ($b.acidLevel -ge $a.acidLevel) {
                throw "level $($a.acidLevel) -> $($b.acidLevel) — should decrease (Y down = higher on screen)"
            }
            "level $($a.acidLevel) -> $($b.acidLevel), drop=$($a.acidLevel - $b.acidLevel) px in 3 s"
        })

        $Ctx.Check('at least one player takes damage within 45 s', {
            param($c)
            $s = $c.WaitFor({
                param($x)
                foreach ($p in $x.players) { if ($p.hp -lt $p.maxHp) { return $true } }
                return $false
            }, 45000, 'any player.hp < player.maxHp')
            $hits = ($s.players | Where-Object { $_.hp -lt $_.maxHp }).Count
            "time=$($s.time), $hits player(s) damaged"
        })

        # ──────────────────────────────────────────────────────────────────
        # Regression test: player sprites are actually rendered.
        # ──────────────────────────────────────────────────────────────────
        # PR #3 shipped a regression where particle emitters on the
        # default RenderLayer corrupted Batcher state and silently disabled
        # the player SpriteAnimator render — health bars still showed, but
        # the character sprites disappeared. This pixel check would have
        # caught it.
        #
        # We run AFTER the acid lifecycle checks so the whole rendering
        # pipeline has been active for ~6 s (acid pouring, smoke firing,
        # ContactHazard processing damage). If sprite rendering was going
        # to break, it's broken by now. Detection works by counting RED-
        # DOMINANT pixels in the lower third of the screen — Future-Axe
        # players wear red armour; floors are gray; the background is
        # black. Only a rendered player sprite can light up those pixels.
        $Ctx.Check('player sprites are still rendered (regression for player invisibility)', {
            param($c)
            $repoRoot = (Get-Item $PSScriptRoot).Parent.Parent.Parent.Parent.FullName
            $tmp = Join-Path $repoRoot '.smoke-test-spawn-screenshot.acid.jpg'
            Invoke-WebRequest -Uri "$($c.ServerUrl)/screenshot" `
                -OutFile $tmp -UseBasicParsing -TimeoutSec 5 -ErrorAction Stop
            Add-Type -AssemblyName System.Drawing
            $img = [System.Drawing.Image]::FromFile($tmp)
            $bmp = New-Object System.Drawing.Bitmap($img)
            $img.Dispose()

            $redCount = 0
            $startY   = [int]($bmp.Height * 0.66)
            for ($y = $startY; $y -lt $bmp.Height; $y += 2) {
                for ($x = 0; $x -lt $bmp.Width; $x += 2) {
                    $px = $bmp.GetPixel($x, $y)
                    if ([int]$px.R -gt 70 -and
                        [int]$px.R -gt [int]$px.G + 15 -and
                        [int]$px.R -gt [int]$px.B + 15) {
                        $redCount++
                    }
                }
            }
            $bmp.Dispose()
            if ($redCount -lt 100) {
                throw "only $redCount red-dominant pixels in the lower screen — player sprites likely not rendering. Screenshot saved at $tmp."
            }
            "red-dominant pixel count = $redCount (threshold >= 100)"
        })
    }
}
