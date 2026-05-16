---
name: acid-smoke-test
description: Run an end-to-end smoke test of the acid hazard in GorelordsBrawler — launches the game, drives it via the HTTP debug server, asserts the full lifecycle, records a short MP4 of the gameplay, and uploads it to catbox.moe for review. Use after AcidSurface/Fluid/* changes, or whenever the user says "run the smoke test", "smoke test the acid", or asks to verify the acid hazard runs in-game.
allowed-tools: Bash, PowerShell, Read
---

# Acid hazard smoke test

Launches the game in Debug mode with `DebugServer` + `DebugDirectArena` + `DebugFastAcid`,
hits the HTTP debug server on `:7777`, asserts the full acid lifecycle, captures a
screenshot, **records ~20 s of gameplay as an MP4**, and uploads it to catbox.moe so the
URL can go in a PR description as the final visual quality gate.

## Prerequisites

- Game must build in Debug (`dotnet build GorelordsBrawler/GorelordsBrawler.csproj -c Debug`).
- `GorelordsBrawler/DevTools/` must exist (provides `GameDebugServer`, `DebugStateExporter`,
  `DebugCommands`). If missing, the Debug build will fail — recover it from the user's
  local `Debug/` folder (which gets silently swallowed by the `[Dd]ebug/` gitignore rule).
- Port `7777` must be free.
- `ffmpeg` on `PATH` for recording (`winget install Gyan.FFmpeg`). Optional —
  if missing the script will warn and skip the recording + upload.
- Outbound network to `catbox.moe` for the upload.

## Run it

```bash
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1
```

That's it. The script handles build, launch, polling, assertions, recording,
screenshot, upload, cleanup.

## What the script checks (in order)

1. **Build** — `dotnet build -c Debug`. Exit 1 on failure.
2. **Launch + server up** — starts the exe, waits up to 20 s for `:7777/state` to respond. Exit 2 on timeout.
3. **Start recording** — `ffmpeg gdigrab` against the game window's rect (Win32 `GetWindowRect`); records for `RecordSeconds` (default 20). Nez rewrites the window title every frame so we can't use ffmpeg's `title=` match — we capture the rect instead.
4. **Acid inactive at start** — first `/state` poll must have `acidActive: false`.
5. **Acid activates within 15 s** — with `DebugFastAcid` the start delay is ~3 s.
6. **Acid level rises** — polls again 3 s later; `acidLevel` (Y in px) must decrease (smaller Y = higher on screen).
7. **A player takes damage within 45 s** — polls until any `player.hp < player.maxHp`.
8. **Wait for recording** — ffmpeg self-stops via its `-t` flag.
9. **Screenshot** — fetches `/screenshot` and writes `.smoke-test-screenshot.png`.
10. **Upload recording** — POSTs the MP4 to `https://catbox.moe/user/api.php` (free, no expiration). URL is printed and also written to `.smoke-test-recording-url.txt`.

Failure exit codes:
- `1` build failure
- `2` debug server didn't come up
- `3` lifecycle assertion failed (look at the `FAIL:` line for which one)
- `4` HTTP error talking to the debug server

## Reporting back

If all checks pass and the upload succeeded: tell the user "Smoke test passed — 4/4 lifecycle checks green," give them the catbox URL (and offer to embed it in the active PR description), and mention where the local screenshot/MP4 are.

If a check fails: report the failing check verbatim from the script output. Read the local `.smoke-test-screenshot.png` to see the last frame of the game before failure.

## Options

```bash
# Skip build (use already-built Debug binaries)
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -NoBuild

# Skip recording + upload (faster, for tight iteration loops)
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -NoRecord

# Custom recording length (default 20 s)
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -RecordSeconds 30

# Custom output paths
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -ScreenshotPath C:\tmp\acid.png -RecordingPath C:\tmp\acid.mp4

# Specify repo root manually
pwsh .claude/skills/acid-smoke-test/smoke_test.ps1 -RepoRoot C:\path\to\GorelordsBrawler
```

## After the test

The script restores `appsettings.json` to its original contents on success or failure
(including kills via Ctrl+C). Game process AND ffmpeg recording process are killed
on every error path.

Artifacts left at the repo root (all gitignored):
- `.smoke-test-screenshot.png` — single frame near the end of the test
- `.smoke-test-recording.mp4` — full ~20 s gameplay clip
- `.smoke-test-recording-url.txt` — the uploaded catbox URL, ready to paste

## When NOT to run

- The user hasn't built the game yet and the build itself is broken with errors unrelated to acid.
- The user only wants to run the unit/integration test suites (use `dotnet test` directly).
- The user is on a non-Windows host (script is PowerShell + Win32 + gdigrab — Windows-only).
