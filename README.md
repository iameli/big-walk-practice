# Big Walk — Practice

A practice tool for the co-op game [Big Walk](https://bigwalk.game/) (House House / Panic): spawn extra player bodies in a solo or small-hosted walk and hot-swap control between them, so routes that need a full lobby can be rehearsed alone.

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
| `F2` / `F3` | Dev menu / free camera (bigwalk-mods DevMenu) |

All keys are configurable in `BepInEx\config\com.bigwalk.practice.cfg`.

## Configuration

- `SlotNames` — comma-separated role names per slot (e.g. `Bridge-A,Gate-Holder`) shown in logs/overlay
- `ShowNameOverlay` — draw the active slot label in the corner
- `MaxExtraBodies` — 1–9 extras
- `NoclipSpeed`, `NoDrowsy`, and the key binds

## Development

```
.\scripts\export-build-refs.ps1   # zip core+interop refs for CI (after game updates)
.\build-deploy.ps1                # build + deploy into the r2modman profile
```

- BepInEx 6.0.755 (Thunderstore pack) IL2CPP, interop from `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default\BepInEx`
- CI (`.github/workflows/build.yml`) builds against a `build-refs.zip` release asset and republishes the canary DLL on every push. When a Big Walk update lands: run `export-build-refs.ps1`, then `gh release upload practice-canary --clobber dist\ci-refs\build-refs.zip`
- Reverse-engineering notes: `NOTES-phase1.md` (spawn path, Mirror ownership, player object model)
- Playtest protocol: `PLAYTEST.md`
- Template + toolchain: [dougwithseismic/bigwalk-mods](https://github.com/dougwithseismic/bigwalk-mods)

## Credits

- **Big Solo Walk** ([Nexus](https://www.nexusmods.com/bigwalk/mods/42)) by StellaLunyari — the community mod that first solved spawning/swapping; its approach informed this architecture
- [dougwithseismic/bigwalk-mods](https://github.com/dougwithseismic/bigwalk-mods) — BepInEx IL2CPP template, install tooling, modding guide
- BepInEx, Il2CppInterop, Harmony, Mirror (game modding ecosystem)

## License

MIT — see `LICENSE`. Not affiliated with House House or Panic.