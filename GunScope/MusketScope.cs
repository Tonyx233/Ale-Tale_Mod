using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TonyMods
{
    // Attached to the native Musket_fp (brass scope, 3x/6x) and to the M4A1, whose optic is
    // interchangeable (M4Scopes): magnified optics use the scope overlay, 1x sights align the real
    // sight picture by moving FPView/Hands. Crossbow_fp also uses GunTool and stays unchanged.
    public sealed class MusketScope : MonoBehaviour
    {
        private const string PatchId = "Tony.MusketScope";
        // Held guns are Instantiate() clones named "Musket_fp(Clone)", and Crossbow_fp also has
        // MusketRoot/Musket, so only the native musket mesh identifies the musket.
        private const string MountPath = "MusketRoot/Musket", MusketMesh = "Musket1_2_1";
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(GunTool), "_state");
        private static readonly FieldInfo ReloadField = AccessTools.Field(typeof(GunTool), "_isReloading");
        private static ConfigEntry<bool> enabledSetting;
        private static ManualLogSource log;
        private static Harmony harmony;
        private static MusketScope active;
        private GunTool gun;
        private Camera cam;
        private GameObject model;
        private Material metal, brass, glass;
        private Texture2D mask;
        private Renderer[] hiddenRenderers;
        private bool[] rendererStates;
        private ScopeCycle cycle = new ScopeCycle();
        private bool rifle;
        // M4 optic; null on the musket. ADS state for 1x sights (fpHands has no native writer).
        private M4ScopeProfile optic;
        private const float AdsSeconds = .14f;
        private float ads;
        private Transform hands;
        private Vector3 handsPos;
        private Quaternion handsRot;
        private Texture2D disc, ring;
        private bool Aligned { get { return optic != null && !optic.Magnified; } }
        private bool aiming { get { return cycle.IsActive; } }
        private bool failed;
        private GUIStyle zoomLabel;
        private float originalFov, appliedFov;

        internal static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            log = logger;
            enabledSetting = config.Bind("GunScope", "Enabled", true, "Add a physical scope to the musket. Aim/block cycles 3x, 6x, off (default right mouse / controller L2).");
            if (StateField == null || ReloadField == null) throw new MissingFieldException("GunTool scope API changed");
            harmony = new Harmony(PatchId);
            try
            {
                Patch("Update", "GunUpdated", false);
                Patch("RaycastShot", "BeforeRaycast", true);
                harmony.Patch(AccessTools.Method(typeof(GunTool), "RaycastShot"), finalizer: new HarmonyMethod(typeof(MusketScope), "AfterRaycast"));
                harmony.Patch(AccessTools.Method(typeof(PlayerInput), "GetLookInput"), postfix: new HarmonyMethod(typeof(MusketScope), "ScaleLook"));
                log.LogInfo("Musket scope ready: physical brass scope + 3x / 6x / off aim cycle, M4A1 ACOG single stage; native shot spread preserved.");
            }
            catch { harmony.UnpatchSelf(); harmony = null; throw; }
        }

        private static void Patch(string target, string handler, bool prefix)
        {
            MethodInfo method = AccessTools.Method(typeof(GunTool), target);
            if (method == null) throw new MissingMethodException("GunTool", target);
            HarmonyMethod patch = new HarmonyMethod(typeof(MusketScope), handler);
            harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }

        internal static void Shutdown()
        {
            foreach (MusketScope scope in Resources.FindObjectsOfTypeAll<MusketScope>())
            { scope.ExitAim(); Destroy(scope); }
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
        }

        private static void GunUpdated(GunTool __instance)
        {
            if (!enabledSetting.Value) return;
            MusketScope scope = __instance.GetComponent<MusketScope>();
            if (scope == null)
            {
                // The M4 is spawned from Musket_fp, so it must be checked before the musket mesh.
                bool m4 = M4Armory.IsM4(__instance);
                if (!m4 && __instance.transform.Find(MountPath + "/" + MusketMesh) == null) return;
                scope = __instance.gameObject.AddComponent<MusketScope>();
                scope.gun = __instance; scope.rifle = m4;
                if (m4) scope.UseOptic(M4Rifle.ProfileFor(__instance));
                else scope.cycle = new ScopeCycle(3, 6);
                if (!m4)
                {
                    try { scope.BuildModel(); }
                    catch (Exception ex) { scope.Fail(ex); }
                }
            }
            if (scope.failed) return;
            try { scope.TickInput(); }
            catch (Exception ex) { scope.Fail(ex); }
        }

        private void UseOptic(M4ScopeProfile profile)
        {
            optic = profile ?? M4Scopes.Profile(M4ScopeKind.Acog);
            cycle = new ScopeCycle(M4Scopes.Stages(optic.Kind, M4Armory.ScopePower));
        }

        // The rifle's optic changed (attach / detach): close the scope and take the new profile.
        internal static void Refresh(GunTool gun)
        {
            MusketScope scope = gun != null ? gun.GetComponent<MusketScope>() : null;
            if (scope == null || !scope.rifle) return;
            scope.ExitAim();
            scope.UseOptic(M4Rifle.ProfileFor(gun));
        }

        private bool CanAim()
        {
            return enabledSetting.Value && Application.isFocused && gun != null && gun.isActiveAndEnabled &&
                PlayerInventory.Instance != null && PlayerInventory.Instance.state == PlayerInventory.State.Play &&
                PlayerInput.Instance != null && PlayerInput.Instance.CanProcessInput() &&
                PlayerMovement.Instance != null && !PlayerMovement.Instance.isDead &&
                PlayerNet.Instance != null && PlayerNet.Instance.IsSpawned &&
                !(bool)ReloadField.GetValue(gun) && (GunTool.State)StateField.GetValue(gun) != GunTool.State.Reload;
        }

        private void TickInput()
        {
            if (model != null) model.SetActive(enabledSetting.Value);
            if (!CanAim()) { ExitAim(); return; }
            if (!PlayerInput.Instance.GetAimInputDown()) return;
            if (aiming)
            {
                if (cycle.IsLastStage) ExitAim();
                else { cycle.Advance(); ApplyZoom(); }
            }
            else
            {
                if (active != null) active.ExitAim();
                cam = PlayerMovement.Instance.mainCamera;
                if (cam == null || cam.orthographic) return;
                originalFov = cam.fieldOfView;
                appliedFov = originalFov;
                cycle.Advance(); active = this;
                if (!Aligned) HideHands();
                ApplyZoom();
            }
        }

        private void LateUpdate()
        {
            if (model != null && model.activeSelf != enabledSetting.Value) model.SetActive(enabledSetting.Value);
            try
            {
                if (aiming)
                {
                    if (!CanAim() || cam == null || cam != PlayerMovement.Instance.mainCamera) ExitAim();
                    else
                    {
                        // Video settings can write a new FOV while the scope is open.
                        if (Mathf.Abs(cam.fieldOfView - appliedFov) > .01f) originalFov = cam.fieldOfView;
                        ApplyZoom();
                    }
                }
                UpdateAds();
            }
            catch (Exception ex) { Fail(ex); }
        }

        // Blend the whole first-person rig (FPView/Hands) so the optic's sight point sits on the
        // camera axis at the profile's eye distance, bore parallel to the view. Runs after the
        // Animator, so idle sway is cancelled; the anchor is outside the Recoil node, so kick stays.
        private void UpdateAds()
        {
            ads = Mathf.MoveTowards(ads, aiming && Aligned ? 1 : 0, Time.deltaTime / AdsSeconds);
            Transform anchor = Aligned ? M4Rifle.AnchorFor(gun) : null;
            PlayerMovement player = PlayerMovement.Instance;
            Camera view = player != null ? player.mainCamera : null;
            if (ads <= 0 || anchor == null || view == null || player.fpHands == null || player.fpHands.parent == null) { ads = anchor == null ? 0 : ads; RestoreHands(); return; }
            if (hands != player.fpHands) { RestoreHands(); hands = player.fpHands; handsPos = hands.localPosition; handsRot = hands.localRotation; }
            hands.localPosition = handsPos; hands.localRotation = handsRot;
            Transform parent = hands.parent;
            Vector3 sight = parent.InverseTransformPoint(anchor.position);
            Vector3 along = parent.InverseTransformDirection(anchor.forward), up = parent.InverseTransformDirection(anchor.up);
            Transform eye = view.transform;
            Vector3 target = parent.InverseTransformPoint(eye.position + eye.forward * optic.EyeDistance);
            Quaternion turn = Quaternion.LookRotation(parent.InverseTransformDirection(eye.forward), parent.InverseTransformDirection(eye.up)) *
                Quaternion.Inverse(Quaternion.LookRotation(along, up));
            hands.localPosition = Vector3.Lerp(handsPos, target - turn * (sight - handsPos), ads);
            hands.localRotation = Quaternion.Slerp(handsRot, turn * handsRot, ads);
        }

        private void RestoreHands()
        {
            if (hands != null) { hands.localPosition = handsPos; hands.localRotation = handsRot; }
            hands = null;
        }

        private void ApplyZoom()
        {
            appliedFov = ScopeMath.ZoomFov(originalFov, cycle.Magnification);
            cam.fieldOfView = appliedFov;
        }

        private void ExitAim()
        {
            if (aiming && cam != null && Mathf.Abs(cam.fieldOfView - appliedFov) < .01f) cam.fieldOfView = originalFov;
            cycle.Reset(); cam = null;
            if (hiddenRenderers != null)
                for (int i = 0; i < hiddenRenderers.Length; i++)
                    if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = rendererStates[i];
            hiddenRenderers = null; rendererStates = null;
            if (active == this) active = null;
        }

        private void HideHands()
        {
            Transform hands = PlayerMovement.Instance.fpHands;
            List<Renderer> renderers = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
            if (hands != null)
                foreach (Renderer renderer in hands.GetComponentsInChildren<Renderer>(true))
                    if (!renderers.Contains(renderer)) renderers.Add(renderer);
            hiddenRenderers = renderers.ToArray();
            rendererStates = new bool[hiddenRenderers.Length];
            for (int i = 0; i < hiddenRenderers.Length; i++)
            { rendererStates[i] = hiddenRenderers[i].enabled; hiddenRenderers[i].enabled = false; }
        }

        private void OnDisable() { ExitAim(); ads = 0; RestoreHands(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ExitAim(); }
        private void OnDestroy()
        {
            ExitAim(); ads = 0; RestoreHands();
            if (disc != null) Destroy(disc);
            if (ring != null) Destroy(ring);
            if (model != null) Destroy(model);
            if (metal != null) Destroy(metal);
            if (brass != null) Destroy(brass);
            if (glass != null) Destroy(glass);
            if (mask != null) Destroy(mask);
        }
        private void Fail(Exception ex)
        {
            ExitAim(); ads = 0; RestoreHands(); failed = true; enabled = false;
            if (model != null) model.SetActive(false);
            log.LogError("Musket scope disabled for this weapon: " + ex);
        }

        internal static bool IsScoped(GunTool gun) { return active != null && active.gun == gun && active.aiming; }

        private static void ScaleLook(ref Vector2 __result)
        {
            if (active != null && active.aiming) __result *= ScopeMath.LookScale(active.originalFov, active.appliedFov);
        }

        private struct ShotView { public Camera Camera; public float Fov; }
        private static void BeforeRaycast(GunTool __instance, out ShotView __state)
        {
            __state = new ShotView();
            if (active == null || active.gun != __instance || !active.aiming || active.cam == null) return;
            // The native ray uses spreadAngle/FOV. Run its calculation with the original
            // projection, then restore the scoped projection before Unity renders a frame.
            __state.Camera = active.cam; __state.Fov = active.cam.fieldOfView;
            active.cam.fieldOfView = active.originalFov;
        }
        private static Exception AfterRaycast(Exception __exception, ShotView __state)
        {
            if (__state.Camera != null) __state.Camera.fieldOfView = __state.Fov;
            return __exception;
        }

        private Material MakeMaterial(string name, Color color, float metallic)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("No scope material shader");
            Material material = new Material(shader); material.name = name;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .65f);
            return material;
        }

        private void BuildModel()
        {
            Transform mount = transform.Find(MountPath);
            if (mount == null || mount.Find(MusketMesh) == null) throw new InvalidOperationException("Native musket model mount missing");
            metal = MakeMaterial("Tony Scope Blued Steel", new Color(.065f, .073f, .08f), .75f);
            brass = MakeMaterial("Tony Scope Brass", new Color(.43f, .29f, .11f), .8f);
            glass = MakeMaterial("Tony Scope Glass", new Color(.04f, .19f, .22f), .6f);
            model = new GameObject("Tony Musket Scope"); model.transform.SetParent(mount, false);
            // Verified native mesh: barrel points along -X, half-length .422, top .077.
            model.transform.localPosition = new Vector3(-.055f, .115f, 0);
            model.layer = mount.gameObject.layer;
            Part("Tube", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.044f, .115f, .044f), metal, true);
            Part("Objective", PrimitiveType.Cylinder, new Vector3(-.12f, 0, 0), new Vector3(.064f, .029f, .064f), brass, true);
            Part("Eyepiece", PrimitiveType.Cylinder, new Vector3(.12f, 0, 0), new Vector3(.054f, .024f, .054f), brass, true);
            Part("Front lens", PrimitiveType.Cylinder, new Vector3(-.150f, 0, 0), new Vector3(.052f, .001f, .052f), glass, true);
            Part("Rear lens", PrimitiveType.Cylinder, new Vector3(.145f, 0, 0), new Vector3(.044f, .001f, .044f), glass, true);
            foreach (float x in new float[] { -.065f, .065f })
            {
                Part("Mount ring", PrimitiveType.Cylinder, new Vector3(x, 0, 0), new Vector3(.051f, .009f, .051f), brass, true);
                Part("Mount foot", PrimitiveType.Cube, new Vector3(x, -.039f, 0), new Vector3(.022f, .055f, .035f), metal, false);
            }
            Part("Elevation dial", PrimitiveType.Cylinder, new Vector3(0, .030f, 0), new Vector3(.027f, .009f, .027f), brass, false);
            log.LogInfo("Physical scope attached to " + name + "/" + MountPath + ".");
        }

        private void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale, Material material, bool barrelAxis)
        {
            GameObject part = GameObject.CreatePrimitive(type); part.name = name;
            part.transform.SetParent(model.transform, false); part.layer = model.layer;
            part.transform.localPosition = position; part.transform.localScale = scale;
            if (barrelAxis) part.transform.localRotation = Quaternion.Euler(0, 0, 90);
            Collider collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            Renderer renderer = part.GetComponent<Renderer>(); renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private void BuildMask()
        {
            const int size = 512;
            mask = new Texture2D(size, size, TextureFormat.RGBA32, false);
            mask.name = "Tony Scope Overlay"; mask.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f - size / 2) / (size / 2f), dy = (y + .5f - size / 2) / (size / 2f);
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                byte alpha = (byte)(255 * Mathf.Clamp01((r - .94f) / .045f));
                pixels[y * size + x] = new Color32(0, 0, 0, alpha);
            }
            mask.SetPixels32(pixels); mask.Apply(false, true);
        }

        private void OnGUI()
        {
            if (!aiming || Event.current.type != EventType.Repaint) return;
            if (Aligned) { if (ads > .85f) DrawSight(); return; }
            if (mask == null) BuildMask();
            Color previous = GUI.color; int depth = GUI.depth; Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity;
            GUI.depth = -10000; GUI.color = Color.black;
            float side = Mathf.Min(Screen.width, Screen.height) * .94f;
            float x = (Screen.width - side) / 2, y = (Screen.height - side) / 2;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, y), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, y + side, Screen.width, Screen.height - y - side), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(0, y, x, side), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x + side, y, Screen.width - x - side, side), Texture2D.whiteTexture);
            GUI.color = Color.white; GUI.DrawTexture(new Rect(x, y, side, side), mask);
            GUI.color = Color.black;
            float line = Mathf.Max(1, Screen.height / 720f), cx = Screen.width / 2f, cy = Screen.height / 2f;
            if (rifle && optic.Reticle == M4Reticle.Chevron) DrawAcog(cx, cy, side, line);
            else if (rifle && optic.Reticle == M4Reticle.MilDot) DrawMilDot(cx, cy, side, line);
            else
            {
                GUI.DrawTexture(new Rect(cx - side * .46f, cy - line / 2, side * .92f, line), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - line / 2, cy - side * .46f, line, side * .92f), Texture2D.whiteTexture);
                GUI.color = new Color(.8f, .14f, .1f);
                GUI.DrawTexture(new Rect(cx - line, cy - line, line * 2, line * 2), Texture2D.whiteTexture);
            }
            if (zoomLabel == null) zoomLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            zoomLabel.fontSize = Mathf.RoundToInt(18 * line);
            zoomLabel.normal.textColor = Color.white;
            GUI.color = Color.white;
            GUI.Label(new Rect(cx - 50 * line, cy + side * .32f, 100 * line, 30 * line), cycle.Magnification + "x", zoomLabel);
            GUI.color = previous; GUI.depth = depth; GUI.matrix = matrix;
        }

        // 1x sights: the sight itself is on screen (ADS); only the projected reticle is drawn.
        private void DrawSight()
        {
            if (cam == null || optic.Reticle == M4Reticle.None) return;
            if (disc == null) disc = Circle("Tony sight dot", 64, 0);
            if (ring == null) ring = Circle("Tony sight ring", 256, .045f);
            Color previous = GUI.color; int depth = GUI.depth; Matrix4x4 matrix = GUI.matrix;
            GUI.matrix = Matrix4x4.identity; GUI.depth = -10000;
            float cx = Screen.width / 2f, cy = Screen.height / 2f, unit = Screen.height / 1080f;
            Color red = new Color(1f, .16f, .1f);
            if (optic.Reticle == M4Reticle.Dot)
            {
                float r = Mathf.Max(2.6f * unit, M4Scopes.MoaToPixels(M4Scopes.DotMoa, cam.fieldOfView, Screen.height));
                GUI.color = new Color(red.r, red.g, red.b, .22f); GUI.DrawTexture(new Rect(cx - r * 2.4f, cy - r * 2.4f, r * 4.8f, r * 4.8f), disc);
                GUI.color = red; GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2, r * 2), disc);
            }
            else
            {
                float rr = Mathf.Max(18 * unit, M4Scopes.MoaToPixels(M4Scopes.HoloRingMoa, cam.fieldOfView, Screen.height));
                float rd = Mathf.Max(1.8f * unit, M4Scopes.MoaToPixels(M4Scopes.HoloDotMoa, cam.fieldOfView, Screen.height));
                GUI.color = red;
                GUI.DrawTexture(new Rect(cx - rr, cy - rr, rr * 2, rr * 2), ring);
                GUI.DrawTexture(new Rect(cx - rd, cy - rd, rd * 2, rd * 2), disc);
                float tick = rr * .22f, w = Mathf.Max(1.5f, rr * .09f);
                GUI.DrawTexture(new Rect(cx - w / 2, cy - rr - tick * .3f, w, tick), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - w / 2, cy + rr - tick * .7f, w, tick), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - rr - tick * .3f, cy - w / 2, tick, w), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx + rr - tick * .7f, cy - w / 2, tick, w), Texture2D.whiteTexture);
            }
            GUI.color = previous; GUI.depth = depth; GUI.matrix = matrix;
        }

        // Anti-aliased white disc (thickness 0) or ring (thickness as a fraction of the radius).
        private static Texture2D Circle(string name, int size, float thickness)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            float half = size / 2f, edge = 1.5f / half;
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f - half) / half, dy = (y + .5f - half) / half, r = Mathf.Sqrt(dx * dx + dy * dy);
                float outer = Mathf.Clamp01((1 - edge - r) / edge);
                float inner = thickness > 0 ? Mathf.Clamp01((r - (1 - edge - thickness)) / edge) : 1;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(255 * outer * inner));
            }
            texture.SetPixels32(pixels); texture.Apply(false, true);
            return texture;
        }

        // Long-range reticle: thin crosshair, heavy outer posts and mil dots.
        private void DrawMilDot(float cx, float cy, float side, float line)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cx - side * .3f, cy - line / 2, side * .6f, line), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - line / 2, cy - side * .3f, line, side * .6f), Texture2D.whiteTexture);
            float post = line * 3.5f;
            GUI.DrawTexture(new Rect(cx - side * .47f, cy - post / 2, side * .17f, post), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + side * .3f, cy - post / 2, side * .17f, post), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - post / 2, cy + side * .3f, post, side * .17f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - post / 2, cy - side * .47f, post, side * .17f), Texture2D.whiteTexture);
            float dot = line * 2.6f;
            for (int k = 1; k <= 4; k++)
            {
                float d = side * .06f * k;
                GUI.DrawTexture(new Rect(cx + d - dot / 2, cy - dot / 2, dot, dot), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - d - dot / 2, cy - dot / 2, dot, dot), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - dot / 2, cy + d - dot / 2, dot, dot), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - dot / 2, cy - d - dot / 2, dot, dot), Texture2D.whiteTexture);
            }
            GUI.color = new Color(.85f, .15f, .1f);
            GUI.DrawTexture(new Rect(cx - line, cy - line, line * 2, line * 2), Texture2D.whiteTexture);
        }

        // TA31-style reticle: red chevron with its tip on the aim point, bullet-drop stadia below.
        private void DrawAcog(float cx, float cy, float side, float line)
        {
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(cx - line / 2, cy + side * .075f, line, side * .21f), Texture2D.whiteTexture);
            float[] widths = { .09f, .07f, .055f, .04f };
            string[] marks = { "4", "5", "6", "8" };
            if (zoomLabel == null) zoomLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
            zoomLabel.fontSize = Mathf.RoundToInt(12 * line); zoomLabel.normal.textColor = Color.black;
            for (int i = 0; i < widths.Length; i++)
            {
                float y = cy + side * (.11f + i * .055f), w = side * widths[i];
                GUI.DrawTexture(new Rect(cx - w / 2, y - line / 2, w, line), Texture2D.whiteTexture);
                GUI.Label(new Rect(cx + w / 2 + 2 * line, y - 9 * line, 20 * line, 18 * line), marks[i], zoomLabel);
            }
            Color red = new Color(.93f, .27f, .14f);
            float arm = side * .045f, width = Mathf.Max(2, line * 2.4f);
            Line(new Vector2(cx, cy), new Vector2(cx - arm * .62f, cy + arm), width, red);
            Line(new Vector2(cx, cy), new Vector2(cx + arm * .62f, cy + arm), width, red);
        }

        private static void Line(Vector2 from, Vector2 to, float width, Color color)
        {
            Vector2 d = to - from;
            Matrix4x4 matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, from);
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - width / 2, d.magnitude, width), Texture2D.whiteTexture);
            GUI.matrix = matrix;
        }
    }
}
