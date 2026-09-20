# AGENTS.md

Orientation for agents bootstrapping new Big Walk mods in this monorepo.
Read `README.md` and `docs/modding-guide.md` too — this file is the fast route
to a working build + first test.

## Project facts

- **Game**: Big Walk (House House / Panic), Unity **6000.3.17f1**, **IL2CPP** (metadata v39).
  No managed `Assembly-CSharp.dll` exists — modding means runtime patching through a loader.
- **Loader**: BepInEx 6 **6.0.755** (Thunderstore pack, IL2CPP), installed via the
  r2modman profile `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default`. The game
  folder stays vanilla; r2modman stages doorstop at launch.
- **Networking**: Mirror (host = server+client). **Voice**: Dissonance. **Input**: Rewired,
  but `UnityEngine.Input` (legacy) also works for simple hotkeys. Dev UI: IMGUI works.
- **Interop**: BepInEx generates proxy assemblies in the profile at
  `BepInEx\interop\` (157 assemblies, incl. `Assembly-CSharp.dll`, `Mirror.dll`,
  `DissonanceVoip.dll`). Mods reference those; they regenerate per game build.
- Toolchain on this machine: .NET SDK 10.0.400 (`C:\Program Files\dotnet`), MSVC 14.51 +
  CMake/Ninja (VS 2026) for anything native, Python 3.12 via `py`, ilspycmd global tool,
  Cpp2IL `2022.1.0-pre-release.21` (Il2CppDumper cannot read metadata v39).

## Repo layout

```
Directory.Build.props     # shared: TFM net6.0, BepInEx/Unity/Mirror/game refs (GamePath-overridable)
mods/<Name>/              # one BepInEx plugin per folder; minimal csproj (props do the rest)
scripts/                  # build.ps1 (-Deploy), new-mod.ps1, install.ps1, package.ps1,
                          # export-build-refs.ps1 (CI refs snapshot), common.ps1
docs/                     # modding-guide.md (RE findings), publishing.md, licenses/
NOTES-phase1.md           # decompile evidence: spawn path, Mirror ownership, player object model
PLAYTEST.md               # in-game test protocol for BigWalk.Practice
.github/workflows/build.yml  # per-mod canary release CI
```

## Bootstrap a new mod (90 seconds)

```powershell
.\scripts\new-mod.ps1 -Name Thing      # creates mods\BigWalk.Thing\ (csproj + Plugin.cs + README)
.\scripts\build.ps1 -Deploy            # builds ALL mods, deploys DLLs into the profile's plugins\<Mod>\
```

The scaffold uses the established pattern (see `mods/BigWalk.Practice/Plugin.cs`):

- `BasePlugin` + `[BepInPlugin(Guid, "Big Walk — Thing", "0.1.0")]`; keep `internal static
  ManualLogSource Trace` for logging.
- Per-frame logic: `ClassInjector.RegisterTypeInIl2Cpp<MyBehaviour>()` then
  `host.AddComponent<MyBehaviour>()` on a `DontDestroyOnLoad` GameObject (DevMenu does this).
  Alternatively Harmony-patch a game method (DevMenu/Patches, BigWalk.SkipIntro).
- Config: `Config.Bind(...)` — entries auto-appear in the F1 config UI (BepInExConfigManager)
  and in `BepInEx\config\com.bigwalk.thing.cfg`.
- **Version strings**: bump the `[BepInPlugin]` version per feature slice — the load log line
  (`Loading [Big Walk — Thing 0.1.0]`) is the freshness fingerprint.

## Build → test loop

1. `.\scripts\build.ps1 -Deploy` (needs the profile; errors if interop missing).
2. The HUMAN launches the game via r2modman, then tests in-game (host a lobby).
3. Read `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default\BepInEx\LogOutput.log` for
   plugin load lines and errors. The game prefixes plugin logs `[Info :Big Walk — Thing]`.
4. Runtime inspection: UnityExplorer is installed in the profile (F1 config manager is NOT
   the dev menu — DevMenu is F2, free cam F3).

## Reverse engineering (for new mechanics)

- Dumps/decompiles live in `out\` (gitignored, ~474 MB, regenerable). If absent on a fresh
  checkout, regenerate:
  `vendor\Cpp2IL\Cpp2IL-Windows.exe --game-path "<game>" --exe-name "Big Walk" --output-as dummydll --output-to out\dummy`
  then `ilspycmd` the dummy DLLs; `--output-as isil` for real logic (dummy bodies are throw-null).
- **Policy: never commit `out\` or other decompiled game code** — it's the game's IP and is
  regenerable (`docs/modding-guide.md` documents this).
- Key game-internals already recovered (`NOTES-phase1.md`): player spawn =
  `HouseNetworkManager.OnServerAddPlayer` → `AddPlayerDelayed` coroutine; player-object root =
  `PlayerCharacter` (per-body `inputPlayer` Rewired player, `cameraTransform`,
  `playerNetworking`, `allPlayerCharacters`); the game treats `NetworkClient.localPlayer`
  as "the" player for camera/listener/looks — `NetworkServer.ReplacePlayerForConnection`
  re-points it cleanly; the 2/3/4 `PlayerCount` lobby variant caps *connections*, not bodies.

## CI & releases

- Every push to `main` builds all mods and publishes each to `<Name>-canary`
  (`bigwalk-practice` keeps tag `practice-canary`, which hosts the stable download URLs:
  `releases/latest/download/BigWalk.Practice.dll` + `-canary.zip`). New mods are picked up
  automatically — no workflow edits.
- CI compiles against `build-refs.zip` (BepInEx core+interop snapshot) hosted on
  `practice-canary`. **After a Big Walk update** the interop changes, so:
  `.\scripts\export-build-refs.ps1` then `gh release upload practice-canary --clobber dist\ci-refs\build-refs.zip`.

## Environment quirks (Windows / this setup)

- Windows PowerShell 5.1 only — no `pwsh`. Run scripts via `powershell -NoProfile -ExecutionPolicy
  Bypass -File <path>`; it needs Windows-style paths (forward-slash `-File` fails).
- PowerShell version-pin workarounds are not needed here: the system .NET SDK (10.0.400) is real.
  The template scripts' scoop references are dead paths and safely skipped.
- This box historically had issues launching the game directly; do NOT fight that — code,
  deploy, and ask the human to launch modded via r2modman.
- Deploying to the profile while the game is running has no effect until the next launch.
- Verify builds with `scripts/build.ps1` + hash-compare deployed vs `bin\Release` if in doubt.

## Guardrails

- Commit per feature slice; keep `main` green (CI runs on push). No pushes without the human's ok.
- Don't add new tooling directories or duplicate the shared `Directory.Build.props` — a second
  convention beside the existing one is prohibited.
- Don't commit: `out/`, `vendor/`, `dist/`, `ci-refs/`, `bin/`, `obj/`.
- Mods are MIT (repo license); credit prior art (Big Solo Walk by StellaLunyari, the
  dougwithseismic template) in new mod READMEs.
- Prefer reusing the game's own APIs (teleport = `PlayerGrease.Teleport`, looks =
  `PlayerLooks.SetBodyToLocalMode/RemoteMode`, sleep = `PlayerSleeper.preventSleeping`).
  Dead retail stubs (`ghostMovementScalar`, `CmdSetGhost`) are compiled out — don't build on them.