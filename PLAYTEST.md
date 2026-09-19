# v0.5 Playtest protocol

Session: r2modman launch, host a lobby (2/3/4 any), one run covers all checks.
Log: `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default\BepInEx\LogOutput.log`

## Pre-flight (game closed)
- [ ] Run `build-deploy.ps1` (or confirm profile plugins contain the latest
      BigWalk.Practice.dll)
- [ ] After launch, log shows `Loading [Big Walk — Practice 0.6.0]` — that exact
      version string is the freshness check (previous builds were 0.2.0/0.5.0)
- [ ] F1 conflict: DevMenu `MenuKey` moved off F1 in `BepInEx\config\com.bigwalk.devmenu.cfg`
- [ ] Log should show on launch: `BigWalk.Practice loaded.`

## In-game, in order

1. **Spawn near player** — host, get in the world, press `+`
   - v0.5: if the world is still loading, the first `+` is ignored with a log
     line `World still loading - spawn ignored (press + again shortly)` —
     press again a beat later.
   - EXPECT: body ~1–2 m behind you (NOT the spawn cubbies), randomly colored
     (head/torso/legs can differ), camera snaps to it.
   - WATCH: body animated (idle/breathe)? log: `Spawned body in slot 2; switching control.`
2. **Multiple bodies** — press `+` 3–4 more times
   - EXPECT: staggered ranks behind you, each spawn switches camera to the new body.
   - WATCH: earlier bodies stay standing (remote mode), each with DIFFERENT colors.
3. **Swap back** — press 1, then 4, then 2
   - EXPECT: camera follows each selection; `Switching to …` lines in the log,
     no `Switch timed out` (a timeout line is a FAILURE → report).
   - WATCH: movement felt correct on each body; inactive bodies did not move.
3b. **Fast swaps** — hammer 1 → 3 → 1 → 3 quickly
   - EXPECT: every press lands (inputs queue while a switch is in flight); no hang.
4. **Pick-up** — with body A active, walk to body B and grab it
   - EXPECT: you can carry B like another player.
   - FAILURE: log errors / nothing to grab → tell me which.
5. **Gather (R)** — walk far away, press R
   - EXPECT: all inactive bodies teleport into a staggered line behind you;
     log `Teleported N body(s) to the active player.`
6. **Noclip (NEW)** — press **G**: the active body becomes driveable through
   walls (WASD + Space/Ctrl, Shift = fast). Toggle again to drop.
   - Follows swaps: change bodies while it's on and it moves to the new active.
   - REPORT: clears walls? movement snappy (velocity drive — watch for
     jitter/stuck-in-wall)? falling feels right after toggling off (colliders
     restored — body may drop a beat)?
6. **Checkpoint** — arrange bodies, press **F5** (`Checkpoint saved (N body(s)).`),
   scatter everyone, press **F9** — every body snaps back, active one included.
7. **Voice/listener** — (nice to have, needs a second human) — skip otherwise.

## What to send back
- Anything that didn't match EXPECT above (one line each)
- Any `[Error` / `Practice tick failed` / warning lines (grep: `Practice|error|Warning`)
- Spawn distance / formation spacing feel (tunable in PracticeController)

## Known out of scope (v0.5)
- Deleting bodies (Big Solo Walk's crash source) — restart the lobby instead
- 11–12 slots beyond number-key coverage
- Multi-human mode (design done; waits on core verification)