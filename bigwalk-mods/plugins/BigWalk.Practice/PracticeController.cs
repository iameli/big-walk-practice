using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using Il2CppInterop.Runtime;

namespace BigWalk.Practice;

/// <summary>
/// Per-frame driver: tracks owned bodies by netId in slots (0 = original
/// player, 1..N = extras). Spawn and swap both go through
/// NetworkServer.ReplacePlayerForConnection on the local connection; after the
/// swap, camera/listener/looks ownership is handed to the new local player
/// once Mirror confirms NetworkClient.localPlayer flipped.
/// </summary>
public class PracticeController : MonoBehaviour
{
    private const int PendingSwitchTimeoutFrames = 600;

    private static PracticeController _instance;

    private readonly List<uint> _slots = new();
    private uint _pendingId;
    private int _pendingUntilFrame;
    private bool _reportedNonHost;

    private readonly string[] _slotNames = new string[12];

    private readonly Vector3[] _savedPositions = new Vector3[12];
    private readonly Quaternion[] _savedRotations = new Quaternion[12];
    private bool _hasCheckpoint;

    private int _queuedSlot = -1;

    private uint _pendingRepositionId;
    private Vector3 _pendingRepositionPos;
    private Quaternion _pendingRepositionRot;
    private int _pendingRepositionAt;

    private bool _noclip;
    private uint _noclipTargetId;
    private readonly List<Collider> _noclipColliders = new();
    private Rigidbody _noclipRb;

    private PlayerCharacter _noclipPc;

    private void Awake()
    {
        _instance = this;

        var capacity = Mathf.Clamp(1 + Plugin.MaxExtraBodies.Value, 1, 12);
        for (var i = 0; i < capacity; i++)
        {
            _slots.Add(0);
        }

        var raw = Plugin.SlotNames.Value ?? string.Empty;
        var parts = raw.Split(',');
        for (var i = 0; i < _slotNames.Length; i++)
        {
            _slotNames[i] = i < parts.Length ? parts[i].Trim() : string.Empty;
        }
    }

    private void Update()
    {
        Tick();
    }

