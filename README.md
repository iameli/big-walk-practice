# Big Walk Mods

A monorepo of [Big Walk](https://bigwalk.game/) (House House / Panic) practice & utility mods — BepInEx 6 (IL2CPP) plugins with shared build tooling, automated canary releases, and live download links.

## BigWalk.Practice

A practice tool: spawn extra player bodies in a solo or small-hosted walk and hot-swap control between them, so routes that need a full lobby can be rehearsed alone.

- **Spawn** extra bodies next to the active player (`+`), with random multiplayer colors
- **Swap** control instantly (number keys `1`–`0`), camera / audio listener / look mode handed off cleanly
- **Gather** inactive bodies to you (`R`)
- **Checkpoints**: save / restore the formation (`F5` / `F9`)
- **Body noclip** for route scouting (`G`, WASD + Space/Ctrl, Shift fast)
- Named slots + on-screen label (optional overlay), config UI (F1)
- **Host only** — mirror-safe: bodies are spawned through the game's own `ReplacePlayerForConnection` path, so every body is a real networked player

## Download

Automated builds are published to the **practice-canary** release on every commit. Stable URLs:

- https://github.com/iameli/big-walk-practice/releases/latest/download/BigWalk.Practice-canary.zip
- https://github.com/iameli/big-walk-practice/releases/latest/download/BigWalk.Practice.dll

Install with a mod manager (r2modman / Gale): import the zip, or drop `BigWalk.Practice.dll` into `BepInEx\plugins\BigWalk.Practice\` manually.

Requires **BepInEx 6 IL2CPP** (6.0.755 pack or newer bleeding-edge) on the current game build. The game updates frequently; if bodies fail to spawn after a Big Walk update, the interop references have likely drifted — see *Development → references* below.

## Controls

| Key | Action |
| --- | --- |
| `+` / Keypad+ | Spawn a body near the active player; switches control to it |
| `1`–`0` | Switch to slot 1–10 |
| `R` | Teleport all inactive bodies to the active player |
| `F5` / `F9` | Save / restore formation checkpoint |
| `G` | Toggle body noclip (WASD + Space/Ctrl, Shift = fast) |
| `F1` | Config UI (BepInExConfigManager) |
| `F2` / `F3` | Dev menu / free camera (DevMenu mod) |

All keys are configurable in `BepInEx\config\com.bigwalk.practice.cfg`.


## Other mods

More mods (DevMenu, SkipIntro, …) live under `mods\` and get their own canary releases (`<name>-canary`). See `mods\` for the current set.

## Configuration

- `SlotNames` — comma-separated role names per slot (e.g. `Bridge-A,Gate-Holder`) shown in logs/overlay
- `ShowNameOverlay` — draw the active slot label in the corner
- `MaxExtraBodies` — 1–9 extras
- `NoclipSpeed`, `NoDrowsy`, and the key binds

## Development (monorepo)

Layout: every mod is `mods\<Name>\` with a minimal csproj; shared settings
(target framework, BepInEx/Unity/game references) come from the repo-root
`Directory.Build.props`, imported automatically for every mod. Tooling lives
in `scripts\`.

```
.\scripts\build.ps1 -Deploy         # build all mods and deploy into the r2modman profile
.\scripts\new-mod.ps1 -Name Thing   # scaffold a new mod under mods\
.\scripts\export-build-refs.ps1     # zip core+interop refs for CI (re-run after game updates)
.\scripts\install.ps1               # manual game-folder loader install (fallback path)
```

- Refs: BepInEx 6.0.755 (Thunderstore pack) IL2CPP, interop generated in
  `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default\BepInEx`
- CI (`.github/workflows/build.yml`) builds every mod against a
  `build-refs.zip` asset and republishes each mod's canary release on every
  push. After a Big Walk update: run `export-build-refs.ps1`, then
  `gh release upload practice-canary --clobber dist\ci-refs\build-refs.zip`
- Reverse-engineering notes: `NOTES-phase1.md` (spawn path, Mirror ownership, player object model)
- Playtest protocol: `PLAYTEST.md`
- Layout + tooling derived from [dougwithseismic/bigwalk-mods]
  (https://github.com/dougwithseismic/bigwalk-mods); third-party licenses in `docs\licenses\`

## Credits

- **Big Solo Walk** ([Nexus](https://www.nexusmods.com/bigwalk/mods/42)) by StellaLunyari — the community mod that first solved spawning/swapping; its approach informed this architecture
- [dougwithseismic/bigwalk-mods](https://github.com/dougwithseismic/bigwalk-mods) — template, tooling, modding guide (MIT; see `docs\licenses\`)
- BepInEx, Il2CppInterop, Harmony, Mirror (game modding ecosystem)

## License

MIT — see `LICENSE`. Not affiliated with House House or Panic.