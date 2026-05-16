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
    }
}
