# Big Walk practice mod — Phase 1 notes

Target: Big Walk (Steam appid 1478500), Unity 6000.3.17f1, IL2CPP, metadata v39.
Game version 1.5.1 2608271531 (boot log). Depot: 1478501, manifest
3254088315844693061, Steam buildid 24982892 (pinned 2026-09-18).

## Dev loop (working as of 2026-09-18)

Loader lives in the r2modman profile, NOT the game folder:

- Profile: `%APPDATA%\r2modmanPlus-local\BigWalk\profiles\Default\BepInEx`
  - core/ (BepInEx 6.0.755), interop/ (generated, 154 assemblies), plugins/
- Game folder is vanilla except r2modman's staged doorstop pilot files
  (winhttp.dll, doorstop_config.ini, dotnet/) left from the last launch.
- Launch = r2modman Play (or, verified equivalent, the game exe with
  `--doorstop-enabled true --doorstop-target-assembly <profile>\BepInEx\core\BepInEx.Unity.IL2CPP.dll`).
- Build: `dotnet build <proj> -c Release /p:GamePath=<profile-base>` (interop
  referenced from profile). Plugins go to `plugins\<Name>\` subfolders.
- Script: `build-deploy.ps1` in this repo root (build all + deploy to profile).

Verified: SkipIntro + DevMenu built, deployed, loaded, and executed
(LogOutput.log: "Splash auto-continued.", "Mic check auto-continued.").

Notes / hazards:
- Crash-on-quit observed: game exits with NullReferenceException in EOS
  teardown then AV 0xc0000005 in UnityPlayer.dll. Seen on modded runs;
  attribution to loader vs game bug NOT yet proven (vanilla reference run
  still needed — game folder has doorstop pilot files, so "vanilla" requires
  removing them first).
- Two in-session NREs logged at menu after SkipIntro fired; non-fatal.
- If the game folder BepInEx from a manual install ever reappears, remove it;
  the loader is profile-owned.

## Game internals recovered so far (dummydll + ISIL)

### Spawn path (server, host)
- `HouseNetworkManager : Mirror.NetworkManager` — the game's NetworkManager
  (`Assembly-CSharp`).
  - `OnServerAddPlayer(NetworkConnectionToClient)` — override; body:
    constructs `AddPlayerDelayed(connectionToClient)` coroutine
    (`<AddPlayerDelayed>d__13`) and starts it (ISIL confirms).
  - `AddPlayerDelayed` — coroutine; delays spawn (waits for subscenes —
    has `LoadSubScenes` coroutine + `subScenesAreLoaded` flag, and
    `localPlayerFullyReady` / `LocalPlayerFullyReady` + `OnLocalPlayerFullyReady`
    event).
  - EOS lobby: `CreateHostEosLobbyAsync()` (UniTask), `GetPlayerName()`,
    `GetLocalPlatformUserID()`.
- Mirror API for multi-body-per-connection (in interop Mirror.dll):
  - `NetworkServer.AddPlayerForConnection(conn, player, assetId)` and
    `(conn, player)` — the route to spawn extra bodies on the SAME connection.
  - `NetworkIdentity.AssignClientAuthority(conn)` / `RemoveClientAuthority()`,
    `NetworkBehaviour.hasAuthority`, `connectionToClient`.
  - `NetworkManager.maxConnections`, `NetworkServer.maxConnections`.
  - No `maxPlayers`/`playerPrefab` usage in Assembly-CSharp (grep) — the
    2/3/4-player lobby variant must live elsewhere (lobby config; TBD via ISIL).

### Player / control / camera / voice classes (Assembly-CSharp)
- `PlayerMover` (field `PlayerCharacter pc`) — movement driver.
- `PlayerGround`, `PlayerArms`, `PlayerHands` (`dragJoint` ConfigurableJoint) —
  locomotion primitives (template's climbing backlog uses these).
- `MirrorIgnorancePlayer : NetworkBehaviour, IDissonancePlayer` — Dissonance
  player tracking bound to Mirror (likely per-player via
  `MirrorIgnorancePlayerController : NetworkBehaviour` with an `Update`).
- `PlayerCameraReferences` — holds per-player `Camera playerCamera`; the
  swap candidate for camera follow rebinding.
- `MotionCamera` (has `PlayerZone playerZone`), `MirrorCamera`,
  `CameraTrigger(Controller)`, `CameraQualityManager` — no Cinemachine.
- Dev/cheat survivors (intact logic): `CameraCheatMover` (free cam, F3),
  `SpawnEmCheat` (spawn entities — ISIL verified real body),
  `TrainCheater`, `PlayerCheater` (stubbed Update).
- Input: Rewired (`InputManager`, `RewiredStandaloneInputModule`,
  `PlayerMouseInputModuleAssignation` with `Rewired.Components.PlayerMouse`).
- Voice: Dissonance `VoiceProximityBroadcastTrigger` / `VoiceProximityReceiptTrigger`
  on grid rooms; `MirrorIgnorancePlayer` is the per-player voice object.

### Open questions for Phase 2
- Where the 2/3/4 player count is enforced when hosting (maxConnections? lobby
  config object?) — needed to decide how to add bodies past the cap.
- Which component actually binds Rewired input to the "controlled" body and
  how ownership (hasAuthority) gates it — key for the swap.
- Dissonance: does `MirrorIgnorancePlayer` track a single player object /
  follow the camera or the body? (Swap must move local-speaker cleanly.)
- Quit-crash attribution (loader vs game).
## Spawn flow (recovered, ISIL)

- `OnServerAddPlayer(conn)` real body: logs "on server add player", constructs
  `<AddPlayerDelayed>d__13` state machine (`<>4__this` + `connectionToClient`),
  starts it on the MonoBehaviour. Fire-and-forget coroutine, no count check here.
- `AddPlayerDelayed(conn)` = coroutine factory. `MoveNext` (real): yield once,
  request subscene load (`LoadSubScenes`), Debug-scaffold logging, then drive the
  connection's player spawn (object from `connectionToClient` + static factory
  calls; exact prefab source not yet traced through native thunks).
- **Spawn hook surface for the mod**: patch `HouseNetworkManager.OnServerAddPlayer`
  (or the coroutine) to add extra bodies for the same connection, or call
  `Mirror.NetworkServer.AddPlayerForConnection(localConn, playerPrefab)` directly.

## Player object model

- `PlayerCharacter : NetworkBehaviour` — the single per-body controller root.
  Fields (all public): `hands, arms, tunings, ground, head, mover, looks, poser,
  lips, highlighter, decisions, gestures, croucher, cameraMinder, cheater, caster,
  playerEyes, registry, misc, grease, sleeper, faller, particles, actions, texter,
  sitter, jumper, teeterer, sprinter, feet, beak, platformer, vegetation, collision,
  menu, shepherd, speechless, teacher, dreamer, playerNetworking` — plus
  `inputPlayer` (Rewired.Player), `rb` (Rigidbody), `cameraTransform`,
  `kernal`, `houseNetworkTransform`, `myAnimator`, static
  `List<PlayerCharacter> allPlayerCharacters`, `playerAudio`, `footstepSound`.
- **Swap-relevant trio**:
  - `inputPlayer` — Rewired player driving this body's movement (Update reads it).
  - `cameraTransform` + `PlayerCameraMinder` — per-body camera rig; the
    active-camera selection logic is the rebind target for a swap.
  - `playerNetworking` (PlayerNetworking : NetworkBehaviour) — per-body Mirror
    plumbing (movement replication via `HouseNetworkTransform`).
  - `PlayerRegistry` — shared registry (grab collider + list).
- `PlayerZone`/`PlayerZoner` — zone system keyed to a PlayerCharacter.

## Player count / lobby variant

- `enum PlayerCount { NotSet=0, PlayerCount4=4, PlayerCount3=3, PlayerCount2=2 }`
  — the 2/3/4 lobby choice; synced via `_Write_PlayerCount` (Mirror custom writer),
  i.e. it is part of a networked message (WelcomeMessage/HouseAuthenticator region).
- `PlayerCountSwapper` (static `playerCount`, `target4`), `PlayerCountMenu`,
  `PlayerCountDisplay` — menu + application of the variant.
- `MaxPlayerCount = 12` const exists in a vegetation-culling manager (loop bound),
  NOT obviously the lobby cap; the real lobby-member cap is likely EOS-side
  (lobby attribute). OPEN QUESTION stays: where the 12-cap lives.

## Ownership / swap API (Mirror, verified present in interop)

- `NetworkServer.AddPlayerForConnection(conn, player[, assetId])`
- `NetworkServer.ReplacePlayerForConnection(conn, player[, assetId], keepAuthority)`
- `NetworkServer.RemovePlayerForConnection(conn, destroyServerObject)`
- `NetworkIdentity.AssignClientAuthority(conn)` / `RemoveClientAuthority()`
- `NetworkBehaviour.hasAuthority`, `connectionToClient`
- `NetworkConnection.RemoveOwnedObject(identity)`
- `NetworkClient.localPlayer` (identity; internal set)
- Client spawn path: `SpawnMessage` → `OnSpawn` / `OnHostClientSpawn` →
  `SpawnPrefab`/`ApplySpawnPayload`, `RegisterPrefab`/`RegisterSpawnHandler`
  (`SpawnDelegate`/`SpawnHandlerDelegate`), `NetworkServer.SendSpawnMessage`.

Design implication: a body spawned via `AddPlayerForConnection(localConn, …)`
inherits `hasAuthority` on the host-client → movement input applies locally and
replicates (HostNetworkTransform side) — the swap then only needs input/camera/voice
rebinding, no authority transfer. Verify against Big Solo Walk's approach.
## Spawn recipe (VERIFIED from AddPlayerDelayed disassembly)

Exact call chain inside `AddPlayerDelayed.MoveNext`:

1. yield once (subscenes load), log "attempting delayed add player"
2. auth data check on `connectionToClient` (warns "no auth data" if missing)
3. `Instantiate(<playerPrefab>, position, rotation)` — position/rotation from
   managers (start-position flavoured); prefab is the INHERITED
   `Mirror.NetworkManager.playerPrefab` field (offset 0x58 on the object) —
   no game-side prefab name in Assembly-CSharp, no `playerPrefab` text refs
4. body init call, then `NetworkServer.AddPlayerForConnection(conn, playerGo)`
5. per-player tail: corpse/backpack transfer props ("corpse expiring backpack
   transfer"), registry wiring, second yield.

`OnServerAddPlayer` performs NO player-count check — the 2/3/4 `PlayerCount`
variant caps *connections* (lobby size), not bodies per connection. Extra
bodies on the local connection therefore bypass the variant constraint
(matches Big Solo Walk's "host 2/3/4 lobby, then add extra characters").

## MVP design (from the verified recipe)

Spawn (host, hotkey "add body" or autocount):
```csharp
var nm = (HouseNetworkManager)NetworkManager.singleton;      // interop type
var go = UnityEngine.Object.Instantiate(
    nm.playerPrefab, nm.GetStartPosition().position, Quaternion.identity);
