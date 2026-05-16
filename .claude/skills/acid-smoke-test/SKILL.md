---
name: acid-smoke-test
description: Run an end-to-end smoke test of the acid hazard in GorelordsBrawler. Use when verifying the particle fluid simulation works on a live game build (after AcidSurface/Fluid/* changes), or whenever the user says "run the smoke test", "smoke test the acid", or asks to verify the acid hazard runs in-game.
allowed-tools: Bash, Read
---

# Acid hazard smoke test

Launches the game in Debug mode with `DebugServer` + `DebugDirectArena` + `DebugFastAcid`,
hits the HTTP debug server on `:7777`, asserts the full acid lifecycle, and grabs a
screenshot for visual eyeballing. Built for fast iteration on `Components/Hazards/Fluid/*`
without manually clicking through menus.

## Prerequisites

- Game must build in Debug (`dotnet build GorelordsBrawler/GorelordsBrawler.csproj -c Debug`).
- The `GorelordsBrawler/DevTools/` folder must exist (provides `GameDebugServer`,
  `DebugStateExporter`, `DebugCommands`). If it doesn't, the Debug build will fail —
  recover it from the user's `Debug/` folder (which is gitignored by `[Dd]ebug/` rule).
- Port `7777` must be free.

## Run it

```bash
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1
```

That's it. The script handles build, launch, polling, assertions, screenshot, cleanup.

## What the script checks (in order)

1. **Build** — runs `dotnet build -c Debug`. Fails with exit code 1.
2. **Launch + server up** — starts the exe, waits up to 20s for `:7777/state` to respond. Exit 2 on timeout.
3. **Acid inactive at start** — first `/state` poll must have `acidActive: false`.
4. **Acid activates within 15s** — with `DebugFastAcid` the start delay is ~3s.
5. **Acid level rises** — polls again 3 s later; `acidLevel` (Y in px) must decrease (smaller Y = higher on screen).
6. **A player takes damage within 45s** — polls until any `player.hp < player.maxHp`.
7. **Screenshot** — fetches `/screenshot` and writes `.smoke-test-screenshot.png` at the repo root for eyeballing.

Failure exit codes:
- `1` build failure
- `2` debug server didn't come up
- `3` lifecycle assertion failed (look at the `FAIL:` line for which one)
- `4` HTTP error talking to the debug server

## Reporting back

If all checks pass: tell the user "Smoke test passed — all 4 lifecycle checks green," mention where the screenshot is, and ask if they want you to view it.

If a check fails: report the failing check verbatim from the script output, and read the most recent `/state` snapshot from `_state.txt` (if present) before suggesting a fix.

## Options

```bash
# Skip build (use already-built Debug binaries)
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -NoBuild

# Custom screenshot path
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -ScreenshotPath C:\tmp\acid.png

# Specify repo root manually
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -RepoRoot C:\path\to\GorelordsBrawler
```

## After the test

The script restores `appsettings.json` to its original contents on success or failure
(including kills via Ctrl+C). The game process is killed even on error paths.

## When NOT to run

- The user hasn't built the game yet and the build itself is broken with errors unrelated to acid.
- The user only wants to run the unit/integration test suites (use `dotnet test` directly).
- The user is on a non-Windows host (script is PowerShell-only; could be ported to Bash but not yet).