    private void Tick()
    {
        try
        {
            if (!NetworkServer.activeHost || NetworkServer.localConnection == null)
            {
                if (!_reportedNonHost)
                {
                    _reportedNonHost = true;
                    Plugin.Trace.LogMessage("Practice works while you are hosting a walk.");
                }

                return;
            }

            var current = NetworkClient.localPlayer;
            if (current == null)
            {
                return;
            }

            // Spawn position telemetry: re-assert the formation spot shortly
            // after spawn and log where the body actually landed.
            if (_pendingRepositionId != 0 && Time.frameCount >= _pendingRepositionAt)
            {
                if (TryGetIdentity(_pendingRepositionId, out var body))
                {
                    var bp = body.GetComponent<PlayerCharacter>();
                    var before = bp != null && bp.rb != null ? bp.rb.position : body.transform.position;
                    TeleportCharacter(bp, _pendingRepositionPos, _pendingRepositionRot);
                    var s = FindSlot(_pendingRepositionId);
                    Plugin.Trace.LogInfo(
                        $"Spawn settle (slot {s + 1}): was at {before}, reasserted to {_pendingRepositionPos} " +
                        $"({Vector3.Distance(before, _pendingRepositionPos):F1}m shift).");
                }

                _pendingRepositionId = 0;
            }

            // Noclip: toggle on the active body and drive it through walls.
            if (Input.GetKeyDown(Plugin.NoclipKey.Value))
            {
                ToggleNoclip(current);
                return;
            }

            if (_noclip)
            {
                UpdateNoclip(current);
            }


            RemoveMissingCharacters();
            RegisterCurrent(current.netId);

            // Re-assert remote/local presentation periodically so the game's
            // own per-frame checks (footsteps, audio listener) agree with the
            // current swap.
            if (Time.frameCount % 90 == 0)
            {
                EnforcePresentation(current);
            }


            if (Input.GetKeyDown(Plugin.GatherKey.Value))
            {
                TeleportAllToActive(current);
                return;
            }

            if (Input.GetKeyDown(Plugin.CheckpointSaveKey.Value))
            {
                SaveCheckpoint();
                return;
            }

            if (Input.GetKeyDown(Plugin.CheckpointRestoreKey.Value))
            {
                RestoreCheckpoint();
                return;
            }


            if (_pendingId != 0)
            {
                if (TryGetRequestedSlot(out var queued))
                {
                    _queuedSlot = queued;
                }

                CompletePendingSwitch(current);
                return;
            }

            if (IsSpawnPressed())
            {
                var target = FindFirstEmptySlot();
                if (target > 0)
                {
                    SpawnAndSelect(current, target);
                }
            }
            else if (TryGetRequestedSlot(out var slot))
            {
                SelectSlot(current, slot);
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Practice tick failed: {e}");
            _pendingId = 0;
            _pendingUntilFrame = 0;
        }
}

    internal static void ResetAll()
    {
        if (_instance != null)
        {
            _instance._slots.Clear();
            _instance._pendingId = 0;

            _instance._pendingRepositionId = 0;
            _instance._pendingUntilFrame = 0;
            _instance._reportedNonHost = false;

            _instance._hasCheckpoint = false;

            _instance._queuedSlot = -1;
        }
    }

    // ---- spawn / switch ---------------------------------------------------

    private void SpawnAndSelect(NetworkIdentity current, int slot)
    {
        if (slot >= _slots.Count || _slots[slot] != 0)
        {
            return;
        }

        var nm = NetworkManager.singleton;
        if (nm == null || nm.playerPrefab == null)
        {
            Plugin.Trace.LogWarning("The player prefab is not available in this scene yet.");
            return;
        }

        // Don't spawn while the world is still settling: the local player can
        // be mid-transition (subscenes loading), and a body placed then lands
        // at a stale reference position.
        var hnm = nm.TryCast<HouseNetworkManager>();
        if (hnm != null && !hnm.LocalPlayerFullyReady)
        {
            Plugin.Trace.LogInfo("World still loading - spawn ignored (press + again shortly).");
            return;
        }

        var oldPc = current.GetComponent<PlayerCharacter>();
        var position = GetFormationPosition(current.transform, slot);
        Plugin.Trace.LogInfo(
            $"Spawn: player at {current.transform.position}, " +
            $"{(Vector3.Distance(current.transform.position, position)):F1}m to spawn spot; " +
            $"ready={(hnm == null ? "n/a" : hnm.LocalPlayerFullyReady.ToString())}; " +
            $"slots {slot + 1} at {position}.");
        var go = UnityEngine.Object.Instantiate(nm.playerPrefab, position, current.transform.rotation);
        if (go == null)
        {
            Plugin.Trace.LogError("The game did not create the extra body.");
            return;
        }

        var identity = go.GetComponent<NetworkIdentity>();
        if (identity == null)
        {
            UnityEngine.Object.Destroy(go);
            Plugin.Trace.LogError("The created body had no NetworkIdentity.");
            return;
        }

        if (!NetworkServer.ReplacePlayerForConnection(NetworkServer.localConnection, go))
        {
            UnityEngine.Object.Destroy(go);
            Plugin.Trace.LogError("Mirror rejected the extra body.");
            return;
        }

        var pc = go.GetComponent<PlayerCharacter>();
        ResetLocalControlState(pc);
        RandomizeLooks(pc);
        try
        {
            if (Plugin.NoDrowsy.Value && pc?.sleeper != null)
            {
                pc.sleeper.preventSleeping = true;
                pc.sleeper.timeTilSleep = float.MaxValue;
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Spawn drowsy guard failed: {e.Message}");
        }
        MakeRemote(oldPc);
        _slots[slot] = identity.netId;
        BeginPendingSwitch(identity.netId);
        // The game may snap a freshly spawned body to a fixed spot (wake-up /
        // start position logic). Re-assert the formation spot shortly after
        // spawn and log the delta.
        _pendingRepositionId = identity.netId;
        _pendingRepositionPos = position;
        _pendingRepositionRot = current.transform.rotation;
        _pendingRepositionAt = Time.frameCount + 30;

        var label = SlotLabel(SlotName(slot));
        Plugin.Trace.LogMessage($"Spawned body in slot {slot + 1}{label}; switching control.");
    }

    private void SelectSlot(NetworkIdentity current, int slot)
    {
        if (slot < 0 || slot >= _slots.Count || _slots[slot] == 0)
        {
            Plugin.Trace.LogMessage($"Slot {slot + 1} has no body yet (press {Plugin.SpawnKey.Value}/+ to spawn).");
            return;
        }

        var id = _slots[slot];
        if (id == current.netId)
        {
            return;
        }

        if (!TryGetIdentity(id, out var identity))
        {
            Plugin.Trace.LogWarning($"Slot {slot + 1} is not currently available.");
            return;
        }

        SwitchTo(current, identity);
    }

    private void SwitchTo(NetworkIdentity current, NetworkIdentity target)
    {
        var currentPc = current.GetComponent<PlayerCharacter>();

        if (!NetworkServer.ReplacePlayerForConnection(NetworkServer.localConnection, target.gameObject))
        {
            Plugin.Trace.LogError("Mirror rejected the switch.");
            return;
        }

        ResetLocalControlState(target.GetComponent<PlayerCharacter>());
        MakeRemote(currentPc);
        BeginPendingSwitch(target.netId);

        var slot = FindSlot(target.netId);
        var label = slot >= 0 ? $"slot {slot + 1}{SlotLabel(SlotName(slot))}" : $"body (netid {target.netId})";
        Plugin.Trace.LogMessage($"Switching to {label}.");
    }

    // ---- deferred handoff ------------------------------------------------

    private void BeginPendingSwitch(uint targetId)
    {
        _pendingId = targetId;
        _pendingUntilFrame = Time.frameCount + PendingSwitchTimeoutFrames;
    }

    private void CompletePendingSwitch(NetworkIdentity current)
    {
        var currentPc = current.GetComponent<PlayerCharacter>();

        if (current.netId == _pendingId)
        {
            MakeLocal(currentPc);
            EnforcePresentation(current);
        }
        else if (Time.frameCount > _pendingUntilFrame)
        {
            Plugin.Trace.LogWarning("Switch timed out; staying on the current body.");
            MakeLocal(currentPc);
        }
        else
        {
            return;
        }

        _pendingId = 0;
        _pendingUntilFrame = 0;

        var queued = _queuedSlot;
        _queuedSlot = -1;
        if (queued >= 0)
        {
            SelectSlot(current, queued);
        }
    }

    private static void RandomizeLooks(PlayerCharacter pc)
    {
        if (pc == null)
        {
            return;
        }

        try
        {
            var looks = pc.looks;
            var pn = pc.playerNetworking;
            if (looks == null || pn == null)
            {
                return;
            }

            pn.ServerSetLook(looks.GetRandomLookId(), PlayerLooks.LookPart.Head, saveChange: false);
            pn.ServerSetLook(looks.GetRandomLookId(), PlayerLooks.LookPart.Torso, saveChange: false);
            pn.ServerSetLook(looks.GetRandomLookId(), PlayerLooks.LookPart.Legs, saveChange: false);
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Randomize looks failed: {e.Message}");
        }
    }

    // ---- local / remote body state ---------------------------------------

    private static void MakeRemote(PlayerCharacter pc)
    {
        if (pc == null)
        {
            return;
        }

        ClearMovementControlState(pc);

        try
        {
            pc.cameraMinder?.ReturnCamera();
            SetListenerEnabled(pc, enabled: false);
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Return camera failed: {e.Message}");
        }

        try
        {
            pc.looks?.SetBodyToRemoteMode();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Remote look failed: {e.Message}");
        }
    }

    private static void MakeLocal(PlayerCharacter pc)
    {
        if (pc == null)
        {
            return;
        }

        try
        {
            pc.decisions?.ClearWindUp();
            if (pc.head != null)
            {
                pc.head.runningTotalLookSpin = 0f;
                pc.head.SetHeadStateLocal();
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Local head state failed: {e.Message}");
        }

        try
        {
            pc.looks?.SetBodyToLocalMode();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Local look failed: {e.Message}");
        }

        try
        {
            SetListenerEnabled(pc, enabled: true);
            pc.cameraMinder?.TakeCamera();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Take camera failed: {e.Message}");
        }
    }

    private static void ResetLocalControlState(PlayerCharacter pc)
    {
        if (pc == null)
        {
            return;
        }

        ClearMovementControlState(pc);
        try
        {
            if (pc.jumper != null)
            {
                pc.jumper.justJumped = false;
                pc.jumper.Jumpness = 0f;
            }

            pc.croucher?.ClearToggles();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Reset control toggles failed: {e.Message}");
        }
    }

    private static void ClearMovementControlState(PlayerCharacter pc)
    {
        if (pc == null)
        {
            return;
        }

        try
        {
            if (pc.rb != null)
            {
                pc.rb.linearVelocity = new Vector3(0f, pc.rb.linearVelocity.y, 0f);
                pc.rb.angularVelocity = Vector3.zero;
            }

            if (pc.mover != null)
            {
                pc.mover.localControlsVelocity = Vector3.zero;
                pc.mover.ResetPosition();
            }

            if (pc.sprinter != null)
            {
                pc.sprinter.isSprinting = false;
            }

            if (pc.playerNetworking != null)
            {
                pc.playerNetworking.NetworkcontrolsVelocity = Vector3.zero;
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Clear movement failed: {e.Message}");
        }
    }

    private static void SetListenerEnabled(PlayerCharacter pc, bool enabled)
    {
        var lm = pc?.cameraMinder?.listenerMover;
        if (lm == null)
        {
            return;
        }

        if (!enabled)
        {
            var localLm = NetworkClient.localPlayer?.GetComponent<PlayerCharacter>()?.cameraMinder?.listenerMover;
            if (localLm != null && lm == localLm)
            {
                return;
            }
        }

        lm.enabled = enabled;
    }

    /// <summary>
    /// Re-assert per-body presentation: footstep ownership and audio-listener
    /// enablement, so the game's bookkeeping matches the swap.
    /// </summary>
    private void EnforcePresentation(NetworkIdentity active)
    {
        if (active == null)
        {
            return;
        }

        var activeList = active.GetComponent<PlayerCharacter>()?.cameraMinder?.listenerMover;
        foreach (var id in _slots)
        {
            if (!TryGetIdentity(id, out var identity))
            {
                continue;
            }

            var pc = identity.GetComponent<PlayerCharacter>();
            var lm = pc?.cameraMinder?.listenerMover;
            if (lm == null)
            {
                continue;
            }

            if (identity.netId == active.netId)
            {
                lm.enabled = true;
            }
            else if (activeList == null || lm != activeList)
            {
                lm.enabled = false;
            }

            // Practice mode: keep the anti-AFK drowsy state off for every owned
            // body (the game drives it from local-player input, which only ever
            // reaches the active body).
            try
            {
                var sl = pc?.sleeper;
                if (Plugin.NoDrowsy.Value && sl != null)
                {
                    if (!sl.preventSleeping)
                    {
                        sl.preventSleeping = true;
                    }

                    sl.timeTilSleep = float.MaxValue;
                }
            }
            catch (Exception e)
            {
                Plugin.Trace.LogDebug($"Drowsy suppression failed: {e.Message}");
            }


            // Footsteps of remote bodies should use the remote-audio path.
            try
            {
                var remote = FootstepSound._remotePlayers;
                var fs = pc?.footstepSound;
                if (fs == null)
                {
                    continue;
                }

                var isRemote = identity.netId != active.netId;
                var present = remote.Contains(fs);
                if (isRemote && !present)
                {
                    remote.Add(fs);
                }
                else if (!isRemote && present)
                {
                    remote.Remove(fs);
                }
            }
            catch (Exception e)
            {
                Plugin.Trace.LogDebug($"Footstep ownership failed: {e.Message}");
            }
        }
    }

    // ---- teleport ---------------------------------------------------------

    private void TeleportAllToActive(NetworkIdentity current)
    {
        var moved = 0;
        for (var i = 1; i < _slots.Count; i++)
        {
            var id = _slots[i];
            if (id == 0 || id == current.netId || !TryGetIdentity(id, out var identity))
            {
                continue;
            }

            var pc = identity.GetComponent<PlayerCharacter>();
            TeleportCharacter(pc, GetFormationPosition(current.transform, i), current.transform.rotation);
            moved++;
        }

        Plugin.Trace.LogMessage($"Teleported {moved} body(s) to the active player.");
    }

    private static void TeleportCharacter(PlayerCharacter pc, Vector3 position, Quaternion rotation)
    {
        if (pc == null)
        {
            return;
        }

        try
        {
            if (pc.grease != null)
            {
                pc.grease.Teleport(position, rotation);
            }
            else if (pc.rb != null)
            {
                pc.rb.position = position;
                pc.rb.rotation = rotation;
            }
            else
            {
                pc.transform.SetPositionAndRotation(position, rotation);
            }

            if (pc.rb != null)
            {
                pc.rb.linearVelocity = Vector3.zero;
                pc.rb.angularVelocity = Vector3.zero;
            }
        }
        catch (Exception e)
        {
            Plugin.Trace.LogDebug($"Teleport failed: {e.Message}");
        }
    }

    // ---- slots / lookup ---------------------------------------------------

    private void RegisterCurrent(uint netId)
    {
        if (netId == 0 || FindSlot(netId) >= 0)
        {
            return;
        }

        if (_slots[0] == 0)
        {
            _slots[0] = netId;
            return;
        }

        var empty = FindFirstEmptySlot();
        if (empty > 0)
        {
            _slots[empty] = netId;
        }
    }

    private int FindSlot(uint netId)
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] == netId)
            {
                return i;
            }
        }

        return -1;
    }

