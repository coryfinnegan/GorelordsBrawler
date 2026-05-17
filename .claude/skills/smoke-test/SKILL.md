---
name: smoke-test
description: Feature-level smoke test + visual quality gate for GorelordsBrawler. Launches the game in Debug mode, drives it via the HTTP debug server, runs the chosen feature's check sequence, records a short MP4 of the gameplay, and uploads it to catbox.moe so the URL can go in a PR description. Use whenever the user says "smoke test the {feature}", "run the smoke test", "quality gate for {feature}", or asks to verify a feature works end-to-end on a live build. Pass the feature name via -Feature. See features/ for what's available.
allowed-tools: Bash, PowerShell, Read
---

# Smoke-test harness

End-to-end smoke test + visual quality gate for any feature. Launches the
game, runs a feature-specific check sequence against `http://localhost:7777/state`,
records 20 s of actual gameplay with `ffmpeg gdigrab`, and uploads the MP4
to catbox.moe so the URL can be pasted into a PR description.

## Run it

```bash
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid
```

`-Feature` picks the module under `features/`. The harness handles
everything generic — building, launching, foregrounding the game window,
recording, screenshot, upload, cleanup, restoring `appsettings.json`.

## What the harness does (feature-agnostic)

1. **Build** — `dotnet build GorelordsBrawler -c Debug`.
2. **Compose appsettings.json** — baseline `DebugServer = true` + `DebugDirectArena = true` merged with the feature's `AppSettings` hashtable. The original `appsettings.json` is backed up and restored on exit.
3. **Launch** the game, wait up to 20 s for `:7777/state`.
4. **Start frame capture job** — a background PowerShell job polls `GET /screenshot` (which returns the live back buffer) at as-fast-as-the-game-can-respond for `RecordSeconds` (feature default, 20 if unset), saving numbered PNGs to a temp dir. This is more reliable than `gdigrab` because the game keeps rendering even when its window thinks it's not focused — MonoGame disables vsync when `Game.IsActive` is false and `gdigrab` would capture a black DWM cache.
5. **Run feature checks** — invokes the feature's `Invoke` scriptblock with a `[SmokeCtx]` (see below).
6. **Wait for capture job**, then **stitch** the captured PNGs into an MP4 with `ffmpeg -framerate <captured/sec> -i frame_%05d.png -c:v libx264 ...`. Captured framerate is typically ~10 fps (limited by HTTP round-trip on `/screenshot`).
7. **Fetch** one more `/screenshot` for the still-frame artifact at `.smoke-test-screenshot.{feature}.png`.
8. **Upload** the MP4 to `https://catbox.moe/user/api.php` (free, no expiration). URL printed + written to `.smoke-test-recording-url.{feature}.txt`.
9. **Kill the game, clean frame dir, restore appsettings.json** in every exit path.

Failure exit codes:
- `1` build failure / missing feature module / bad descriptor
- `2` debug server didn't come up
- `3` a feature check failed (see the `FAIL:` line for which one)
- `4` HTTP / upload / unhandled error

## Available features

Anything under `features/*.ps1`. Currently:

- **`acid`** — Acid hazard lifecycle: inactive → activates → rises → damages players. ~6 s of useful gameplay, recorded for 20 s to capture the visible pool fill-up.

## Writing a new feature

Drop a `features/<name>.ps1` that returns a hashtable:

```powershell
return @{
    Name        = '<name>'
    Description = '<one-line summary, shown when the harness loads the module>'
    AppSettings = @{                # merged onto DebugServer + DebugDirectArena
        DebugFastAcid = $true       # whatever flags your feature needs
    }
    RecordSeconds = 20

    Invoke = {
        param([SmokeCtx] $Ctx)
        $Ctx.Check('first assertion', { param($c)
            $s = $c.GetState()
            if (-not $s.someFlag) { throw "someFlag was false" }
            "OK message printed on green line"
        })
        $Ctx.Check('something that needs polling', { param($c)
            $s = $c.WaitFor({ param($x) $x.thing -gt 100 }, 15000, 'thing > 100')
            "got thing=$($s.thing) at time=$($s.time)"
        })
    }
}
```

The `[SmokeCtx]` passed to `Invoke` exposes:

- `[object] GetState()` — one `/state` poll, deserialized
- `[object] WaitFor([scriptblock] $cond, [int] $timeoutMs, [string] $description)` — poll until `$cond` returns truthy, throw on timeout
- `[void] Check([string] $name, [scriptblock] $body)` — run one named assertion. Body receives the `SmokeCtx`. Throw to fail. Return value is printed on the OK line.

