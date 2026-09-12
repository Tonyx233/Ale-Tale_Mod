using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TonyMods
{
    // Single-player arcade cart: one movement/collision authority (the original player controller).
    public sealed class TavernCart : MonoBehaviour
    {
        private static TavernCart instance;
        private Harmony patches;
        private ManualLogSource log;
        private ConfigEntry<bool> enabledSetting;
        private ConfigEntry<KeyCode> summonKey, mountKey;
        private ConfigEntry<float> cruise, boost;
        private GameObject cart;
        private BoxCollider parkedCollider;
        private readonly List<Transform> wheels = new List<Transform>();
        private readonly List<Material> materials = new List<Material>();
        private PlayerNet sessionPlayer, rider;
        private PlayerMovement movement;
        private CharacterController controller;
        private float originalRadius;
        private bool handsVisible;
        private Vector3 lastPosition;
        private bool attemptedSpawn;
        private float spawnAt, noticeUntil;
        private string notice = "";
        private int consumeFrame = -1;
        private int cartScene = -1;
        private const float MountRange = 2.8f;
        private const float CartRadius = 0.85f;

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            instance = this;
            log = logger;
            enabledSetting = config.Bind("Cart", "Enabled", true, "Enable the single-player arcade cart.");
            summonKey = config.Bind("Cart", "SummonKey", KeyCode.F6, "Place or recall the parked cart near you, outside only.");
            mountKey = config.Bind("Cart", "MountKey", KeyCode.E, "Mount or dismount the cart.");
            cruise = config.Bind("Cart", "SpeedMultiplier", 1.8f, new ConfigDescription("Mounted ground speed relative to walking.", new AcceptableValueRange<float>(1f, 3f)));
            boost = config.Bind("Cart", "SprintMultiplier", 2.4f, new ConfigDescription("Mounted sprint speed relative to walking.", new AcceptableValueRange<float>(1f, 4f)));
            patches = new Harmony("Tony.AleTaleMods.Cart");
            patches.Patch(AccessTools.Method(typeof(PlayerMovement), "HandleCharacterMovement"),
                prefix: new HarmonyMethod(typeof(TavernCart), "BeforeMove"),
                finalizer: new HarmonyMethod(typeof(TavernCart), "AfterMove"));
            foreach (string name in new[] { "GetJumpInputDown", "GetJumpInputHeld", "GetDashInputDown", "GetCrouchInputDown", "GetCrouchInputHeld", "GetFireInputDown", "GetFireInputHeld", "GetFireInputReleased", "GetAimInputDown", "GetAimInputHeld", "GetAimInputReleased", "GetDropInputDown", "GetAutoRunInputDown", "GetUseInputDown", "GetUseInput", "GetUseInputUp" })
            {
                MethodInfo method = AccessTools.Method(typeof(PlayerInput), name);
                if (method == null || method.ReturnType != typeof(bool)) throw new MissingMethodException("PlayerInput." + name);
                patches.Patch(method, prefix: new HarmonyMethod(typeof(TavernCart), "FilterAction"));
            }
            SceneManager.sceneUnloaded += SceneUnloaded;
            log.LogInfo("Single-player cart ready: F6 summon, E mount/dismount.");
        }

        private static bool Solo()
        {
            NetworkManager net = NetworkManager.Singleton;
            return net != null && net.IsListening && net.IsServer && net.ConnectedClientsIds.Count == 1;
        }

        private static bool CanInput()
        {
            return Time.timeScale > 0 && PlayerInput.Instance != null && PlayerInput.Instance.CanProcessInput() &&
                PlayerMovement.Instance != null && PlayerMovement.Instance.canMove && !PlayerMovement.Instance.isDead;
        }

        private void Update()
        {
            PlayerNet player = PlayerNet.Instance;
            if (!enabledSetting.Value || player == null || !player.IsSpawned || !Solo())
            {
                if (rider != null) RestoreRider();
                RemoveCart();
                sessionPlayer = null;
                return;
            }
            if (sessionPlayer != player)
            {
                RestoreRider(); RemoveCart();
                sessionPlayer = player;
                attemptedSpawn = false;
                spawnAt = Time.unscaledTime + 3f;
            }
            if (rider != null && (rider.hp == null || rider.hp.Value <= 0 || movement == null || controller == null ||
                movement.isDead || Vector3.Distance(rider.transform.position, lastPosition) > 15f))
            {
                RestoreRider(); RemoveCart();
                Tell("Cart ride ended. Press F6 outside to place it again.");
                return;
            }
            if (!CanInput()) return;
            if (!attemptedSpawn && Time.unscaledTime >= spawnAt)
            {
                attemptedSpawn = true;
                PlaceCart();
            }
            if (Input.GetKeyDown(summonKey.Value) && rider == null) PlaceCart();
            if (Input.GetKeyDown(mountKey.Value))
            {
                if (rider != null) { consumeFrame = Time.frameCount; Dismount(); }
                else if (NearCart()) { consumeFrame = Time.frameCount; Mount(); }
            }
            if (rider != null && (movement.isInWater || movement.isOverWater))
            {
                RestoreRider(); RemoveCart();
                Tell("Cart cannot travel through water. Press F6 on dry land.");
            }
        }

        private bool NearCart()
        {
            if (cart == null || PlayerNet.Instance == null || !Solo()) return false;
            if (Vector3.Distance(PlayerNet.Instance.transform.position, cart.transform.position) > MountRange) return false;
            Camera camera = PlayerMovement.Instance != null ? PlayerMovement.Instance.mainCamera : null;
            if (camera == null) return false;
            Vector3 target = cart.transform.position + Vector3.up * 0.7f;
            if (Vector3.Dot(camera.transform.forward, (target - camera.transform.position).normalized) < 0.4f) return false;
            RaycastHit hit;
            return !Physics.Linecast(camera.transform.position, target, out hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.transform.IsChildOf(cart.transform);
        }

        private void PlaceCart()
        {
            if (PlayerMovement.Instance == null || !PlayerMovement.Instance.isGrounded || PlayerMovement.Instance.isInWater) return;
            CharacterController cc = PlayerNet.Instance.GetComponent<CharacterController>();
            if (cc == null) return;
            Vector3 heading = Vector3.ProjectOnPlane(PlayerNet.Instance.transform.forward, Vector3.up).normalized;
            Vector3 spot;
            bool found = false;
            spot = Vector3.zero;
            for (int ring = 0; ring < 3 && !found; ring++)
                for (int i = 0; i < 8 && !found; i++)
                {
                    Vector3 direction = Quaternion.Euler(0, i * 45, 0) * heading;
                    found = GroundSpot(PlayerNet.Instance.transform.position + direction * (4 + ring * 2), CartRadius, cc.height, true, out spot);
                }
            if (!found) { Tell("No clear outdoor space nearby. Move outside and press F6."); return; }
            RemoveCart();
            BuildCart();
            cart.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(heading));
            cartScene = PlayerNet.Instance.gameObject.scene.handle;
            Tell("Cart placed nearby. Look at it and press E to ride.");
        }

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

        private void Mount()
        {
            PlayerMovement pm = PlayerMovement.Instance;
            CharacterController cc = PlayerNet.Instance.GetComponent<CharacterController>();
            if (pm == null || cc == null || !cc.enabled || !pm.isGrounded || pm.isCrouching || pm.isInWater) return;
            parkedCollider.enabled = false;
            Vector3 ground;
            if (!GroundSpot(cart.transform.position, CartRadius, cc.height, true, out ground))
            {
                parkedCollider.enabled = true;
                Tell("Not enough clearance to ride here. Recall the cart with F6.");
                return;
            }
            rider = PlayerNet.Instance; movement = pm; controller = cc;
            originalRadius = cc.radius;
            handsVisible = pm.fpHands != null && pm.fpHands.gameObject.activeSelf;
            if (pm.fpHands != null) pm.fpHands.gameObject.SetActive(false);
            cc.enabled = false;
            // Keep the original controller's feet on the ground, independent of prefab pivot.
            rider.transform.position = ground - Vector3.up * (cc.center.y - cc.height / 2);
            cc.radius = Mathf.Min(CartRadius, cc.height / 2 - 0.02f);
            cc.enabled = true;
            pm.characterVelocity = Vector3.zero;
            pm.isAutoRunning = false;
            lastPosition = rider.transform.position;
            Tell("Riding: WASD move | Shift faster | E dismount");
        }

        private void Dismount()
        {
            if (controller == null || cart == null) { RestoreRider(); return; }
            Vector3 ground = Vector3.zero;
            bool found = false;
            for (int i = 0; i < 8 && !found; i++)
            {
                Vector3 offset = Quaternion.Euler(0, i * 45 + 90, 0) * cart.transform.forward * 2f;
                found = GroundSpot(cart.transform.position + offset, originalRadius, controller.height, false, out ground);
                if (found && Physics.Linecast(cart.transform.position + Vector3.up, ground + Vector3.up, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) found = false;
            }
            if (!found) { Tell("No safe place to dismount. Move to open ground first."); return; }
            PlayerNet player = rider;
            CharacterController cc = controller;
            float feetOffset = cc.center.y - cc.height / 2;
            RestoreRider();
            cc.enabled = false;
            player.transform.position = ground - Vector3.up * feetOffset;
            cc.enabled = true;
            if (parkedCollider != null) parkedCollider.enabled = true;
        }

        private void RestoreRider()
        {
            if (rider == null && movement == null) return;
            if (controller != null) controller.radius = originalRadius;
            if (movement != null)
            {
                movement.characterVelocity = Vector3.zero;
                movement.isAutoRunning = false;
                if (movement.fpHands != null) movement.fpHands.gameObject.SetActive(handsVisible);
            }
            rider = null; movement = null; controller = null;
        }

        private void LateUpdate()
        {
            if (rider == null || cart == null || controller == null) return;
            Vector3 delta = rider.transform.position - lastPosition;
            if (delta.sqrMagnitude > 225f)
            {
                RestoreRider(); RemoveCart();
                Tell("Cart ride ended after teleport. Press F6 outside.");
                return;
            }
            float turn = Mathf.Clamp01(Time.deltaTime * 10);
            Vector3 heading = Vector3.ProjectOnPlane(rider.transform.forward, Vector3.up);
            if (heading.sqrMagnitude > 0.01f) cart.transform.rotation = Quaternion.Slerp(cart.transform.rotation, Quaternion.LookRotation(heading), turn);
            cart.transform.position = rider.transform.position + Vector3.up * (controller.center.y - controller.height / 2);
            float spin = Vector3.Dot(delta, cart.transform.forward) / 0.34f * Mathf.Rad2Deg;
            foreach (Transform wheel in wheels) wheel.Rotate(Vector3.up, spin, Space.Self);
            lastPosition = rider.transform.position;
        }

        private sealed class SpeedState
        {
            public float ground, air;
        }
        private static void BeforeMove(PlayerMovement __instance, out SpeedState __state)
        {
            __state = null;
            TavernCart self = instance;
            if (self == null || self.rider == null || self.movement != __instance) return;
            __state = new SpeedState { ground = __instance.maxSpeedOnGround, air = __instance.maxSpeedInAir };
            // The current game does not read sprintSpeedModifier in movement; apply Shift explicitly.
            bool sprinting = CanInput() && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            float multiplier = sprinting ? Mathf.Max(self.cruise.Value, self.boost.Value) : self.cruise.Value;
            __instance.maxSpeedOnGround *= multiplier;
            __instance.maxSpeedInAir *= multiplier;
        }
        private static Exception AfterMove(PlayerMovement __instance, SpeedState __state, Exception __exception)
        {
            if (__state != null)
            {
                __instance.maxSpeedOnGround = __state.ground;
                __instance.maxSpeedInAir = __state.air;
            }
            return __exception;
        }
        private static bool FilterAction(MethodBase __originalMethod, ref bool __result)
        {
            TavernCart self = instance;
            if (self == null || !self.enabledSetting.Value) return true;
            bool riding = self.rider != null;
            bool use = __originalMethod.Name.StartsWith("GetUse", StringComparison.Ordinal);
            bool consumed = self.consumeFrame == Time.frameCount;
            if (riding || (use && (consumed || (CanInput() && Input.GetKey(self.mountKey.Value) && self.NearCart()))))
            {
                __result = false;
                return false;
            }
            return true;
        }

        private void BuildCart()
        {
            cart = new GameObject("Tony Single Player Cart");
            Material wood = MakeMaterial(new Color(0.38f, 0.18f, 0.07f));
            Material rim = MakeMaterial(new Color(0.12f, 0.12f, 0.13f));
            Material seat = MakeMaterial(new Color(0.5f, 0.08f, 0.06f));
            Part("Floor", PrimitiveType.Cube, new Vector3(0, 0.43f, 0), new Vector3(1.35f, 0.16f, 1.7f), wood);
            Part("Left rail", PrimitiveType.Cube, new Vector3(-0.65f, 0.8f, 0), new Vector3(0.1f, 0.5f, 1.7f), wood);
            Part("Right rail", PrimitiveType.Cube, new Vector3(0.65f, 0.8f, 0), new Vector3(0.1f, 0.5f, 1.7f), wood);
            Part("Front rail", PrimitiveType.Cube, new Vector3(0, 0.8f, 0.8f), new Vector3(1.3f, 0.5f, 0.1f), wood);
            Part("Seat", PrimitiveType.Cube, new Vector3(0, 0.65f, -0.4f), new Vector3(1f, 0.15f, 0.4f), seat);
            Part("Seat back", PrimitiveType.Cube, new Vector3(0, 1f, -0.65f), new Vector3(1f, 0.65f, 0.1f), seat);
            Part("Handle", PrimitiveType.Cube, new Vector3(0, 1.05f, 0.5f), new Vector3(0.75f, 0.07f, 0.08f), rim);
            for (int side = -1; side <= 1; side += 2)
                for (int end = -1; end <= 1; end += 2)
                {
                    Transform wheel = Part("Wheel", PrimitiveType.Cylinder, new Vector3(side * 0.77f, 0.34f, end * 0.58f), new Vector3(0.68f, 0.08f, 0.68f), rim);
                    wheel.localRotation = Quaternion.Euler(0, 0, 90);
                    Transform spoke = Part("Spoke", PrimitiveType.Cube, Vector3.zero, Vector3.one, wood);
                    spoke.SetParent(wheel, false);
                    spoke.localPosition = Vector3.zero;
                    spoke.localScale = new Vector3(0.75f, 2.1f, 0.13f);
                    wheels.Add(wheel);
                }
            parkedCollider = cart.AddComponent<BoxCollider>();
            parkedCollider.center = new Vector3(0, 0.6f, 0);
            parkedCollider.size = new Vector3(1.65f, 1.2f, 1.8f);
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
        private void Tell(string text) { notice = text; noticeUntil = Time.unscaledTime + 5; }
        private void OnGUI()
        {
            if (!enabledSetting.Value || sessionPlayer == null) return;
            string text = Time.unscaledTime < noticeUntil ? notice :
                rider != null ? "Cart: WASD move | Shift faster | E dismount" :
                CanInput() && NearCart() ? "E: Ride cart | F6: Recall cart" : "";
            if (!String.IsNullOrEmpty(text)) GUI.Box(new Rect(Screen.width / 2 - 250, Screen.height - 175, 500, 35), text);
        }
        private void SceneUnloaded(Scene scene)
        {
            if (scene.handle != cartScene) return;
            RestoreRider(); RemoveCart(); sessionPlayer = null;
        }
        private void OnDestroy()
        {
            RestoreRider(); RemoveCart();
            SceneManager.sceneUnloaded -= SceneUnloaded;
            if (patches != null) patches.UnpatchSelf();
            if (instance == this) instance = null;
        }
    }
}
