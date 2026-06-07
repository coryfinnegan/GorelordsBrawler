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

        # The Sump (Phase A): the basin is PRE-FILLED with a resting pool at
        # scene start (AcidSurface.PreFill), so the acid is present and dangerous
        # during the "Calm" phase — long before it starts rising. PreFill runs
        # synchronously in OnAddedToEntity, but the first /state snapshot can land
        # a frame or two after the HTTP server opens (fields read empty until the
        # exporter's first Update — same reason the original Check 2 used WaitFor).
        # Wait for the first populated snapshot, then assert acid exists BEFORE
        # the rise begins (acidActive still false → it's the pre-fill, not pour).
        $Ctx.Check('basin is pre-filled during the calm phase (before rise)', {
            param($c)
            $s = $c.WaitFor({ param($x) $x.acidParticleCount -gt 0 }, 5000, 'acidParticleCount > 0')
            if ($s.acidActive) {
                throw "acid was already rising when particles first appeared (time=$($s.time)) — cannot prove the pool was pre-filled during calm"
            }
            "acidParticleCount=$($s.acidParticleCount) while acidActive=false (calm phase)"
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

        # Regression guard for the "FPS tanks as the basin fills" bug: the inlet
        # must STOP once the basin is full (a geometry-derived particle cap),
        # instead of pouring toward MaxParticles (5000) and flooding the arena.
        # The old volumetric inlet-stop was geometry-blind and never tripped for
        # the narrow basin. Let it fill, then confirm the count plateaued well
        # below the 5000 cap and is no longer climbing. (DebugFastAcid 4x → the
        # basin fills in ~5 s after activation; 8 s is a safe settle margin.)
        $Ctx.Check('inlet stops at basin-full (count plateaus, no flood / FPS cliff)', {
            param($c)
            $c.WaitFor({ param($x) $x.acidActive }, 15000, 'acidActive == true') | Out-Null
            Start-Sleep -Seconds 8
            $c1 = $c.GetState().acidParticleCount
            Start-Sleep -Seconds 3
            $c2 = $c.GetState().acidParticleCount
            if ($c2 -ge 2500) {
                throw "particle count $c2 too high — inlet did not cap (ran toward MaxParticles=5000, the flood/FPS regression)"
            }
            if (($c2 - $c1) -gt 100) {
                throw "particle count still climbing ($c1 -> $c2 over 3 s) — inlet not capping"
            }
            "count plateaued at $c2 (< 2500, delta=$($c2 - $c1) over 3 s)"
        })

        # No-NaN guard: the PBF sim must stay finite through the whole lifecycle
        # (the hitstop dt=0 -> 1/dt -> NaN failure mode documented in CLAUDE.md).
        # acidFinite scans every live particle; if it ever flips false the pool
        # has silently corrupted even if it still looks fine for a frame.
        $Ctx.Check('all acid particles stay finite (no NaN blow-up)', {
            param($c)
            $s = $c.GetState()
            if (-not $s.acidFinite) {
                throw "acidFinite=false at time=$($s.time) — particle sim went NaN (count=$($s.acidParticleCount))"
            }
            "acidFinite=true, count=$($s.acidParticleCount), time=$($s.time)"
        })

        # ──────────────────────────────────────────────────────────────────
        # Regression test: player sprites are actually rendered.
        # ──────────────────────────────────────────────────────────────────
        # Previous PRs shipped regressions where new renderables on the wrong
        # RenderLayer (or with mid-render device-state changes) silently
        # disabled the player SpriteAnimator — health bars still showed, but
        # the character sprites disappeared. This pixel check would have
        # caught those PRs before they shipped.
        #
        # Runs AFTER the acid lifecycle so the whole rendering pipeline has
        # been active for ~6 s. We count RED-DOMINANT pixels — Future-Axe
        # players wear red armour; floors/tiers are gray; the background is
        # dark blue/black; the acid is green. Only a rendered player sprite
        # can light those pixels up.
        #
        # Scan band: the WHOLE play area minus the top HUD strip (which renders
        # red "P1 XXX P2 XXX" stock text that would be a false positive) and the
        # bottom letterbox. The previous lower-third-only band (y > 0.66) broke
        # when the Sump arena moved spawns up onto the banks (~0.58 height) — the
        # players sit ABOVE the old band, so it counted 0 and false-failed. A
        # full-height-minus-HUD scan is robust to spawn-position changes.
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
            $startY = [int]($bmp.Height * 0.15)   # below the red HUD stock text
            $endY   = [int]($bmp.Height * 0.90)   # above the bottom letterbox
            for ($y = $startY; $y -lt $endY; $y += 2) {
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
                throw "only $redCount red-dominant pixels in the play area — player sprites likely not rendering. Screenshot saved at $tmp."
            }
            "red-dominant pixel count = $redCount (threshold >= 100)"
        })
    }
}
