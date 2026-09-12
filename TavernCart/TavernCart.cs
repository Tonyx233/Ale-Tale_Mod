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
    public sealed class TavernCart : MonoBehaviour
    {
        private const string Channel = "Tony.Cart.v040";
        private const float MountRange = 4f, CartRadius = 0.85f;
        private static TavernCart instance;
        private readonly CartSeats seats = new CartSeats();
        private readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastRequest = new Dictionary<ulong, float>();
        private readonly List<Transform> wheels = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private readonly Vector3[] offsets = { new Vector3(0,0,1.05f), new Vector3(-0.53f,0,0.1f), new Vector3(0.53f,0,0.1f), new Vector3(-0.53f,0,-0.85f), new Vector3(0.53f,0,-0.85f), new Vector3(0,0,-1.65f) };
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<KeyCode> summonKey, mountKey;
        private ConfigEntry<float> cruise, boost;
        private NetworkManager network;
        private Harmony patches;
        private ManualLogSource log;
        private GameObject cart;
        private BoxCollider parkedCollider;
        private PlayerNet rider;
        private PlayerMovement movement;
        private CharacterController controller;
        private float originalRadius, passengerPitch, passengerYaw;
        private bool handsVisible, controllerEnabled;
        private Vector3 lastPosition, vehiclePosition;
        private Quaternion vehicleRotation = Quaternion.identity;
        private int localSeat = -1, consumeFrame = -1, serial, receivedSerial = -1, cartScene = -1;
        private float nextHello, nextState, noticeUntil, lastStateAt, driverGraceUntil;
        private bool attemptedSpawn;
        private string notice = "";
        [Serializable] private sealed class Wire
        {
            public int op, revision;
            public string occupants, message;
            public bool exists;
            public Vector3 position;
            public float yaw;
        }

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            instance = this; log = logger;
            enabledSetting = config.Bind("Cart", "Enabled", true, "Shared six-seat cart. All riders and the host need this mod version.");
            summonKey = config.Bind("Cart", "SummonKey", KeyCode.F6, "Host places or recalls an empty cart outside.");
            mountKey = config.Bind("Cart", "MountKey", KeyCode.E, "Request a seat or safely leave the cart.");
            cruise = config.Bind("Cart", "SpeedMultiplier", 1.8f, new ConfigDescription("Driver walking speed multiplier.", new AcceptableValueRange<float>(1f, 3f)));
            boost = config.Bind("Cart", "SprintMultiplier", 2.4f, new ConfigDescription("Driver Shift speed multiplier.", new AcceptableValueRange<float>(1f, 4f)));
            patches = new Harmony("Tony.AleTaleMods.Cart");
            patches.Patch(AccessTools.Method(typeof(PlayerMovement), "HandleCharacterMovement"), prefix: new HarmonyMethod(typeof(TavernCart), "BeforeMove"), finalizer: new HarmonyMethod(typeof(TavernCart), "AfterMove"));
            foreach (string name in new[] { "GetJumpInputDown", "GetJumpInputHeld", "GetDashInputDown", "GetCrouchInputDown", "GetCrouchInputHeld", "GetFireInputDown", "GetFireInputHeld", "GetFireInputReleased", "GetAimInputDown", "GetAimInputHeld", "GetAimInputReleased", "GetDropInputDown", "GetAutoRunInputDown", "GetUseInputDown", "GetUseInput", "GetUseInputUp" })
                patches.Patch(AccessTools.Method(typeof(PlayerInput), name), prefix: new HarmonyMethod(typeof(TavernCart), "FilterAction"));
            SceneManager.sceneUnloaded += SceneUnloaded;
            log.LogInfo("Six-seat cart ready: host F6 places cart; E requests driver/passenger seat.");
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
            network = null; attemptedSpawn = false; receivedSerial = -1; serial = 0;
        }
        private void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (!enabledSetting.Value || net == null || !net.IsListening || PlayerNet.Instance == null || !PlayerNet.Instance.IsSpawned)
            { if (network != null) Unbind(); return; }
            if (network != net) Bind(net);
            if (Time.unscaledTime >= nextHello) { nextHello = Time.unscaledTime + 1; Request(0); }
            if (network.IsServer)
            {
                for (int i = 0; i < CartSeats.Capacity; i++)
                {
                    ulong id = seats[i]; float seen;
                    if (id != CartSeats.Empty && (!Alive(Player(id)) || (id != network.LocalClientId && (!peers.TryGetValue(id, out seen) || Time.unscaledTime - seen > 5)))) seats.Remove(id);
                }
                FollowDriver();
                if (!attemptedSpawn && CanInput()) { attemptedSpawn = true; PlaceCart(); }
                if (Time.unscaledTime >= nextState) { nextState = Time.unscaledTime + 0.1f; Broadcast(); }
            }
            else if (Time.unscaledTime - lastStateAt > 5)
            { RestoreRider(); RemoveCart(); seats.Clear(); receivedSerial = -1; Tell("Waiting for a compatible cart host..."); }
            ApplySeat();
            if (!CanInput()) return;
            if (Input.GetKeyDown(summonKey.Value)) Request(3);
            if (Input.GetKeyDown(mountKey.Value) && (localSeat >= 0 || NearCart()))
            { consumeFrame = Time.frameCount; Request(localSeat >= 0 ? 2 : 1); }
            if (localSeat == 0 && movement != null && (movement.isInWater || movement.isOverWater)) Request(2);
        }
        private void Request(int op)
        {
            if (network == null) return;
            if (network.IsServer) ServerRequest(network.LocalClientId, new Wire { op = op });
            else Send(NetworkManager.ServerClientId, new Wire { op = op });
        }
        private void Send(ulong target, Wire packet)
        {
            string json = JsonUtility.ToJson(packet);
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
                Wire packet = JsonUtility.FromJson<Wire>(json);
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
            catch (Exception ex) { log.LogWarning("Cart message rejected: " + ex.Message); }
        }
        private static bool Finite(float v) { return !Single.IsNaN(v) && !Single.IsInfinity(v); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private void ServerRequest(ulong sender, Wire packet)
        {
            bool connected = sender == network.LocalClientId;
            foreach (ulong id in network.ConnectedClientsIds) if (id == sender) connected = true;
            if (!connected || packet.op < 0 || packet.op > 3) return;
            if (packet.op == 0) { peers[sender] = Time.unscaledTime; return; }
            if (!peers.ContainsKey(sender)) return;
            float previous;
            if (lastRequest.TryGetValue(sender, out previous) && Time.unscaledTime - previous < 0.25f) return;
            lastRequest[sender] = Time.unscaledTime;
            PlayerNet player = Player(sender);
            if (!Alive(player)) return;
            if (packet.op == 3)
            {
                if (sender != network.LocalClientId) { Note(sender, "Only the host can recall an empty cart."); return; }
                if (seats.Count != 0) { Note(sender, "Everyone must leave before recalling the cart."); return; }
                PlaceCart();
            }
            else if (packet.op == 1)
            {
                if (cart == null || Vector3.Distance(player.transform.position, vehiclePosition) > MountRange) return;
                RaycastHit hit;
                if (Physics.Linecast(player.transform.position + Vector3.up, vehiclePosition + Vector3.up, out hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) &&
                    !hit.transform.IsChildOf(cart.transform) && !hit.transform.IsChildOf(player.transform)) return;
                bool newRider = seats.Find(sender) < 0;
                int assigned = seats.Board(sender);
                if (assigned < 0) Note(sender, "Cart is full (6/6).");
                else if (assigned == 0 && newRider) driverGraceUntil = Time.unscaledTime + 0.75f;
                if (parkedCollider != null) parkedCollider.enabled = seats.Count == 0;
                if (sender == network.LocalClientId) ApplySeat();
            }
            else if (packet.op == 2 && seats.Find(sender) >= 0)
            {
                Vector3 exit;
                if (!FindExit(player, out exit)) { Note(sender, "No safe exit. Move the cart to open ground."); return; }
                seats.Remove(sender);
                if (sender == network.LocalClientId) ExitAt(exit);
                else Send(sender, new Wire { op = 5, position = exit });
            }
            Broadcast();
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
            vehiclePosition = position; vehicleRotation = rotation;
        }
        private bool NearCart()
        {
            if (cart == null || PlayerNet.Instance == null) return false;
            return Vector3.Distance(PlayerNet.Instance.transform.position, cart.transform.position) < MountRange;
        }
        private void ApplySeat()
        {
            int wanted = seats.Find(network.LocalClientId);
            if (!Alive(PlayerNet.Instance) || cart == null) wanted = -1;
            if (wanted == localSeat && (wanted < 0 || rider == PlayerNet.Instance)) return;
            RestoreRider();
            if (wanted < 0) return;
            movement = PlayerMovement.Instance; rider = PlayerNet.Instance;
            controller = rider.GetComponent<CharacterController>();
            if (movement == null || controller == null) { rider = null; movement = null; return; }
            parkedCollider.enabled = false;
            localSeat = wanted; originalRadius = controller.radius; controllerEnabled = controller.enabled;
            handsVisible = movement.fpHands != null && movement.fpHands.gameObject.activeSelf;
            if (movement.fpHands != null) movement.fpHands.gameObject.SetActive(false);
            controller.enabled = false;
            rider.transform.position = vehiclePosition + vehicleRotation * offsets[wanted];
            rider.transform.rotation = vehicleRotation;
            if (wanted == 0) { controller.radius = Mathf.Min(CartRadius, controller.height / 2 - 0.02f); controller.enabled = controllerEnabled; }
            movement.characterVelocity = Vector3.zero; movement.isAutoRunning = false;
            passengerPitch = passengerYaw = 0;
            lastPosition = rider.transform.position;
            Tell(wanted == 0 ? "Driver: WASD | Shift boost | E exit" : "Passenger " + wanted + "/5 | E exit");
        }
        private void RestoreRider()
        {
            if (controller != null) { controller.radius = originalRadius; controller.enabled = controllerEnabled && Alive(rider); }
            if (movement != null)
            {
                movement.characterVelocity = Vector3.zero; movement.isAutoRunning = false;
                if (movement.fpHands != null) movement.fpHands.gameObject.SetActive(handsVisible);
            }
            rider = null; movement = null; controller = null; localSeat = -1;
        }
        private bool FindExit(PlayerNet player, out Vector3 exit)
        {
            exit = Vector3.zero;
            CharacterController cc = player.GetComponent<CharacterController>();
            if (cc == null) return false;
            for (int i = 0; i < 12; i++)
            {
                Vector3 near = vehiclePosition + Quaternion.Euler(0, i * 30, 0) * Vector3.forward * 3.5f;
                if (!GroundSpot(near, 0.5f, cc.height, false, out exit)) continue;
                if (!Physics.Linecast(vehiclePosition + Vector3.up * 1.4f, exit + Vector3.up * 1.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            }
            return false;
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
        private void LateUpdate()
        {
            if (cart == null || network == null) return;
            // Driver already uses the game's owner-authoritative NetworkTransform.
            FollowDriver();
            Vector3 delta = vehiclePosition - cart.transform.position;
            if (!network.IsServer && localSeat != 0)
                cart.transform.SetPositionAndRotation(Vector3.Lerp(cart.transform.position, vehiclePosition, Mathf.Clamp01(Time.unscaledDeltaTime * 15)), Quaternion.Slerp(cart.transform.rotation, vehicleRotation, Mathf.Clamp01(Time.unscaledDeltaTime * 15)));
            else cart.transform.SetPositionAndRotation(vehiclePosition, vehicleRotation);
            parkedCollider.enabled = seats.Count == 0;
            foreach (Transform wheel in wheels) wheel.Rotate(Vector3.up, Vector3.Dot(delta, cart.transform.forward) / 0.34f * Mathf.Rad2Deg, Space.Self);
            if (localSeat > 0 && rider != null)
            {
                rider.transform.position = cart.transform.position + cart.transform.rotation * offsets[localSeat] + Vector3.up * 0.35f;
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
        private void PlaceCart()
        {
            if (!network.IsServer || seats.Count > 0 || PlayerMovement.Instance == null || !PlayerMovement.Instance.isGrounded) return;
            CharacterController cc = PlayerNet.Instance.GetComponent<CharacterController>();
            if (cc == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(PlayerNet.Instance.transform.forward, Vector3.up).normalized;
            Vector3 spot = Vector3.zero; bool found = false;
            for (int ring = 0; ring < 3 && !found; ring++)
                for (int i = 0; i < 8 && !found; i++) found = GroundSpot(PlayerNet.Instance.transform.position + Quaternion.Euler(0,i*45,0)*forward*(5+ring*2), 2f, 4.2f, true, out spot);
            if (!found) { Tell("Find a large open outdoor area and press F6."); return; }
            RemoveCart(); BuildCart();
            vehiclePosition = spot; vehicleRotation = Quaternion.LookRotation(forward);
            cart.transform.SetPositionAndRotation(vehiclePosition, vehicleRotation);
            cartScene = PlayerNet.Instance.gameObject.scene.handle;
            Tell("Six-seat cart placed. E boards the first free seat (driver first).");
        }
        private sealed class SpeedState { public float ground, air; }
        private static bool BeforeMove(PlayerMovement __instance, out SpeedState __state)
        {
            __state = null; TavernCart self = instance;
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
            TavernCart self=instance;
            if (self == null || !self.enabledSetting.Value) return true;
            if (self.localSeat >= 0 || (__originalMethod.Name.StartsWith("GetUse",StringComparison.Ordinal) &&
                (self.consumeFrame==Time.frameCount || (CanInput()&&Input.GetKey(self.mountKey.Value)&&self.NearCart())))) { __result=false; return false; }
            return true;
        }
        private void Tell(string text) { notice=text; noticeUntil=Time.unscaledTime+5; }
        private void OnGUI()
        {
            if (network==null || !enabledSetting.Value) return;
            string text=Time.unscaledTime<noticeUntil ? notice : localSeat==0 ? "Driver | WASD | Shift boost | E exit" : localSeat>0 ? "Passenger "+localSeat+"/5 | E exit" : CanInput()&&NearCart() ? "E: Board ("+seats.Count+"/6) | Host F6: recall" : "";
            if (!String.IsNullOrEmpty(text)) GUI.Box(new Rect(Screen.width/2-270,Screen.height-175,540,35),text);
        }
        private void SceneUnloaded(Scene scene)
        {
            if (scene.handle!=cartScene) return;
            RestoreRider(); RemoveCart(); seats.Clear(); attemptedSpawn=false;
            if (network!=null && network.IsServer) Broadcast();
        }
        private void OnDestroy()
        {
            Unbind(); SceneManager.sceneUnloaded-=SceneUnloaded;
            if (patches!=null) patches.UnpatchSelf(); if(instance==this)instance=null;
        }
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
            cart = new GameObject("Tony Six Seat Cart");
            Material wood = MakeMaterial(new Color(0.38f, 0.18f, 0.07f));
            Material rim = MakeMaterial(new Color(0.12f, 0.12f, 0.13f));
            Material seat = MakeMaterial(new Color(0.5f, 0.08f, 0.06f));
            Part("Floor", PrimitiveType.Cube, new Vector3(0, 0.43f, 0), new Vector3(1.6f, 0.16f, 4f), wood);
            Part("Left rail", PrimitiveType.Cube, new Vector3(-0.82f, 0.8f, 0), new Vector3(0.1f, 0.5f, 4f), wood);
            Part("Right rail", PrimitiveType.Cube, new Vector3(0.82f, 0.8f, 0), new Vector3(0.1f, 0.5f, 4f), wood);
            Part("Front rail", PrimitiveType.Cube, new Vector3(0, 0.8f, 1.95f), new Vector3(1.6f, 0.5f, 0.1f), wood);
            Part("Handle", PrimitiveType.Cube, new Vector3(0, 1.05f, 1.65f), new Vector3(0.75f, 0.07f, 0.08f), rim);
            for (int seatIndex = 0; seatIndex < offsets.Length; seatIndex++)
            {
                Vector3 at = offsets[seatIndex];
                Part("Seat " + seatIndex, PrimitiveType.Cube, at + new Vector3(0,0.65f,0), new Vector3(0.65f,0.15f,0.55f), seat);
                Part("Back " + seatIndex, PrimitiveType.Cube, at + new Vector3(0,0.95f,-0.3f), new Vector3(0.65f,0.55f,0.08f), seat);
            }
            for (int side = -1; side <= 1; side += 2)
                for (int end = -1; end <= 1; end += 2)
                {
                    Transform wheel = Part("Wheel", PrimitiveType.Cylinder, new Vector3(side * 0.94f, 0.34f, end * 1.5f), new Vector3(0.68f, 0.08f, 0.68f), rim);
                    wheel.localRotation = Quaternion.Euler(0, 0, 90);
                    Transform spoke = Part("Spoke", PrimitiveType.Cube, Vector3.zero, Vector3.one, wood);
                    spoke.SetParent(wheel, false);
                    spoke.localPosition = Vector3.zero;
                    spoke.localScale = new Vector3(0.75f, 2.1f, 0.13f);
                    wheels.Add(wheel);
                }
            parkedCollider = cart.AddComponent<BoxCollider>();
            parkedCollider.center = new Vector3(0, 0.6f, 0);
            parkedCollider.size = new Vector3(2f, 1.2f, 4.1f);
        }
        private Material MakeMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Cart material shader unavailable.");
            Material material = new Material(shader);
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            materials.Add(material);
            return material;
        }
        private Transform Part(string name, PrimitiveType kind, Vector3 position, Vector3 size, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(kind);
            part.name = name;
            part.transform.SetParent(cart.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = size;
            Collider collider = part.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            part.GetComponent<Renderer>().sharedMaterial = material;
            return part.transform;
        }
        private void RemoveCart()
        {
            if (cart != null) Destroy(cart);
            cart = null; parkedCollider = null; cartScene = -1;
            wheels.Clear();
            foreach (Material material in materials) if (material != null) Destroy(material);
            materials.Clear();
        }

    }
}
