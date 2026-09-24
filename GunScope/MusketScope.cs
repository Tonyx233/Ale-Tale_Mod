using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TonyMods
{
    // Attached only to the native Musket_fp: Crossbow_fp also uses GunTool.
    public sealed class MusketScope : MonoBehaviour
    {
        private const string PatchId = "Tony.MusketScope";
        private static readonly FieldInfo StateField = AccessTools.Field(typeof(GunTool), "_state");
        private static readonly FieldInfo ReloadField = AccessTools.Field(typeof(GunTool), "_isReloading");
        private static ConfigEntry<bool> enabledSetting;
        private static ConfigEntry<float> magnification;
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
        private bool aiming, failed;
        private float originalFov, appliedFov;

        internal static void Initialize(ConfigFile config, ManualLogSource logger)
        {
            log = logger;
            enabledSetting = config.Bind("GunScope", "Enabled", true, "Add a physical scope to the musket. Aim/block toggles magnification (default right mouse / controller L2).");
            magnification = config.Bind("GunScope", "Magnification", 3f,
                new ConfigDescription("Optical magnification; original damage and shot spread are preserved.", new AcceptableValueRange<float>(1.5f, 6f)));
            if (StateField == null || ReloadField == null) throw new MissingFieldException("GunTool scope API changed");
            harmony = new Harmony(PatchId);
            try
            {
                Patch("Update", "GunUpdated", false);
                Patch("RaycastShot", "BeforeRaycast", true);
                harmony.Patch(AccessTools.Method(typeof(GunTool), "RaycastShot"), finalizer: new HarmonyMethod(typeof(MusketScope), "AfterRaycast"));
                harmony.Patch(AccessTools.Method(typeof(PlayerInput), "GetLookInput"), postfix: new HarmonyMethod(typeof(MusketScope), "ScaleLook"));
                log.LogInfo("Musket scope ready: physical brass scope + toggle aim + 3x default; native shot spread preserved.");
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
            if (!enabledSetting.Value || __instance.name != "Musket_fp") return;
            MusketScope scope = __instance.GetComponent<MusketScope>();
            if (scope == null)
            {
                scope = __instance.gameObject.AddComponent<MusketScope>();
                scope.gun = __instance;
                try { scope.BuildModel(); }
                catch (Exception ex) { scope.Fail(ex); }
            }
            if (scope.failed) return;
            try { scope.TickInput(); }
            catch (Exception ex) { scope.Fail(ex); }
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
            if (aiming) ExitAim();
            else
            {
                if (active != null) active.ExitAim();
                cam = PlayerMovement.Instance.mainCamera;
                if (cam == null || cam.orthographic) return;
                originalFov = cam.fieldOfView;
                appliedFov = originalFov;
                aiming = true; active = this;
                HideHands();
                ApplyZoom();
            }
        }

        private void LateUpdate()
        {
            if (model != null && model.activeSelf != enabledSetting.Value) model.SetActive(enabledSetting.Value);
            if (!aiming) return;
            try
            {
                if (!CanAim() || cam == null || cam != PlayerMovement.Instance.mainCamera) { ExitAim(); return; }
                // Video settings can write a new FOV while the scope is open.
                if (Mathf.Abs(cam.fieldOfView - appliedFov) > .01f) originalFov = cam.fieldOfView;
                ApplyZoom();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private void ApplyZoom()
        {
            appliedFov = ScopeMath.ZoomFov(originalFov, magnification.Value);
            cam.fieldOfView = appliedFov;
        }

        private void ExitAim()
        {
            if (aiming && cam != null && Mathf.Abs(cam.fieldOfView - appliedFov) < .01f) cam.fieldOfView = originalFov;
            aiming = false; cam = null;
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

        private void OnDisable() { ExitAim(); }
        private void OnApplicationFocus(bool focused) { if (!focused) ExitAim(); }
        private void OnDestroy()
        {
            ExitAim();
            if (model != null) Destroy(model);
            if (metal != null) Destroy(metal);
            if (brass != null) Destroy(brass);
            if (glass != null) Destroy(glass);
            if (mask != null) Destroy(mask);
        }
        private void Fail(Exception ex)
        {
            ExitAim(); failed = true; enabled = false;
            if (model != null) model.SetActive(false);
            log.LogError("Musket scope disabled for this weapon: " + ex);
        }

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
            Transform mount = transform.Find("MusketRoot/Musket");
            if (mount == null || mount.Find("Musket1_2_1") == null) throw new InvalidOperationException("Native musket model mount missing");
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
            log.LogInfo("Physical scope attached to Musket_fp/MusketRoot/Musket.");
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
            GUI.DrawTexture(new Rect(cx - side * .46f, cy - line / 2, side * .92f, line), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - line / 2, cy - side * .46f, line, side * .92f), Texture2D.whiteTexture);
            GUI.color = new Color(.8f, .14f, .1f);
            GUI.DrawTexture(new Rect(cx - line, cy - line, line * 2, line * 2), Texture2D.whiteTexture);
            GUI.color = previous; GUI.depth = depth; GUI.matrix = matrix;
        }
    }
}
