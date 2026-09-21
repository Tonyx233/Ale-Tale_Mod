using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TonyMods
{
    public sealed class TavernHorse : MonoBehaviour
    {
        private string Channel;
        public string Id { get; private set; }
        public string SceneName { get; private set; }
        public int Variant { get; private set; }
        private float Extension { get { return HorseVariant.RearExtension(Variant); } }
        private HorseModel model;
        private static readonly List<TavernHorse> all = new List<TavernHorse>();
        public static IEnumerable<TavernHorse> All { get { return all; } }
        public bool HasRider(ulong id) { return seats.Find(id) >= 0; }
        public bool TryGetRiderSeat(ulong id, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero; rotation = Quaternion.identity;
            int seat = seats.Find(id);
            if (seat < 0 || cart == null || !cart.activeInHierarchy) return false;
            rotation = cart.transform.rotation;
            float bob = model == null ? 0 : model.SaddleBob;
            position = cart.transform.TransformPoint(offsets[seat] + Vector3.up * (2.13f + bob));
            return true;
        }
        public Vector3 Position { get { return vehiclePosition; } }
        public float Yaw { get { return vehicleRotation.eulerAngles.y; } }
        private const float MountRange = 3f, CartRadius = 0.55f;
        private static TavernHorse Active
        {
            get { foreach (var h in all) if (h.localSeat >= 0) return h; return null; }
        }
        private static TavernHorse Nearest
        {
            get { TavernHorse best = null; float distance = MountRange; foreach (var h in all) { if (h.cart == null || PlayerNet.Instance == null || h.SceneName != SceneManager.GetActiveScene().name) continue; float d = h.MountDistance(PlayerNet.Instance.transform.position); if (d < distance) { distance = d; best = h; } } return best; }
        }
        private HorseSeats seats = new HorseSeats();
        private readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastRequest = new Dictionary<ulong, float>();
        private Vector3[] offsets;
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<KeyCode> mountKey;
        private ConfigEntry<float> cruise, boost;
        private NetworkManager network;

        private ManualLogSource log;
        private GameObject cart;
        private BoxCollider parkedCollider;
        private PlayerNet rider;
        private PlayerMovement movement;
        private CharacterController controller;
        private float originalRadius, originalViewOffset, passengerPitch, passengerYaw;
        private bool controllerEnabled;
        private Vector3 lastPosition, vehiclePosition;
        private Quaternion vehicleRotation = Quaternion.identity;
        private int localSeat = -1, consumeFrame = -1, serial, receivedSerial = -1, cartScene = -1;
        private float nextHello, nextState, noticeUntil, lastStateAt, driverGraceUntil;
        private float fallSpeed;

        private string notice = "";
        [Serializable] private sealed class Wire
        {
            public int op, revision;
            public string occupants, message;
            public bool exists;
            public Vector3 position;
            public float yaw;
        }

        public void Initialize(ConfigFile config, ManualLogSource logger, NetworkManager net, string id, string scene, Vector3 position, float yaw, int variant = HorseVariant.Original)
        {
            Variant = variant; seats = new HorseSeats(HorseVariant.Seats(variant));
            offsets = new Vector3[seats.Capacity];
            for (int i = 0; i < offsets.Length; i++) offsets[i] = new Vector3(0, 0, HorseVariant.SeatZ(i));
            log = logger; Id = id; SceneName = scene; Channel = "Tony.Horse.v090." + id;
            enabledSetting = config.Bind("Horse", "Enabled", true, "Shared two-seat horses. All riders and host need this version.");
            mountKey = config.Bind("Horse", "MountKey", KeyCode.E, "Mount or dismount the closest horse.");
            cruise = config.Bind("Cart", "SpeedMultiplier", 1.8f);
            boost = config.Bind("Cart", "SprintMultiplier", 2.4f);
            Bind(net); vehiclePosition = position; vehicleRotation = Quaternion.Euler(0,yaw,0);
            BuildCart(); cart.transform.SetPositionAndRotation(position,vehicleRotation); all.Add(this);
        }
        public static void InstallPatches(Harmony patches)
        {
            patches.Patch(AccessTools.Method(typeof(PlayerMovement), "HandleCharacterMovement"), prefix: new HarmonyMethod(typeof(TavernHorse), "BeforeMove"), finalizer: new HarmonyMethod(typeof(TavernHorse), "AfterMove"));
            foreach (string name in new[] { "GetJumpInputDown", "GetJumpInputHeld", "GetDashInputDown", "GetCrouchInputDown", "GetCrouchInputHeld", "GetDropInputDown", "GetAutoRunInputDown", "GetUseInputDown", "GetUseInput", "GetUseInputUp" })
                patches.Patch(AccessTools.Method(typeof(PlayerInput), name), prefix: new HarmonyMethod(typeof(TavernHorse), "FilterAction"));
        }
        private static bool CanInput()
        {
            return Time.timeScale > 0 && PlayerInput.Instance != null && PlayerInput.Instance.CanProcessInput() && PlayerMovement.Instance != null && PlayerMovement.Instance.canMove && !PlayerMovement.Instance.isDead;
        }
        private PlayerNet Player(ulong id)
        {
            PlayerNet player;
            return PlayerManager.Instance != null && PlayerManager.Instance.players.TryGetValue(id, out player) ? player : null;
        }
        private static bool Alive(PlayerNet p) { return p != null && p.IsSpawned && p.hp != null && p.hp.Value > 0; }
        private void Bind(NetworkManager net)
        {
            Unbind(); network = net;
            network.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
            nextHello = nextState = 0;
            lastStateAt = Time.unscaledTime;
        }
        private void Unbind()
        {
            if (network != null && network.CustomMessagingManager != null) network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            RestoreRider(); RemoveCart(); seats.Clear(); peers.Clear(); lastRequest.Clear();
            network = null; receivedSerial = -1; serial = 0;
        }
        private void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (!enabledSetting.Value || net == null || !net.IsListening || PlayerNet.Instance == null || !PlayerNet.Instance.IsSpawned)
            { RestoreRider(); return; }
            if (network != net) return;
            if (Time.unscaledTime >= nextHello) { nextHello = Time.unscaledTime + 1; Request(0); }
            if (network.IsServer)
            {
                for (int i = 0; i < seats.Capacity; i++)
                {
                    ulong id = seats[i]; float seen;
                    if (id != HorseSeats.Empty && (!Alive(Player(id)) || (id != network.LocalClientId && (!peers.TryGetValue(id, out seen) || Time.unscaledTime - seen > 5)))) seats.Remove(id);
                }
                FollowDriver();
                if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + (seats.Count > 0 ? 0.1f : 1f); Broadcast(); }
            }
            else if (Time.unscaledTime - lastStateAt > 5)
            { RestoreRider(); seats.Clear(); receivedSerial = -1; Tell("Waiting for a compatible horse host..."); }
            ApplySeat();
            if (!CanInput()) return;
            if (localSeat >= 0 && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            {
                for (int seat = 0; seat < seats.Capacity; seat++)
                    if (Input.GetKeyDown((KeyCode)((int)KeyCode.F1 + seat))) Request(7 + seat);
            }
            if (Input.GetKeyDown(mountKey.Value) && (localSeat >= 0 || (Active == null && Nearest == this)))
            { consumeFrame = Time.frameCount; Request(localSeat >= 0 ? 2 : 1); }
        }
        private void Request(int op)
        {
            if (network == null) return;
            if (network.IsServer) ServerRequest(network.LocalClientId, new Wire { op = op });
            else Send(NetworkManager.ServerClientId, new Wire { op = op });
        }
        private void Send(ulong target, Wire packet)
        {
            string json = HorseJson.Serialize(packet);
            using (FastBufferWriter writer = new FastBufferWriter(4096, Allocator.Temp))
            { writer.WriteValueSafe(json); network.CustomMessagingManager.SendNamedMessage(Channel, target, writer, NetworkDelivery.ReliableSequenced); }
        }
        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (reader.Length > 4096) return;
                string json; reader.ReadValueSafe(out json, false);
                if (json.Length > 1800) return;
                Wire packet = HorseJson.Deserialize<Wire>(json);
                if (packet == null) return;
                if (network.IsServer) ServerRequest(sender, packet);
                else if (sender == NetworkManager.ServerClientId)
                {
                    if (packet.op == 4 && packet.revision > receivedSerial)
                    {
                        if (!Finite(packet.position) || !Finite(packet.yaw) || !seats.Decode(packet.occupants)) return;
                        receivedSerial = packet.revision; lastStateAt = Time.unscaledTime;
                        vehiclePosition = packet.position; vehicleRotation = Quaternion.Euler(0, packet.yaw, 0);
                        if (packet.exists && cart == null) { BuildCart(); cart.transform.SetPositionAndRotation(vehiclePosition, vehicleRotation); cartScene = PlayerNet.Instance.gameObject.scene.handle; }
                        if (!packet.exists) { RestoreRider(); RemoveCart(); }
                        ApplySeat();
                    }
                    else if (packet.op == 5 && Finite(packet.position)) ExitAt(packet.position);
                    else if (packet.op == 6) Tell(packet.message);
                }
            }
            catch (Exception ex) { log.LogWarning("Horse message rejected: " + ex.Message); }
        }
        private static bool Finite(float v) { return !Single.IsNaN(v) && !Single.IsInfinity(v); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private void ServerRequest(ulong sender, Wire packet)
        {
            bool connected = sender == network.LocalClientId;
            foreach (ulong id in network.ConnectedClientsIds) if (id == sender) connected = true;
            if (!connected || !(packet.op == 0 || packet.op == 1 || packet.op == 2 || (packet.op >= 7 && packet.op < 7 + seats.Capacity))) return;
            if (packet.op == 0) { peers[sender] = Time.unscaledTime; return; }
            if (!peers.ContainsKey(sender)) return;
            float previous;
            if (lastRequest.TryGetValue(sender, out previous) && Time.unscaledTime - previous < 0.25f) return;
            lastRequest[sender] = Time.unscaledTime;
            PlayerNet player = Player(sender);
            if (!Alive(player)) return;
            if (packet.op >= 7 && HasRider(sender) && IsAirborne())
            { Note(sender, "Wait until the horse lands before changing seats."); return; }
            if (packet.op >= 7)
            {
                int target = packet.op - 7;
                if (!seats.TrySwitch(sender, target)) { Note(sender, "Seat occupied, or you are not riding this horse."); return; }
                if (target == 0) driverGraceUntil = Time.unscaledTime + .75f;
                if (sender == network.LocalClientId) ApplySeat();
            }
            else if (packet.op == 1)
            {
                if (cart == null) { Note(sender, "Horse is not ready. Try again."); return; }
                if (MountDistance(player.transform.position) > MountRange)
                { Note(sender, "Move closer to the horse (within 3m)."); return; }
                foreach (var other in all) if (other != this && other.HasRider(sender)) return;
                if (MountPathBlocked(player))
                { Note(sender, "Path to horse is blocked. Approach from another side."); return; }
                bool newRider = seats.Find(sender) < 0;
                int assigned = seats.Board(sender);
                if (assigned < 0) Note(sender, "Horse is full (" + seats.Capacity + "/" + seats.Capacity + ").");
                else if (assigned == 0 && newRider) driverGraceUntil = Time.unscaledTime + 0.75f;
                if (parkedCollider != null) parkedCollider.enabled = seats.Count == 0;
                if (sender == network.LocalClientId) ApplySeat();
                if (assigned >= 0) log.LogInfo("Horse mount accepted: horse=" + Id + "; client=" + sender + "; seat=" + (assigned + 1));
            }
            else if (packet.op == 2 && seats.Find(sender) >= 0)
            {
                Vector3 exit;
                if (!FindExit(player, out exit)) { Note(sender, "No safe exit. Move the horse to open ground."); return; }
                seats.Remove(sender);
                if (sender == network.LocalClientId) ExitAt(exit);
                else Send(sender, new Wire { op = 5, position = exit });
            }
            Broadcast();
        }
        private Vector3 MountPoint(Vector3 player)
        {
            if (Extension == 0) return vehiclePosition;
            Vector3 local = Quaternion.Inverse(vehicleRotation) * (player - vehiclePosition);
            return vehiclePosition + vehicleRotation * new Vector3(0, 0, Mathf.Clamp(local.z, offsets[offsets.Length-1].z, offsets[0].z));
        }
        private float MountDistance(Vector3 player) { return Vector3.Distance(player, MountPoint(player)); }
        private bool HorseObstacle(Transform obstacle)
        {
            if (obstacle == null || obstacle.IsChildOf(cart.transform)) return false;
            for (int i = 0; i < seats.Capacity; i++)
            {
                if (seats[i] == HorseSeats.Empty) continue;
                PlayerNet occupant = Player(seats[i]);
                if (occupant != null && obstacle.IsChildOf(occupant.transform)) return false;
            }
            return true;
        }
        private bool ExtendedPathClear(Vector3 position, Quaternion rotation)
        {
            // Test the whole long body while translating AND turning, not only the driver capsule.
            float travel = Vector3.Distance(position, vehiclePosition) + Quaternion.Angle(vehicleRotation, rotation) * Mathf.Deg2Rad * (1.35f + Extension);
            if (travel < .0001f) return true;
            int steps = Mathf.CeilToInt(travel / .15f);
            if (steps > 128) return false;
            for (int i = 1; i <= steps; i++)
            {
                float t = (float)i / steps;
                Quaternion facing = Quaternion.Slerp(vehicleRotation, rotation, t);
                Vector3 center = Vector3.Lerp(vehiclePosition, position, t) + facing * new Vector3(0, 1.4f, -Extension*.5f);
                foreach (Collider obstacle in Physics.OverlapBox(center, new Vector3(.46f, .85f, 1.35f+Extension*.5f), facing,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (HorseObstacle(obstacle.transform)) return false;
            }
            return true;
        }
        private bool MountPathBlocked(PlayerNet player)
        {
            Vector3 start = player.transform.position + Vector3.up;
            Vector3 delta = MountPoint(player.transform.position) + Vector3.up - start;
            if (delta.sqrMagnitude < 0.0001f) return false;
            // Inspect every hit: ignoring a rider must not hide a wall behind them.
            foreach (RaycastHit hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                Transform obstacle = hit.transform;
                if (obstacle == null || obstacle.IsChildOf(cart.transform) || obstacle.IsChildOf(player.transform)) continue;
                bool seatedRider = false;
                for (int i = 0; i < seats.Capacity; i++)
                {
                    if (seats[i] == HorseSeats.Empty) continue;
                    PlayerNet occupant = Player(seats[i]);
                    if (occupant != null && obstacle.IsChildOf(occupant.transform)) { seatedRider = true; break; }
                }
                if (seatedRider) continue;
                log.LogInfo("Horse mount blocked: horse=" + Id + "; client=" + player.OwnerClientId + "; collider=" + obstacle.name);
                return true;
            }
            return false;
        }
        private void Note(ulong target, string text) { if (target == network.LocalClientId) Tell(text); else Send(target, new Wire { op = 6, message = text }); }
        private void Broadcast()
        {
            Wire state = new Wire { op = 4, revision = ++serial, exists = cart != null, occupants = seats.Encode(), position = vehiclePosition, yaw = vehicleRotation.eulerAngles.y };
            foreach (ulong id in network.ConnectedClientsIds) if (id != network.LocalClientId && peers.ContainsKey(id)) Send(id, state);
        }
        private void FollowDriver()
        {
            PlayerNet driver = Player(seats[0]);
            if (cart == null || !Alive(driver)) return;
            if (network.IsServer && Time.unscaledTime < driverGraceUntil) return;
            if (!network.IsServer && localSeat != 0) return;
            Quaternion rotation = Quaternion.Euler(0, driver.transform.eulerAngles.y, 0);
            Vector3 position = driver.transform.position - rotation * offsets[0];
            if (Vector3.Distance(position, vehiclePosition) > 15) { if (network.IsServer) seats.Clear(); return; }
            if (Extension > 0 && localSeat == 0 && !ExtendedPathClear(position, rotation))
            {
                bool wasEnabled = controller != null && controller.enabled;
                if (controller != null) controller.enabled = false;
                driver.transform.SetPositionAndRotation(vehiclePosition + vehicleRotation * offsets[0], vehicleRotation);
                if (controller != null) controller.enabled = wasEnabled;
                if (movement != null) movement.characterVelocity = Vector3.zero;
                return;
            }
            vehiclePosition = position; vehicleRotation = rotation;
        }
        private bool NearCart()
        {
            if (cart == null || PlayerNet.Instance == null) return false;
            return MountDistance(PlayerNet.Instance.transform.position) < MountRange;
        }
        private void ApplySeat()
        {
            int wanted = seats.Find(network.LocalClientId);
            if (!Alive(PlayerNet.Instance) || cart == null || SceneName != SceneManager.GetActiveScene().name) wanted = -1;
            if (wanted == localSeat && (wanted < 0 || rider == PlayerNet.Instance)) return;
            RestoreRider();
            if (wanted < 0) return;
            movement = PlayerMovement.Instance; rider = PlayerNet.Instance;
            controller = rider.GetComponent<CharacterController>();
            if (movement == null || controller == null) { rider = null; movement = null; return; }
            parkedCollider.enabled = false;
            localSeat = wanted; originalViewOffset = movement.fpViewHeightOffset; movement.fpViewHeightOffset += 1.12f; originalRadius = controller.radius; controllerEnabled = controller.enabled;
            UpdateRiderView(movement);
            controller.enabled = false;
            rider.transform.position = vehiclePosition + vehicleRotation * offsets[wanted];
            rider.transform.rotation = vehicleRotation;
            if (wanted == 0) { controller.radius = Mathf.Min(CartRadius, controller.height / 2 - 0.02f); controller.enabled = controllerEnabled; }
            movement.characterVelocity = Vector3.zero; movement.isAutoRunning = false;
            passengerPitch = passengerYaw = 0;
            lastPosition = rider.transform.position;
            Tell(wanted == 0 ? "Driver: WASD | Shift boost | Jump key | E exit" : "Passenger | E exit | Ctrl+F1: driver | Ctrl+F2-F"+seats.Capacity+": seat");
        }
        private void RestoreRider()
        {
            if (controller != null) { controller.radius = originalRadius; controller.enabled = controllerEnabled && Alive(rider); }
            if (movement != null)
            {
                movement.fpViewHeightOffset = originalViewOffset;
                UpdateRiderView(movement);
                movement.characterVelocity = Vector3.zero; movement.isAutoRunning = false;
            }
            rider = null; movement = null; controller = null; localSeat = -1;
        }
        private bool FindExit(PlayerNet player, out Vector3 exit)
        {
            exit = Vector3.zero;
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null) return false;
            int seat = seats.Find(player.OwnerClientId);
            Vector3 origin = Extension > 0 && seat >= 0 ? vehiclePosition + vehicleRotation * offsets[seat] : vehiclePosition;
            if (Extension > 0)
            {
                foreach (float side in new[] { 1f, -1f })
                {
                    Vector3 near = origin + vehicleRotation * Vector3.right * (side * 1.4f);
                    if (GroundSpot(near, .5f, cc.height, false, out exit) && ExitPathClear(origin, exit)) return true;
                }
            }
            for (int i = 0; i < 12; i++)
            {
                Vector3 near = origin + Quaternion.Euler(0, i * 30, 0) * Vector3.forward * 3.5f;
                if (!GroundSpot(near, 0.5f, cc.height, false, out exit)) continue;
                if (Extension > 0 ? ExitPathClear(origin, exit) : !Physics.Linecast(origin + Vector3.up * 1.4f, exit + Vector3.up * 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
        }
        private bool ExitPathClear(Vector3 origin, Vector3 exit)
        {
            Vector3 local = Quaternion.Inverse(vehicleRotation) * (exit - vehiclePosition);
            if (Mathf.Abs(local.x) < 1f && local.z > -1.35f-Extension-.5f && local.z < 1.85f) return false;
            Vector3 delta = exit - origin;
            foreach (RaycastHit hit in Physics.RaycastAll(origin+Vector3.up*1.4f, delta.normalized, delta.magnitude,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (HorseObstacle(hit.transform)) return false;
            return true;
        }
        private void ExitAt(Vector3 position)
        {
            seats.Remove(network.LocalClientId);
            PlayerNet player = PlayerNet.Instance;
            RestoreRider();
            if (player == null) return;
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null) return;
            bool wasEnabled = cc.enabled; cc.enabled = false;
            player.transform.position = position - Vector3.up * (cc.center.y - cc.height / 2);
            cc.enabled = wasEnabled;
        }
        // Preserve the installed camera-height fix when rebuilding from source.
        private static void UpdateRiderView(PlayerMovement movement)
        {
            if (movement == null || movement.fpView == null) return;
            movement.fpView.localPosition = new Vector3(0,
                movement.capsuleHeightStanding + movement.fpViewHeightOffset, 0);
        }
        private void LateUpdate()
        {
            UpdateRiderView(movement);
            if (cart == null || network == null) return;
            // Driver already uses the game's owner-authoritative NetworkTransform.
            FollowDriver();
            if (network.IsServer) SettleWithoutDriver(Time.deltaTime);
            Vector3 delta = vehiclePosition - cart.transform.position;
            if (!network.IsServer && localSeat != 0)
                cart.transform.SetPositionAndRotation(Vector3.Lerp(cart.transform.position, vehiclePosition, Mathf.Clamp01(Time.unscaledDeltaTime * 15)), Quaternion.Slerp(cart.transform.rotation, vehicleRotation, Mathf.Clamp01(Time.unscaledDeltaTime * 15)));
            else cart.transform.SetPositionAndRotation(vehiclePosition, vehicleRotation);
            parkedCollider.enabled = seats.Count == 0;
            cart.SetActive(SceneName == SceneManager.GetActiveScene().name);
            if (model != null) model.Animate(Time.deltaTime, IsAirborne());
            if (localSeat > 0 && rider != null)
            {
                rider.transform.position = cart.transform.position + cart.transform.rotation * offsets[localSeat];
                rider.transform.rotation = cart.transform.rotation * Quaternion.Euler(0, passengerYaw, 0);
                if (CanInput())
                {
                    Vector2 look = PlayerInput.Instance.GetLookInput();
                    passengerYaw = Mathf.Clamp(passengerYaw + look.x * movement.rotationSpeed * Time.deltaTime, -100, 100);
                    passengerPitch = Mathf.Clamp(passengerPitch - look.y * movement.rotationSpeed * Time.deltaTime, -65, 65);
                }
                if (movement.fpView != null) movement.fpView.localRotation = Quaternion.Euler(passengerPitch, 0, 0);
                PlayerManager.LocalPlayerPosition = rider.transform.position;
            }
        }
        private sealed class SpeedState { public float ground, air; }
        private bool GroundBelow(float distance, out RaycastHit ground)
        {
            ground = new RaycastHit();
            float closest = Single.PositiveInfinity;
            foreach (RaycastHit hit in Physics.RaycastAll(vehiclePosition + Vector3.up * .2f, Vector3.down,
                distance + .2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform == null || (cart != null && hit.transform.IsChildOf(cart.transform)) ||
                    hit.transform.GetComponentInParent<PlayerNet>() != null || hit.normal.y < .3f) continue;
                if (hit.distance < closest) { closest = hit.distance; ground = hit; }
            }
            return closest < Single.PositiveInfinity;
        }
        private bool IsAirborne()
        {
            if (localSeat == 0 && movement != null) return !movement.isGrounded;
            RaycastHit ground;
            return !GroundBelow(.25f, out ground);
        }
        private void SettleWithoutDriver(float dt)
        {
            // A driver dying/disconnecting in midair must not leave a floating horse.
            if (SceneName != SceneManager.GetActiveScene().name) return;
            if (Alive(Player(seats[0]))) { fallSpeed = 0; return; }
            dt = Mathf.Clamp(dt, 0, .1f);
            fallSpeed = Mathf.Min(fallSpeed + 20f * dt, 20f);
            float drop = fallSpeed * dt;
            RaycastHit ground;
            if (GroundBelow(drop + .1f, out ground))
            {
                vehiclePosition.y = Mathf.Min(vehiclePosition.y, ground.point.y + .06f);
                fallSpeed = 0;
            }
            else vehiclePosition.y -= drop;
        }
        private static bool BeforeMove(PlayerMovement __instance, out SpeedState __state)
        {
            __state = null; TavernHorse self = Active;
            if (self == null || self.localSeat < 0 || self.movement != __instance) return true;
            if (self.localSeat > 0) return false;
            __state = new SpeedState { ground=__instance.maxSpeedOnGround, air=__instance.maxSpeedInAir };
            float multiplier = CanInput() && (Input.GetKey(KeyCode.LeftShift)||Input.GetKey(KeyCode.RightShift)) ? Mathf.Max(self.cruise.Value,self.boost.Value) : self.cruise.Value;
            __instance.maxSpeedOnGround *= multiplier; __instance.maxSpeedInAir *= multiplier;
            return true;
        }
        private static Exception AfterMove(PlayerMovement __instance, SpeedState __state, Exception __exception)
        {
            if (__state != null) { __instance.maxSpeedOnGround=__state.ground; __instance.maxSpeedInAir=__state.air; }
            return __exception;
        }
        private static bool FilterAction(MethodBase __originalMethod, ref bool __result)
        {
            TavernHorse self=Active ?? Nearest;
            if (self == null || !self.enabledSetting.Value) return true;
            if (__originalMethod.Name == "GetJumpInputDown" || __originalMethod.Name == "GetJumpInputHeld")
            {
                // Let native movement handle grounding, impulse and gravity; passengers never drive it.
                if (self.localSeat < 0 || (self.localSeat == 0 && CanInput())) return true;
                __result = false; return false;
            }
            if (self.localSeat >= 0 || (__originalMethod.Name.StartsWith("GetUse",StringComparison.Ordinal) &&
                (self.consumeFrame==Time.frameCount || (CanInput()&&Input.GetKey(self.mountKey.Value)&&self.NearCart())))) { __result=false; return false; }
            return true;
        }
        private void Tell(string text) { notice=text; noticeUntil=Time.unscaledTime+5; }
        private void OnGUI()
        {
            if (network==null || !enabledSetting.Value || (Active != this && Nearest != this)) return;
            string text=Time.unscaledTime<noticeUntil ? notice : localSeat==0 ? "Driver | WASD | Shift | Jump key | E exit | Ctrl+F1-F"+seats.Capacity+": seat" : localSeat>0 ? "Passenger | E exit | Ctrl+F1-F"+seats.Capacity+": seat" : CanInput()&&NearCart() ? "E: Mount horse ("+seats.Count+"/"+seats.Capacity+")" : "";
            if (!String.IsNullOrEmpty(text)) GUI.Box(new Rect(Screen.width/2-270,Screen.height-175,540,35),text);
        }
        private void OnDestroy() { Unbind(); all.Remove(this); }
        // Ground clearance and procedural cart geometry.

        private bool GroundSpot(Vector3 near, float radius, float height, bool outdoors, out Vector3 ground)
        {
            ground = Vector3.zero;
            RaycastHit hit;
            if (!Physics.Raycast(near + Vector3.up * 2.5f, Vector3.down, out hit, 6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            if (Vector3.Angle(hit.normal, Vector3.up) > 30f || (cart != null && hit.transform.IsChildOf(cart.transform))) return false;
            ground = hit.point + Vector3.up * 0.06f;
            if (outdoors && Physics.Raycast(ground + Vector3.up * 0.2f, Vector3.up, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            float top = Mathf.Max(height - radius, radius);
            Collider[] overlaps = Physics.OverlapCapsule(ground + Vector3.up * (radius + 0.03f), ground + Vector3.up * top,
                radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (Collider other in overlaps)
            {
                if (PlayerNet.Instance != null && other.transform.IsChildOf(PlayerNet.Instance.transform)) continue;
                return false;
            }
            // Reject narrow ledges: the footprint needs support on all four sides.
            foreach (Vector3 offset in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
            {
                RaycastHit support;
                if (!Physics.Raycast(ground + offset * radius + Vector3.up, Vector3.down, out support, 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    Mathf.Abs(support.point.y - hit.point.y) > 0.35f) return false;
            }
            return true;
        }

        private void BuildCart()
        {
            cart = new GameObject(Variant == HorseVariant.Extended ? "Tony Five Seat Horse" : "Tony Two Seat Horse"); cart.transform.SetParent(transform, false);
            model = HorseModel.Create(cart.transform, Variant);
            parkedCollider = cart.AddComponent<BoxCollider>();
            parkedCollider.center = new Vector3(0,1.2f,-Extension*.5f); parkedCollider.size = new Vector3(.9f,2.4f,2.7f+Extension);
        }
        private void RemoveCart()
        {
            if (cart != null) Destroy(cart); cart = null; parkedCollider = null; model = null;
        }
    }
}