### Adding state your feature needs

The `/state` endpoint is fed by `GorelordsBrawler/DevTools/DebugStateExporter.cs`,
which always emits `time` + `players[]` and accepts arbitrary keys via
`RegisterProvider`. Wire your feature's keys in the scene's setup:

```csharp
#if DEBUG
if (AppSettings.DebugServer)
{
    var exporter = AddSceneComponent(new DevTools.DebugStateExporter(playerManager));
    exporter.RegisterProvider("combatActive",  () => combat.IsActive);
    exporter.RegisterProvider("currentRound",  () => combat.CurrentRound);
}
#endif
```

Then your feature module reads `$s.combatActive` etc.

## Prerequisites

- Game must build in Debug. `GorelordsBrawler/DevTools/` (GameDebugServer, DebugStateExporter, DebugCommands) must be present — if missing, recover from `GorelordsBrawler/Debug/` (silently gitignored by the default `[Dd]ebug/` rule).
- Port `7777` must be free.
- `ffmpeg` on `PATH` to stitch captured PNGs into MP4 (`winget install Gyan.FFmpeg`). Optional — if missing, the script warns and skips recording + upload.
- Outbound network to `catbox.moe` for the upload.
- PowerShell 7+ (uses `class` syntax and `Start-Job`). Cross-platform in principle, only smoke-tested on Windows.

## Reporting back to the user

If all checks pass and the upload succeeded: tell the user "{feature} smoke test passed — N/N checks green," paste the catbox URL, and offer to embed it in the active PR description (`gh api -X PATCH repos/.../pulls/N -f body=...`). `gh pr edit` has a known bug that fails on the GraphQL "Projects (classic)" deprecation — use the REST API directly instead.

If a check fails: report the failing check name verbatim (from the `FAIL at check '...'` line). Read `.smoke-test-screenshot.{feature}.png` to see the last frame before failure. The recording is NOT uploaded on failure (don't pollute catbox with broken clips).

## Preparing the user for functional testing

The smoke test is the FIRST gate — it proves the feature didn't crash and meets its assertions. After that the user does manual functional testing on the running game. Two things make that easier and the agent should do both as part of the feature-dev workflow:

1. **Pass `-OpenIde` on the smoke-test invocation that precedes PR creation.** When the test passes, the harness `Start-Process`es the worktree's `GorelordsBrawler.slnx`, opening Visual Studio against THIS branch's code. The user can hit F5 to run a normal (non-test-mode) game session immediately, or set breakpoints / inspect code while testing. No effect when checks fail (we don't yank focus on a broken run).

2. **Write the PR description for a learning reader.** Per the user's role in `CLAUDE.md`, they want to learn game-development concepts as they review. PR bodies should:
   - Lead with WHAT changed and WHY, not just the diff summary.
   - Briefly explain any non-obvious technique or API used (with a one-line citation if it came from research — paper, Nez source, MonoGame docs, etc.).
   - Call out the trade-offs or alternatives considered.
   - Treat the PR as the durable record of the change — once merged, the user will reference it later instead of re-reading the conversation.

If `-OpenIde` is wrong for the situation (user said they're away from their machine, you're iterating fast and don't want focus stealing), just don't pass it for that run.

## Options

```bash
# Skip build (use already-built Debug binaries)
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -NoBuild

# Skip recording + upload (for tight iteration loops)
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -NoRecord

# Custom recording length (overrides the feature's default)
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -RecordSeconds 30

# Custom output paths
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -ScreenshotPath C:\tmp\s.png -RecordingPath C:\tmp\r.mp4

# Specify repo root manually
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -RepoRoot C:\path\to\GorelordsBrawler

# Open Visual Studio after success so the user can F5 into a functional-test
# session against this branch's code (no-op on failure). The standard feature-
# dev agent workflow passes this on the run that precedes PR creation.
pwsh .claude/skills/smoke-test/smoke_test.ps1 -Feature acid -OpenIde
```

## Artifacts (all gitignored)

Written to the repo root, namespaced by feature so concurrent runs don't collide:

- `.smoke-test-screenshot.{feature}.png` — single frame near the end of the test
- `.smoke-test-recording.{feature}.mp4` — full ~20 s gameplay clip
- `.smoke-test-recording-url.{feature}.txt` — the uploaded catbox URL, ready to paste

## When NOT to run

- The user only wants the unit/integration suites (use `dotnet test` directly).
- The build itself is broken with errors unrelated to the feature under test.
