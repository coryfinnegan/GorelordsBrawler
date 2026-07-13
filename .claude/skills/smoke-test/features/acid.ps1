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

    # Feature-specific debug flags layered on top of the harness baseline
    # (DebugServer + DebugDirectArena are always set); injected as GLB_* env
    # vars on the game process, not written to appsettings.json.
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
        # synchronously in OnAddedToEntity, but the debug server comes up BEFORE
        # the scene finishes loading content (shaders + 4096px atlases), so /state
        # serves empty snapshots for several seconds on a COLD boot — e.g. the
        # first launch after a fresh build. A 5 s timeout flaked exactly that way
        # (passed with -NoBuild, failed right after a rebuild). 15 s rides out the
        # content load; the calm-phase assert below still proves the pre-fill
        # happened before the rise, because scene time (and the 3 s DebugFastAcid
        # delay) only starts ticking once the scene is actually updating.
        $Ctx.Check('basin is pre-filled during the calm phase (before rise)', {
            param($c)
            $s = $c.WaitFor({ param($x) $x.acidParticleCount -gt 0 }, 15000, 'acidParticleCount > 0')
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

        # Regression guard for the "FPS tanks as the pour runs away" bug, in its
        # Phase-C form: the pour must always respect its safety stop. The fill
        # is CLOSED-LOOP on the measured surface, so the real count can
        # legitimately run up to 1.25x the geometric estimate (acidFillCap)
        # before the hard stop — the runaway bound is 1.30x + splash slack.
        $Ctx.Check('pour always respects its safety stop (no runaway / FPS cliff)', {
            param($c)
            $c.WaitFor({ param($x) $x.acidActive }, 15000, 'acidActive == true') | Out-Null
            Start-Sleep -Seconds 8
            $s1 = $c.GetState()
            Start-Sleep -Seconds 3
            $s2 = $c.GetState()
            foreach ($s in @($s1, $s2)) {
                $bound = [int]($s.acidFillCap * 1.30) + 50
                if ($s.acidParticleCount -gt $bound) {
                    throw "count $($s.acidParticleCount) exceeds the safety envelope for estimate $($s.acidFillCap) (1.30x + 50 = $bound) in phase $($s.acidPhase) — pour running away"
                }
            }
            "count $($s1.acidParticleCount)->$($s2.acidParticleCount) within envelope of estimate $($s2.acidFillCap) (phase=$($s2.acidPhase), loop=$($s2.acidLoop))"
        })

        # Phase C: the phase machine must progress past Rise into the Scramble/
        # Surge stretch within the debug-fast window, and at least one surge
        # must fire (the first surge fires ON Surge-phase entry, as the visible
        # beat). Catches a stuck state machine that the older checks (level
        # rises, players damaged) would sail straight past.
        $Ctx.Check('phase machine progresses and a surge fires', {
            param($c)
            $s = $c.WaitFor({ param($x) $x.acidPhase -eq 'Surge' -and $x.acidSurgeCount -ge 1 }, 60000, "acidPhase == Surge with a surge fired")
            "phase=$($s.acidPhase), loop=$($s.acidLoop), surges=$($s.acidSurgeCount), time=$($s.time)"
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
            # STAGE the sample: the footing-cycle map is all hazard, so idle
            # smoke players die and respawn constantly — "whenever this check
            # happens to run" stopped being a valid sample moment (both dead =
            # zero red pixels = a false invisibility alarm; hit twice on
            # -OpenIde runs, whose IDE launch delays the schedule). Wait out
            # the respawn window, then teleport whoever is alive to a fixed
            # mid-air spot over the pit (above the storm ceiling, inside the
            # scan band, clear of every platform column) and snapshot at once.
            $deadline = (Get-Date).AddSeconds(12)
            do {
                $s = Invoke-RestMethod "$($c.ServerUrl)/state" -TimeoutSec 3
                $alive = @($s.players | Where-Object { -not $_.dead }).Count
                if ($alive -ge 2) { break }
                Start-Sleep -Milliseconds 250
            } while ((Get-Date) -lt $deadline)
            foreach ($i in 0, 1) {
                $body = @{ player = $i; x = 576 + $i * 128; y = 200 } | ConvertTo-Json -Compress
                Invoke-RestMethod -Method Post -Uri "$($c.ServerUrl)/teleport" `
                    -Body $body -ContentType 'application/json' -TimeoutSec 3 | Out-Null
            }
            Start-Sleep -Milliseconds 150   # let a frame render with them in place

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
