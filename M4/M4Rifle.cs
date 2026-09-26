using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TonyMods
{
    // Added to the local first-person GunTool spawned for an M4 item. Replaces the native musket
    // fire/reload flow, which is gated by a 0.5 s fire animation and reloads only one round.
    internal sealed class M4Rifle : MonoBehaviour
    {
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(GunTool), "_state");
        private static readonly FieldInfo ReloadingField = AccessTools.Field(typeof(GunTool), "_isReloading");
        private static readonly FieldInfo ShotTimerField = AccessTools.Field(typeof(GunTool), "_shotTimer");
        private static readonly FieldInfo ItemIdField = AccessTools.Field(typeof(GunTool), "_gunItemId");
        private static readonly FieldInfo AmmoField = AccessTools.Field(typeof(GunTool), "ammoData");
        private static readonly FieldInfo NoiseField = AccessTools.Field(typeof(GunTool), "_fireNoise");
        private static readonly FieldInfo DeviationField = AccessTools.Field(typeof(GunTool), "_damageDeviation");
        private static readonly FieldInfo BreakField = AccessTools.Field(typeof(GunTool), "_breakSoundEvent");
        private static readonly MethodInfo SetStateMethod = AccessTools.Method(typeof(GunTool), "SetState");
        private static readonly MethodInfo RaycastMethod = AccessTools.Method(typeof(GunTool), "RaycastShot");
        private static readonly FieldInfo PitchField = AccessTools.Field(typeof(PlayerMovement), "_cameraVerticalAngle");
        private static readonly FieldInfo BodyField = AccessTools.Field(typeof(PlayerMovement), "playerTransform");
        // Local magazine state survives weapon swaps until the batched inventory RPCs come back.
        private static readonly Dictionary<uint, KeyValuePair<int, float>> clipMemory = new Dictionary<uint, KeyValuePair<int, float>>();
        private static bool automatic = true;

        internal static M4Rifle Active { get; private set; }
        internal static bool ApiReady
        {
            get
            {
                return StateField != null && ReloadingField != null && ShotTimerField != null && ItemIdField != null && AmmoField != null &&
                    NoiseField != null && DeviationField != null && BreakField != null && SetStateMethod != null && RaycastMethod != null &&
                    PitchField != null && BodyField != null;
            }
        }

        private GunTool gun;
        private M4Model model;
        private readonly M4Batch batch = new M4Batch();
        private bool starting;
        private float heat, lastShot = -10, emptyAt, noiseAt, kick, pitchDebt, yawDebt, noticeUntil;
        private string notice;
        private Coroutine reload;
        private readonly List<Casing> casings = new List<Casing>();
        private GUIStyle hudStyle, noticeStyle;
        private sealed class Casing { public Transform Body; public Vector3 Velocity; public float Life; }

        public void Bind(GunTool native)
        {
            gun = native;
            if (model == null)
            {
                model = M4Model.Skin(gun.transform, true, M4Armory.ModelScale, M4Armory.FirstPersonOffset, M4Armory.Fde);
                if (model == null) throw new InvalidOperationException("Native musket mesh missing from fp prefab");
                gun.endPoint = model.Muzzle;
            }
            gun.fireRate = M4Rules.ShotInterval(M4Armory.Rpm);
            gun.damage = (ushort)M4Armory.Damage;
            DeviationField.SetValue(gun, (ushort)0);
            gun.projectilePerShot = 1;
            gun.screenShake = false;
            gun.disabledOnEmpty = false;
            gun.triggerType = automatic ? GunTool.TriggerType.Auto : GunTool.TriggerType.Manual;
            AmmoField.SetValue(gun, M4Armory.AmmoData);
            SetSelector();
        }

        private uint ItemId { get { return (uint)ItemIdField.GetValue(gun); } }
        private bool Reloading { get { return (bool)ReloadingField.GetValue(gun); } }
        private bool Idle { get { return (GunTool.State)StateField.GetValue(gun) == GunTool.State.Idle; } }
        // Reload in progress or being started: native CheckReload must not start another one.
        internal bool Busy { get { return starting || Reloading; } }

        public void AfterSelected()
        {
            KeyValuePair<int, float> memory;
            uint id = ItemId;
            if (id != 0 && clipMemory.TryGetValue(id, out memory) && Time.unscaledTime - memory.Value < 5)
                gun.clipContent = Mathf.Clamp(memory.Key, 0, gun.clipSize);
        }

        private void Remember(int clip)
        {
            uint id = ItemId;
            if (id != 0) clipMemory[id] = new KeyValuePair<int, float>(clip, Time.unscaledTime);
        }

        // Called from the GunTool.Fire prefix instead of the native animation-gated shot.
        public void Fire()
        {
            if (!Idle || (float)ShotTimerField.GetValue(gun) > 0) return;
            if (gun.clipContent <= 0) { Empty(); return; }
            gun.clipContent--;
            ShotTimerField.SetValue(gun, gun.fireRate);
            batch.Add(Time.time);
            bool scoped = MusketScope.IsScoped(gun);
            gun.spreadAngle = M4Rules.Spread(heat, scoped);
            heat += 1; lastShot = Time.time;
            RaycastMethod.Invoke(gun, null);
            Remember(gun.clipContent);
            // May re-enter StartReload on the host when the magazine hits zero (see Flush).
            if (M4Rules.ShouldFlush(batch.Charge, 0, gun.clipContent)) Flush();
            kick = 1;
            if (!scoped) { model.Flash(); Eject(); }
            float climb = M4Rules.Recoil(scoped, M4Armory.RecoilScale);
            pitchDebt += climb * UnityEngine.Random.Range(.85f, 1.15f);
            yawDebt += UnityEngine.Random.Range(-.13f, .13f) * M4Armory.RecoilScale;
            Vector3 muzzle = model.Muzzle.position;
            M4Audio.Play(M4Sound.Kind.Shot, muzzle, true);
            M4Armory.SendSound(M4Sound.Kind.Shot, muzzle);
            if (Time.time >= noiseAt && PlayerNoiseManager.Instance != null)
            {
                noiseAt = Time.time + .4f;
                PlayerNoiseManager.Instance.NoiseServerRpc(transform.position, (float)NoiseField.GetValue(gun), default(ServerRpcParams));
            }
        }

        private void Empty()
        {
            if (Time.time < emptyAt) return;
            emptyAt = Time.time + .3f;
            M4Audio.Play(M4Sound.Kind.Empty, transform.position, true);
            M4Armory.SendSound(M4Sound.Kind.Empty, transform.position);
        }

        // Charge and durability RPCs accept a count, so bursts are merged (see M4Rules.ShouldFlush).
        // Counts are taken before sending: host RPCs run synchronously and re-enter via
        // OnItemsChanged -> CheckReload -> Reload (0.14.0 recursed until the stack overflowed).
        private void Flush()
        {
            int charge, wear;
            if (!batch.Take(out charge, out wear)) return;
            uint id = ItemId;
            ContainerNet inventory = PlayerInventory.Instance != null ? PlayerInventory.Instance.inventory : null;
            if (id != 0 && inventory != null)
            {
                if (charge > 0) inventory.RemoveItemChargeServerRpc(id, (ushort)charge);
                ItemData data = M4Armory.RifleData;
                if (wear > 0 && data != null && data.durability > 0 && ItemManager.Instance != null)
                {
                    Item item;
                    if (inventory.GetItemById(id, out item, true) && item.durability <= data.durabilityPerHit * wear && SoundManager.Instance != null)
                    {
                        var broken = (SoundEvent)BreakField.GetValue(gun);
                        SoundManager.Instance.Play(broken, transform.position);
                        SoundManager.Instance.PlayServerRpc(broken, transform.position, true, default(ServerRpcParams));
                    }
                    ItemManager.Instance.DamageToolServerRpc(id, (ushort)wear, default(ServerRpcParams));
                }
            }
        }

        // Called from the GunTool.Reload prefix. Moves the real round count (native loads one).
        public void StartReload()
        {
            if (starting || Reloading || !Idle || gun.clipContent >= gun.clipSize || PlayerInventory.Instance == null) return;
            uint id = ItemId;
            if (id == 0) return;
            ContainerNet inventory = PlayerInventory.Instance.inventory;
            int need = M4Rules.ReloadCount(inventory.GetItemAmount(M4Armory.AmmoId), gun.clipContent, gun.clipSize);
            if (need <= 0) { if (gun.clipContent == 0) Empty(); return; }
            bool empty = gun.clipContent == 0;
            int target = gun.clipContent + need;
            starting = true;
            try
            {
                // Same order as native Reload: mark the reload before any RPC, because on the host each
                // RPC synchronously fires OnItemsChanged -> CheckReload -> Reload back into this method.
                ReloadingField.SetValue(gun, true);
                SetStateMethod.Invoke(gun, new object[] { GunTool.State.Reload });
                Remember(target);
                Flush();
                inventory.RemoveAmountServerRpc(M4Armory.AmmoId, (ushort)need);
                inventory.AddItemChargeServerRpc(id, (ushort)need);
            }
            finally { starting = false; }
            reload = StartCoroutine(ReloadRoutine(target, empty));
        }

        private IEnumerator ReloadRoutine(int target, bool empty)
        {
            float duration = M4Rules.ReloadSeconds(empty, M4Armory.ReloadSeconds), started = Time.time;
            bool magOut = false, magIn = false, charged = !empty;
            Transform mag = model.Joint("mag"), handle = model.Joint("charge");
            Vector3 magRest = model.Rest("mag"), handleRest = model.Rest("charge");
            while (true)
            {
                float t = (Time.time - started) / duration;
                if (t >= 1) break;
                float r = t * 2.2f; // animation authored on the 2.2 s tactical timeline
                model.Recoil.localRotation = Quaternion.Euler(0, 0, 22 * Smooth(r / .3f) * (1 - Smooth((r - 1.85f) / .3f)));
                float drop = Smooth((r - .25f) / .35f), rise = Smooth((r - .95f) / .45f);
                mag.localPosition = magRest + Vector3.down * (.28f * (r < .95f ? drop : 1 - rise));
                mag.gameObject.SetActive(r < .6f || r > .95f);
                if (empty)
                {
                    float pull = Smooth((r - 1.55f) / .12f) * (1 - Smooth((r - 1.78f) / .1f));
                    handle.localPosition = handleRest + Vector3.back * (.06f * pull);
                }
                if (!magOut && r >= .28f) { magOut = true; Sound(M4Sound.Kind.MagOut); }
                if (!magIn && r >= 1.3f) { magIn = true; Sound(M4Sound.Kind.MagIn); }
                if (!charged && r >= 1.55f) { charged = true; Sound(M4Sound.Kind.Charge); }
                yield return null;
            }
            gun.clipContent = Mathf.Min(gun.clipSize, target);
            Remember(gun.clipContent);
            ResetPose();
            ReloadingField.SetValue(gun, false);
            SetStateMethod.Invoke(gun, new object[] { GunTool.State.Idle });
            reload = null;
        }

        private static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }

        private void Sound(M4Sound.Kind kind)
        {
            M4Audio.Play(kind, transform.position, true);
            M4Armory.SendSound(kind, transform.position);
        }

        private void ResetPose()
        {
            if (model == null) return;
            model.Recoil.localPosition = Vector3.zero; model.Recoil.localRotation = Quaternion.identity;
            Transform mag = model.Joint("mag"), handle = model.Joint("charge");
            if (mag != null) { mag.localPosition = model.Rest("mag"); mag.gameObject.SetActive(true); }
            if (handle != null) handle.localPosition = model.Rest("charge");
        }

        private bool CanUse()
        {
            return gun != null && gun.isActiveAndEnabled && PlayerInput.Instance != null && PlayerInput.Instance.CanProcessInput() &&
                PlayerInventory.Instance != null && PlayerInventory.Instance.state == PlayerInventory.State.Play &&
                PlayerMovement.Instance != null && !PlayerMovement.Instance.isDead;
        }

        private void Update()
        {
            if (gun == null || model == null) return;
            float dt = Time.deltaTime;
            heat = M4Rules.CoolHeat(heat, dt, Time.time - lastShot);
            if (M4Rules.ShouldFlush(batch.Charge, Time.time - batch.Since, gun.clipContent)) Flush();
            if (CanUse() && Input.GetKeyDown(M4Armory.FireModeKey) && !Reloading)
            {
                automatic = !automatic;
                gun.triggerType = automatic ? GunTool.TriggerType.Auto : GunTool.TriggerType.Manual;
                SetSelector(); Sound(M4Sound.Kind.Mode);
                notice = M4Armory.Text(automatic ? "全自動" : "半自動", automatic ? "Full auto" : "Semi-auto"); noticeUntil = Time.unscaledTime + 1.2f;
            }
            ApplyRecoil(dt);
            kick = Mathf.MoveTowards(kick, 0, dt * 14);
            if (reload == null)
            {
                model.Recoil.localPosition = new Vector3(0, 0, -.018f * kick);
                model.Recoil.localRotation = Quaternion.Euler(-2.2f * kick, 0, 0);
            }
            Transform trigger = model.Joint("trigger");
            if (trigger != null) trigger.localRotation = Quaternion.Euler(kick > .5f ? 14 : 0, 0, 0);
            for (int i = casings.Count - 1; i >= 0; i--)
            {
                Casing c = casings[i];
                c.Life -= dt; c.Velocity += Vector3.down * 3.2f * dt;
                c.Body.localPosition += c.Velocity * dt * .35f; c.Body.Rotate(0, 720 * dt, 0, Space.Self);
                if (c.Life <= 0) { Destroy(c.Body.gameObject); casings.RemoveAt(i); }
            }
        }

        private void ApplyRecoil(float dt)
        {
            PlayerMovement player = PlayerMovement.Instance;
            if (player == null || (pitchDebt <= 0 && Mathf.Abs(yawDebt) < .0001f)) return;
            float share = Mathf.Min(1, dt * 25);
            float pitch = pitchDebt * share, yaw = yawDebt * share;
            pitchDebt -= pitch; yawDebt -= yaw;
            if (pitchDebt < .001f) pitchDebt = 0;
            PitchField.SetValue(player, Mathf.Clamp((float)PitchField.GetValue(player) - pitch, -89, 89));
            Transform body = (Transform)BodyField.GetValue(player);
            if (body != null) body.Rotate(0, yaw, 0, Space.Self);
        }

        private void SetSelector()
        {
            Transform selector = model != null ? model.Joint("selector") : null;
            if (selector != null) selector.localRotation = Quaternion.Euler(automatic ? 90 : 0, 0, 0);
        }

        private void Eject()
        {
            if (casings.Count >= 6 || model.Port == null) return;
            GameObject brass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Collider collider = brass.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            brass.name = "Tony M4 casing"; brass.layer = model.gameObject.layer;
            brass.transform.SetParent(model.Port.parent, false);
            brass.transform.localPosition = model.Port.localPosition;
            brass.transform.localRotation = Quaternion.Euler(0, 0, 90);
            brass.transform.localScale = new Vector3(.009f, .011f, .009f);
            Renderer renderer = brass.GetComponent<Renderer>();
            renderer.sharedMaterial = M4Armory.BrassMaterial; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            casings.Add(new Casing { Body = brass.transform, Velocity = new Vector3(UnityEngine.Random.Range(.9f, 1.2f), UnityEngine.Random.Range(.8f, 1.1f), -.3f), Life = .6f });
        }

        private void OnEnable() { Active = this; }

        private void OnDisable()
        {
            Flush();
            if (reload != null) { StopCoroutine(reload); reload = null; }
            ResetPose();
            pitchDebt = 0; yawDebt = 0; kick = 0;
            foreach (Casing c in casings) if (c.Body != null) Destroy(c.Body.gameObject);
            casings.Clear();
            if (Active == this) Active = null;
        }

        private void OnGUI()
        {
            if (Active != this || Event.current.type != EventType.Repaint || !CanUse()) return;
            float unit = Screen.height / 1080f;
            if (hudStyle == null)
            {
                hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
                noticeStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            hudStyle.fontSize = Mathf.RoundToInt(26 * unit); noticeStyle.fontSize = Mathf.RoundToInt(24 * unit);
            uint spare = PlayerInventory.Instance.inventory.GetItemAmount(M4Armory.AmmoId);
            string mode = Reloading ? M4Armory.Text("換彈中", "Reloading") : automatic ? M4Armory.Text("全自動", "Auto") : M4Armory.Text("半自動", "Semi");
            string text = gun.clipContent + " / " + gun.clipSize + "   " + spare + "   " + mode;
            Rect box = new Rect(Screen.width - 520 * unit, Screen.height - 150 * unit, 480 * unit, 40 * unit);
            Color previous = GUI.color;
            GUI.color = new Color(0, 0, 0, .75f); GUI.Label(new Rect(box.x + 2 * unit, box.y + 2 * unit, box.width, box.height), text, hudStyle);
            GUI.color = gun.clipContent == 0 ? new Color(1, .45f, .35f) : Color.white; GUI.Label(box, text, hudStyle);
            if (Time.unscaledTime < noticeUntil && notice != null)
            {
                Rect center = new Rect(Screen.width / 2f - 200 * unit, Screen.height * .62f, 400 * unit, 40 * unit);
                GUI.color = new Color(0, 0, 0, .75f); GUI.Label(new Rect(center.x + 2 * unit, center.y + 2 * unit, center.width, center.height), notice, noticeStyle);
                GUI.color = Color.white; GUI.Label(center, notice, noticeStyle);
            }
            GUI.color = previous;
        }
    }
}