    private bool IsSpawnPressed()
    {
        if (Input.GetKeyDown(Plugin.SpawnKey.Value) || Input.GetKeyDown(KeyCode.KeypadPlus))
        {
            return true;
        }

        return Input.inputString.IndexOf('+') >= 0;
    }

    private static bool TryGetRequestedSlot(out int slot)
    {
        if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) { slot = 0; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) { slot = 1; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) { slot = 2; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) { slot = 3; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Keypad5)) { slot = 4; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha6) || Input.GetKeyDown(KeyCode.Keypad6)) { slot = 5; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha7) || Input.GetKeyDown(KeyCode.Keypad7)) { slot = 6; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha8) || Input.GetKeyDown(KeyCode.Keypad8)) { slot = 7; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha9) || Input.GetKeyDown(KeyCode.Keypad9)) { slot = 8; return true; }
        if (Input.GetKeyDown(KeyCode.Alpha0)) { slot = 9; return true; }

        slot = -1;
        return false;
    }

    private int FindFirstEmptySlot()
    {
        for (var i = 1; i < _slots.Count; i++)
        {
            if (_slots[i] == 0)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemoveMissingCharacters()
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            var id = _slots[i];
            if (id == 0 || id == _pendingId)
            {
                continue;
            }

            if (!TryGetIdentity(id, out _))
            {
                _slots[i] = 0;
            }
        }
    }

    private static bool TryGetIdentity(uint netId, out NetworkIdentity identity)
    {
        identity = null;
        if (netId != 0 && NetworkServer.spawned != null && NetworkServer.spawned.TryGetValue(netId, out identity))
        {
            return identity != null;
        }

        return false;
    }

    /// <summary>Formation offsets around the active body (staggered ranks behind it).</summary>
    private static Vector3 GetFormationPosition(Transform active, int slot)
    {
        if (active == null || slot < 0)
        {
            return Vector3.zero;
        }

        var rank = (slot + 2) / 2;          // first pair rank 1, etc.
        var side = (slot % 2 == 1) ? -1f : 1f;
        var offset = new Vector3(
            side * 0.85f * rank,
            0.15f,
            -1.1f - (rank - 1) * 1.15f);

        return active.position + active.right * offset.x + Vector3.up * offset.y + active.forward * offset.z;
    }


    private void SaveCheckpoint()
    {
        var count = 0;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!TryGetIdentity(_slots[i], out var identity))
            {
                continue;
            }

            var pc = identity.GetComponent<PlayerCharacter>();
            _savedPositions[i] = pc?.rb != null ? pc.rb.position : pc.transform.position;
            _savedRotations[i] = pc?.rb != null ? pc.rb.rotation : pc.transform.rotation;
            count++;
        }

        _hasCheckpoint = count > 0;
        Plugin.Trace.LogMessage(_hasCheckpoint
            ? $"Checkpoint saved ({count} body(s))."
            : "No bodies to checkpoint yet.");
    }

    private void RestoreCheckpoint()
    {
        if (!_hasCheckpoint)
        {
            Plugin.Trace.LogMessage("No checkpoint to restore (save with F5 first).");
            return;
        }

        var count = 0;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (!TryGetIdentity(_slots[i], out var identity))
            {
                continue;
            }

            TeleportCharacter(identity.GetComponent<PlayerCharacter>(), _savedPositions[i], _savedRotations[i]);
            count++;
        }

        Plugin.Trace.LogMessage($"Restored checkpoint ({count} body(s)).");
    }


    private void ToggleNoclip(NetworkIdentity current)
    {
        if (!_noclip)
        {
            if (StartNoclip(current))
            {
                Plugin.Trace.LogMessage("Noclip ON (WASD + Space/Ctrl, Shift = fast).");
            }

            return;
        }

        _noclip = false;
        RestoreNoclip();
        Plugin.Trace.LogMessage("Noclip OFF.");
    }

    private bool StartNoclip(NetworkIdentity target)
    {
        var pc = target?.GetComponent<PlayerCharacter>();
        if (pc == null || pc.rb == null)
        {
            Plugin.Trace.LogWarning("No active body to noclip.");
            return false;
        }

        _noclipTargetId = target.netId;
        _noclipRb = pc.rb;
        _noclipPc = pc;
        pc.bypassFixedUpdate = true;
        _noclipColliders.Clear();
        foreach (var c in target.gameObject.GetComponentsInChildren<Collider>(true))
        {
            if (c != null && c.enabled)
            {
                _noclipColliders.Add(c);
                c.enabled = false;
            }
        }

        _noclip = true;
        return true;
    }

    private void RestoreNoclip()
    {
        foreach (var c in _noclipColliders)
        {
            if (c != null)
            {
                c.enabled = true;
            }
        }

        _noclipColliders.Clear();
        if (_noclipRb != null)
        {
            _noclipRb.linearVelocity = Vector3.zero;
            _noclipRb.angularVelocity = Vector3.zero;
        }

        if (_noclipPc != null)
        {
            _noclipPc.bypassFixedUpdate = false;
            _noclipPc = null;
        }

        _noclipRb = null;
        _noclipTargetId = 0;
    }

    private void UpdateNoclip(NetworkIdentity current)
    {
        // Follow swaps: if the active body changed, move the effect onto it.
        if (current == null || current.netId != _noclipTargetId)
        {
            RestoreNoclip();
            if (current == null || !StartNoclip(current))
            {
                _noclip = false;
                return;
            }
        }

        var rb = _noclipRb;
        if (rb == null)
        {
            return;
        }

        var dir = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) dir += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) dir -= Vector3.forward;
        if (Input.GetKey(KeyCode.D)) dir += Vector3.right;
        if (Input.GetKey(KeyCode.A)) dir -= Vector3.right;
        if (Input.GetKey(KeyCode.Space)) dir += Vector3.up;
        if (Input.GetKey(KeyCode.LeftControl)) dir -= Vector3.up;

        if (dir.sqrMagnitude > 0f)
        {
            var pc = _noclipPc;
            var cam = Camera.main;
            var look = pc != null && pc.cameraTransform != null ? pc.cameraTransform
                     : cam != null ? cam.transform : null;
            var yaw = look != null ? look.eulerAngles.y : 0f;
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var speed = Plugin.NoclipSpeed.Value * (Input.GetKey(KeyCode.LeftShift) ? 3f : 1f);
            rb.linearVelocity = rot * dir.normalized * speed;
        }
        else
        {
            rb.linearVelocity = Vector3.zero;
        }

        if (Time.frameCount % 150 == 0)
        {
            var pc2 = _noclipPc;
            var cam2 = Camera.main;
            var look2 = pc2 != null && pc2.cameraTransform != null ? pc2.cameraTransform : null;
            Plugin.Trace.LogInfo(
                $"Noclip diag: dir={dir}, yaw={(look2 != null ? look2.eulerAngles.y : (cam2 != null ? cam2.transform.eulerAngles.y : 0f)):F1}, " +
                $"look={(look2 != null ? "pc-cam" : (cam2 != null ? "main" : "none"))}, vel={rb.linearVelocity}, " +
                $"bypassFixed={pc2 != null && pc2.bypassFixedUpdate}");
        }
    }

    private string SlotName(int slot)
    {
        return slot >= 0 && slot < _slotNames.Length ? _slotNames[slot] : string.Empty;
    }

    private static string SlotLabel(string name)
    {
        return string.IsNullOrEmpty(name) ? string.Empty : $" [{name}]";
    }

    private void OnGUI()
    {
        if (!Plugin.ShowNameOverlay.Value || !NetworkServer.activeHost)
        {
            return;
        }

        try
        {
            var current = NetworkClient.localPlayer;
            if (current == null)
            {
                return;
            }

            var slot = FindSlot(current.netId);
            GUI.Label(new Rect(20f, 20f, 500f, 24f), $"Active slot {slot + 1}{SlotLabel(SlotName(slot))}");
        }
        catch
        {
            // overlay is optional; never crash the frame
        }
    }
}