NetworkServer.AddPlayerForConnection(NetworkServer.localConnection, go);
```
Same call the game makes → Mirror-consistent spawn, host authority on the
host-client for every body (AddPlayerForConnection grants it), so movement
replicates without any authority transfer.

Swap (input/camera/voice rebind; no authority change):
- bodies enumerated via `PlayerCharacter.allPlayerCharacters` (static) or
  `NetworkServer.localConnection.PlayerCharacters`-equivalent;
- per-body: `PlayerCharacter.inputPlayer` (Rewired) + the `bypassUpdate /
  bypassLateUpdate / bypassFixedUpdate` flags gate simulation — set on
  inactive bodies;
- camera: `PlayerCameraMinder` + `cameraTransform` — the active-camera
  selection to move; verify names in UE;
- voice: `MirrorIgnorancePlayer`/`MirrorIgnorancePlayerController` per body —
  confirm which object Dissonance tracks and point it at the active body.

Risks to verify at runtime (Unity Explorer, host side):
- `playerPrefab` instantiation position source (start positions count?).
- `onServerAddPlayer` coroutine guard: AddPlayerForConnection during flight;
  patch AFTER the game's own add settles (delay or idempotency via body count).
- deleting extra bodies: Big Solo Walk's known crash on delete — the game's
  player-removal path (corpse conversion) may not tolerate non-connection
  players; leave removal out of v1 (spawn-only + swap), or reuse
  `RemovePlayerForConnection` carefully.
## Implementation status (2026-09-18)

`BigWalk.Practice` v0.1.0 implemented and deployed (build clean against profile
interop):
- `Plugin.cs` — config: MaxExtraBodies, SpawnKey, SwapKey11/12; registers
  `PracticeController` via ClassInjector on a DontDestroyOnLoad host (template's
  DevMenu pattern).
- `PracticeController.cs` — `+`/Keypad+ adds a body via
  `Instantiate(nm.playerPrefab)` + `NetworkServer.AddPlayerForConnection(
  NetworkServer.localConnection, go)`, gated on host + `LocalPlayerFullyReady` +
  max count. Digit keys 1-9/0/11/12 swap: inactive bodies get
  `bypassUpdate/LateUpdate/FixedUpdate = true`; exactly one Camera enabled under
  the active body.
- Pointer-identity compares used (`pc.Pointer`) instead of `==` on Il2Cpp
  proxies.

Playtest checklist (whenever you next host a lobby):
1. Log line `BigWalk.Practice loaded.` confirms load; config file
   `BepInEx\config\com.bigwalk.practice.cfg` appears.
2. Host any 2/3/4 lobby, press `+` — expect "Spawned extra body #1" + the body
   appearing in the world; keep pressing up to max.
3. Press digit keys — expect control to move slot to slot, others frozen.
4. Report: bodies visible/animating? swapping moves camera? voice follows?
   any game errors after spawn/swap (esp. the delete-crash analog - we do NOT
   delete bodies in v1).
## Mirror ownership verified (upstream source, master)

Fetched `MirrorNetworking/Mirror` master `NetworkServer.cs` into
`out/mirror-src/`. Findings that pin the design:

- `AddPlayerForConnection(conn, go)` FAILS when `conn.identity != null`
  ("AddPlayer: player object already exists") — the v0.1 bug: extras were
  silently un-spawned prefab clones, never networked. v0.2 avoids this API
  entirely.
- `ReplacePlayerForConnection(conn, go, keepAuthority=false)` → options
  `KeepActive` (the game's interop exposes the bool overload):
  - `conn.identity = identity; identity.SetClientOwner(conn);`
  - host: `identity.isOwned = true; NetworkClient.InternalAddPlayer(identity)`
    → `NetworkClient.localPlayer` re-points at the new body (the flip our
    pending-switch polls for; synchronous on host but we keep a timeout).
  - OLD body: `previousPlayer.RemoveClientAuthority()` — clears BOTH
    `isLocalPlayer` and `hasAuthority` on the client, object stays alive.
    Switch-back therefore works (re-owning re-sets those flags via
    ChangeOwnerMessage/SpawnMessage).
  - Explicit `Unspawn`/`Destroy` options exist for future body-removal work.
- `RemovePlayerForConnection(conn, options)` — KeepActive: clears
  `connectionToClient` + `conn.owned`, sends ChangeOwnerMessage.

Runtime proof of replication still pending the in-game playtest (v0.2), but
the ownership mechanics are confirmed at the source level.
## Config UI (2026-09-18)

`sinai-dev/BepInExConfigManager` 1.3.0 (IL2CPP CoreCLR) installed into the
profile (`patchers/` + `plugins/`). It auto-discovers BepInEx cfg entries, so
Practice's MaxExtraBodies/SpawnKey/GatherKey (sections "Spawn"/"Keys") show up
with zero code. NOTE: its F1 default collides with DevMenu's MenuKey F1 —
rebind either in cfg (com.bigwalk.devmenu.cfg MenuKey -> F2, or
BepInExConfigManager key in its cfg) to keep both usable.

MirrorIgnorancePlayer ISIL file is absent from the dump output (Cpp2IL
omission) — its voice binding stays an open playtest question, though the
listener (PlayerCameraMinder.listenerMover) is confirmed as the swap point for
what you hear.
## Multi-human mode — design notes (not yet implemented)

Goal: 2-3 real players, each controlling their own set of extra bodies.
Mechanically Mirror wants bodies owned per connection; single-human solo mode
works because there is ONE local connection and ReplacePlayerForConnection is
used between that connection's own bodies.

Minimal viable design:
- The HOST runs the spawn machinery. Extras are created for a specific real
  connection via the SAME ReplacePlayerForConnection call (that connection
  becomes local player of the extra body) — no new spawn path needed.
- Who owns which extra: client sends a custom Mirror message
  (`NetworkClient.RegisterHandler<BodyGrabRequest>` host-side /
  `NetworkServer.RegisterHandler` client-side ops on the host), e.g.
  `RequestBody(slotId, targetNetId)` → host validates (spawned, unclaimed)
  → performs the Replace for that REQUESTING connection → replies
  `BodyAssigned(...)` so both sides update their slot tables.
- Each player's UI/keys drive the same swap controller against the bodies
  owned by THEIR connection (netId lists per connection instead of global).
- Movement: each human's input keys off their own localPlayer identity —
  already how the game works; no extra replication work.

Why it waits: the single-human core (v0.2-v0.4) must be verified first, and
the protocol needs a second human for testing. Blocked on playtest +
playmate.