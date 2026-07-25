<#
.SYNOPSIS
    Acid pacing + calibration smoke-test feature module.

.DESCRIPTION
    The regression gate for the two failures the 2026-07-01 real-speed capture
    exposed:
      1. CALIBRATION — the particle caps assumed hex-packed density, so every
         fill ceiling landed ~half its intended height above the lip (the acid
         never reached a platform in a real match). Asserts the MEASURED
         standing surface (acidSurfaceY oracle) lands on the loop-0 ceiling.
      2. PACING — the drain "relief beat" was a fixed-rate 2.7 s blink.
         Asserts the drain takes ~its configured duration in game time.

    Runs under DebugFastAcid (the harness baseline): the pour is 4x and the
    timers are x0.25, but the CAP (and so the standing surface) is identical to
    a real match — the calibration check is speed-independent.

    Keep the literals in sync with AcidConfig:
        RiseCeilings[0]      = 528  (loop-0 "banks awash" ceiling)
        DrainDurationSeconds = 9    (x0.25 debug-fast = 2.25 s)
#>

return @{
    Name        = 'pacing'
    Description = 'Acid pacing gates: cap-to-surface calibration + the drain relief beat.'

    AppSettings = @{
        DebugFastAcid = $true
    }

    RecordSeconds = 30

    Invoke = {
        param([SmokeCtx] $Ctx)

        # ── 0. The opening volley: footing exists within seconds of frame one ──
        # The 2026-07-24 pacing rework: the footing director telegraphs the
        # match's first platforms at t=0 and holds a target population, instead
        # of waiting for the acid's first consume beat (60+ s of bare arena).
        $Ctx.Check('opening volley reaches the footing target within seconds', {
            param($c)
            # The debug server answers before the scene exists, and PowerShell's
            # $null -ge $null is TRUE — demand a concrete positive target so an
            # early empty state can't pass this vacuously.
            $s = $c.WaitFor({ param($x)
                [int]$x.platformTarget -gt 0 -and [int]$x.platformsAlive -ge [int]$x.platformTarget
            }, 15000, 'platformsAlive >= platformTarget (> 0)')
            if ([float]$s.time -le 0 -or [float]$s.time -gt 10) {
                throw "full footing arrived at game-time t=$($s.time)s - the opening volley regressed (target is ~1 s debug-fast / ~3 s real)"
            }
            "platformsAlive=$($s.platformsAlive) / target=$($s.platformTarget) at t=$($s.time)s"
        })

        # ── 1. Calibration: the loop-0 rise must STAND where its ceiling says ──
        # Median of 3 reads (surface sloshes +/-16-32 px in 16 px probe quanta);
        # +/-40 px band — the gate exists to catch calibration-class breaks
        # (the original was ~130 px off), not wave noise.
        $Ctx.Check('loop-0 standing surface lands on the configured ceiling (528 +/- 40)', {
            param($c)
            $s = $c.WaitFor({ param($x) $x.acidPhase -eq 'Scramble' }, 30000, 'phase == Scramble (loop-0 target reached)')
            $reads = @()
            foreach ($i in 1..3) {
                Start-Sleep -Seconds 1
                $reads += [int]$c.GetState().acidSurfaceY
            }
            $surface = ($reads | Sort-Object)[1]
            if ($surface -lt 488 -or $surface -gt 568) {
                throw "standing surface y=$surface (reads: $($reads -join ',')) is off the loop-0 ceiling (528 +/- 40) - the cap<->surface calibration drifted (see FluidConfig.EffectiveParticleArea / closed-loop fill)"
            }
            $s = $c.GetState()
            "acidSurfaceY=$surface (reads $($reads -join ',')) vs ceiling 528 (estimate $($s.acidFillCap), count $($s.acidParticleCount))"
        })

        # ── 2. The drain is a BEAT, not a blink ────────────────────────────────
        $Ctx.Check('drain takes ~its configured duration (9 s scaled -> ~2.25 s)', {
            param($c)
            $d0 = $c.WaitFor({ param($x) $x.acidPhase -eq 'Drain' }, 30000, 'phase == Drain')
            $t0 = [float]$d0.time
            $r1 = $c.WaitFor({ param($x) $x.acidPhase -eq 'Rise' -and $x.acidLoop -ge 1 }, 20000, 'phase == Rise (loop 1)')
            $dur = [float]$r1.time - $t0
            if ($dur -lt 1.0 -or $dur -gt 4.5) {
                throw "drain took $($dur.ToString('F2')) s game-time - target is ~2.25 s (9 s x0.25); the relief beat regressed"
            }
            "drain duration $($dur.ToString('F2')) s game-time (target ~2.25 s)"
        })

        # ── 3. Nothing went NaN while we were at it ────────────────────────────
        $Ctx.Check('all acid particles stay finite', {
            param($c)
            $s = $c.GetState()
            if (-not $s.acidFinite) {
                throw "acidFinite=false at time=$($s.time) - particle sim went NaN"
            }
            "acidFinite=true, count=$($s.acidParticleCount)"
        })
    }
}
