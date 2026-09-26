using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TonyMods
{
    public sealed class TideModel : MonoBehaviour
    {
        [Serializable] private sealed class Bone { public string name, parent; public float[] p; }
        [Serializable] private sealed class Part { public string name, bone; public int material; public float[] vertices, uv; public int[] triangles; }
        [Serializable] private sealed class Definition { public Bone[] bones; public Part[] parts; public float[][] materials; public string[] textures; }
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private readonly List<Texture2D> textures = new List<Texture2D>();
        private Transform orb;
        private LineRenderer effect, warning;
        private Material glow, water;
        private readonly List<Transform> droplets = new List<Transform>();
        // 潮彈 visuals are placed in world space: the idol walks on while its shells are still in the air.
        private sealed class ShellFx { public Transform orb; public LineRenderer trail, mark, timer, burst; public Transform[] spray; }
        private readonly ShellFx[] shells = new ShellFx[TideRules.ShotCount];
        private bool shellsShown;
        private float gait;
        internal static TideModel Create(Transform parent)
        {
            GameObject go = new GameObject("Tidefork authored model"); go.transform.SetParent(parent, false);
            TideModel model = go.AddComponent<TideModel>();
            try { model.Build(); return model; } catch { Destroy(go); throw; }
        }
        private void Build()
        {
            Definition data;
            using (Stream stream = typeof(TideModel).Assembly.GetManifestResourceStream("Tony.Tidefork.model.json"))
            using (var reader = new StreamReader(stream)) data = Newtonsoft.Json.JsonConvert.DeserializeObject<Definition>(reader.ReadToEnd());
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Tidefork material shader unavailable");
            for (int i = 0; i < data.materials.Length; i++)
            {
                float[] rgb = data.materials[i]; var color = new Color(rgb[0], rgb[1], rgb[2]);
                var material = new Material(shader); material.name = "Tidefork palette " + i;
                material.SetColor("_Color", color); material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", i == 1 || i == 4 ? .02f : .48f);
                material.SetFloat("_Smoothness", i == 1 ? .19f : .28f);
                if (data.textures != null && i < data.textures.Length)
                {
                    using (Stream image = typeof(TideModel).Assembly.GetManifestResourceStream("Tony.Tidefork." + data.textures[i]))
                    using (var bytes = new MemoryStream())
                    {
                        if (image == null) throw new InvalidDataException("Missing Tidefork texture: " + data.textures[i]);
                        image.CopyTo(bytes);
                        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
                        textures.Add(texture);
                        if (!ImageConversion.LoadImage(texture, bytes.ToArray(), false)) throw new InvalidDataException("Invalid Tidefork texture");
                        texture.name = "Tidefork " + data.textures[i]; texture.wrapMode = TextureWrapMode.Repeat;
                        texture.filterMode = FilterMode.Trilinear; texture.anisoLevel = 4;
                        material.SetTexture("_MainTex", texture); material.SetTexture("_BaseMap", texture);
                    }
                }
                if (i == 3 || i == 5) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * (i == 3 ? .35f : 1.4f)); }
                materials.Add(material);
            }
            foreach (Bone bone in data.bones)
            {
                Transform node = new GameObject(bone.name).transform;
                node.SetParent(String.IsNullOrEmpty(bone.parent) ? transform : bones[bone.parent], false);
                node.localPosition = new Vector3(bone.p[0], bone.p[1], bone.p[2]); bones.Add(bone.name, node);
            }
            // Group by animated bone and material; preserve per-corner UVs and flat normals.
            var uvs = new Dictionary<string, List<Vector2>>();
            var groups = new Dictionary<string, List<Vector3>>(); var owners = new Dictionary<string, Part>();
            foreach (Part part in data.parts)
            {
                string key = part.bone + "|" + part.material; List<Vector3> corners;
                if (!groups.TryGetValue(key, out corners)) { groups.Add(key, corners = new List<Vector3>()); owners.Add(key, part); uvs.Add(key, new List<Vector2>()); }
                if (part.uv == null || part.uv.Length != part.triangles.Length * 2)
                    throw new InvalidDataException("Invalid Tidefork UV count: " + part.name);
                for (int i = 0; i < part.triangles.Length; i++)
                {
                    int at = part.triangles[i] * 3;
                    corners.Add(new Vector3(part.vertices[at], part.vertices[at + 1], part.vertices[at + 2]));
                    uvs[key].Add(new Vector2(part.uv[i * 2], part.uv[i * 2 + 1]));
                }
            }
            foreach (var group in groups)
            {
                Part part = owners[group.Key];
                var go = new GameObject("Tidefork " + group.Key); go.transform.SetParent(bones[part.bone], false);
                var triangles = new int[group.Value.Count];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
                var mesh = new Mesh { name = go.name, vertices = group.Value.ToArray(), triangles = triangles, uv = uvs[group.Key].ToArray() };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = materials[part.material];
            }
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.name = "Tidefork thrown seal";
            DestroyImmediate(ball.GetComponent<Collider>()); ball.GetComponent<MeshRenderer>().sharedMaterial = materials[1];
            orb = ball.transform; orb.SetParent(transform, false); orb.localScale = Vector3.one * .27f;
            Shader fxShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? shader;
            glow = new Material(fxShader); glow.SetColor("_Color", new Color(.15f, .9f, 1)); glow.SetColor("_BaseColor", new Color(.15f, .9f, 1));
            effect = MakeLine("Tidefork attack water", .055f, false); warning = MakeLine("Tidefork telegraph", .022f, false);
            for (int i = 0; i < 12; i++) droplets.Add(Drop("Tidefork water droplet " + i, materials[3], .035f));
            water = new Material(shader); water.name = "Tidefork shell water";
            water.SetColor("_Color", new Color(.25f, .78f, .9f)); water.SetColor("_BaseColor", new Color(.25f, .78f, .9f));
            water.SetFloat("_Metallic", 0); water.SetFloat("_Smoothness", .85f);
            water.EnableKeyword("_EMISSION"); water.SetColor("_EmissionColor", new Color(.1f, .75f, .9f) * 1.3f);
            for (int k = 0; k < shells.Length; k++)
            {
                var fx = new ShellFx { orb = Drop("Tidefork shell " + k, water, .36f), spray = new Transform[6] };
                fx.trail = MakeLine("Tidefork shell trail " + k, .12f, true); fx.trail.endWidth = .02f; fx.trail.endColor = new Color(.12f, .86f, .95f, 0);
                fx.mark = MakeLine("Tidefork shell landing " + k, .03f, true); fx.timer = MakeLine("Tidefork shell timer " + k, .05f, true);
                fx.burst = MakeLine("Tidefork shell splash " + k, .07f, true);
                for (int i = 0; i < fx.spray.Length; i++) fx.spray[i] = Drop("Tidefork shell spray " + k + "." + i, water, .06f);
                shells[k] = fx;
            }
        }
        private Transform Drop(string name, Material material, float size)
        {
            GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere); drop.name = name;
            DestroyImmediate(drop.GetComponent<Collider>()); drop.GetComponent<MeshRenderer>().sharedMaterial = material;
            drop.transform.SetParent(transform, false); drop.transform.localScale = Vector3.one * size;
            drop.SetActive(false); return drop.transform;
        }
        private LineRenderer MakeLine(string name, float width, bool world)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = glow; line.useWorldSpace = world;
            line.startWidth = line.endWidth = width; line.startColor = line.endColor = new Color(.12f, .86f, .95f, .85f);
            line.enabled = false; return line;
        }
        private static void Ring(LineRenderer line, float radius, float y, float begin, float end)
        {
            line.enabled = true; line.positionCount = 49;
            for (int i = 0; i < 49; i++) { float a = Mathf.Lerp(begin, end, i / 48f) * Mathf.Deg2Rad; line.SetPosition(i, new Vector3(Mathf.Sin(a) * radius, y, Mathf.Cos(a) * radius)); }
        }
        internal void Animate(byte action, float age, float speed, float hitFlash, Vector3 ballPosition)
        {
            bool flight = action == TideRules.Summon && age < TideRules.Flight;
            orb.gameObject.SetActive(flight); if (flight) orb.position = ballPosition;
            bones["body"].gameObject.SetActive(!flight);
            effect.enabled = warning.enabled = false;
            float rise = action == TideRules.Summon ? Mathf.Clamp01((age - TideRules.Flight) / TideRules.Rise) : 1;
            float dying = action == TideRules.Death ? Mathf.Clamp01(age / TideRules.Duration(TideRules.Death)) : 0;
            bones["body"].localScale = Vector3.one * Mathf.Max(.02f, rise * (1 - dying * .8f));
            bones["body"].localPosition = Vector3.down * dying * .3f;
            float weight = action == TideRules.Walk ? Mathf.Clamp01(speed / 1.5f) : 0;
            gait += Time.deltaTime * (1.4f + speed * 3);
            float step = Mathf.Sin(gait), bob = Mathf.Abs(step) * .035f * weight;
            Transform torso = bones["torso"];
            torso.localPosition = new Vector3(0, bob, 0);
            torso.localRotation = Quaternion.Euler(0, 0, step * 4 * weight + hitFlash * 20);
            bones["neck"].localRotation = Quaternion.Euler(Mathf.Sin(gait - .4f) * (1 + weight * 4), 0, -step * 2 * weight);
            bones["crown"].localScale = new Vector3(1 + Mathf.Sin(gait * .5f) * .035f, 1, 1);
            bones["tail"].localRotation = Quaternion.Euler(0, Mathf.Sin(gait - .8f) * (3 + weight * 8), 0);
            bones["jaw"].localRotation = Quaternion.Euler(0, 0, -2 + Mathf.Sin(gait * .4f) * 2);
            for (int i = 0; i < 2; i++)
            {
                float phase = i == 0 ? step : -step; Transform foot = bones[i == 0 ? "footL" : "footR"];
                foot.localPosition = new Vector3(i == 0 ? -.19f : .19f, .48f + Mathf.Max(0, phase) * .14f * weight, phase * .12f * weight);
                foot.localRotation = Quaternion.Euler(phase * 18 * weight, 0, 0);
            }
            float windup = TideRules.Windup(action), release = age - windup;
            float charge = windup > 0 ? Mathf.Clamp01(age / windup) : 0;
            float pulse = Mathf.Clamp01(1 - Mathf.Abs(release) / .4f);
            for (int i = 0; i < droplets.Count; i++)
            {
                bool active = action >= TideRules.Bite && action <= TideRules.Wave && release >= 0 && release < .6f || hitFlash > 0;
                Transform drop = droplets[i]; drop.gameObject.SetActive(active);
                if (!active) continue;
                float t = hitFlash > 0 ? .3f - hitFlash : release;
                float a = i * 2.39996f;
                float radius = action == TideRules.Wave ? Mathf.Min(TideRules.WaveRadius, .4f + t * TideRules.WaveRadius * 2.25f) : .25f + t * 2.8f;
                Vector3 center = action == TideRules.Bite ? Vector3.forward * (TideRules.BiteReach * .72f) : Vector3.zero;
                drop.localPosition = center + new Vector3(Mathf.Sin(a) * radius, (action == TideRules.Wave ? .1f : 1.2f) + t * 2 - t * t * 4, Mathf.Cos(a) * radius);
                drop.localScale = Vector3.one * (.04f * Mathf.Clamp01(1 - t / .65f));
            }
            materials[3].SetColor("_EmissionColor", new Color(.06f, .7f, .8f) * (action == TideRules.Wave || action == TideRules.Shot ? .3f + charge * 3 : .18f));
            if (action == TideRules.Bite)
            {
                torso.localRotation = Quaternion.Euler(-charge * 7, 90 * Mathf.Clamp01(age / (windup * .6f)), 0);
                torso.localPosition += new Vector3(0, -charge * .08f, release >= 0 ? pulse * .28f : -charge * .08f);
                bones["jaw"].localRotation = Quaternion.Euler(0, 0, release < 0 ? charge * 28 : -2);
                if (release < 0)
                { warning.enabled = true; warning.positionCount = 5; float w = TideRules.BiteHalfWidth, r = TideRules.BiteReach; warning.SetPositions(new[] {new Vector3(-w,.035f,0),new Vector3(-w,.035f,r),new Vector3(w,.035f,r),new Vector3(w,.035f,0),new Vector3(-w,.035f,0)}); }
                if (release >= 0 && release < .25f)
                { effect.enabled = true; effect.positionCount = 3; effect.SetPositions(new[] {new Vector3(0,1.25f,.6f),new Vector3(.04f,1.22f,TideRules.BiteReach * .6f),new Vector3(0,1.18f,TideRules.BiteReach)}); }
            }
            else if (action == TideRules.Sweep)
            {
                torso.localRotation = Quaternion.Euler(0, release < 0 ? -30 * charge : -30 - 120 * Mathf.Clamp01(release / .35f), 0);
                if (release < 0) Ring(warning, TideRules.SweepRadius, .035f, -60, 60);
                if (release >= 0 && release < .4f) Ring(effect, TideRules.SweepRadius, .75f, -60, 60);
            }
            else if (action == TideRules.Wave)
            {
                torso.localPosition += Vector3.down * (release < 0 ? charge * .18f : 0);
                bones["crown"].localScale = new Vector3(1 + charge * .2f, 1, 1);
                if (release < 0) Ring(warning, TideRules.WaveRadius, .04f, 0, 360);
                if (release >= 0 && release < .6f) Ring(effect, Mathf.Lerp(.5f, TideRules.WaveRadius, Mathf.Clamp01(release / .4f)), .1f, 0, 360);
            }
            else if (action == TideRules.Shot)
            {
                // Rears back while a shell gathers over the crown, pitches forward on each throw, settles after the last.
                float strike = TideRules.Strike(action), last = TideRules.Release(TideRules.ShotCount - 1), pitch;
                if (release < 0) pitch = -10 * charge;
                else if (age >= last + strike) pitch = 8 * (1 - Mathf.Clamp01((age - last - strike) / (TideRules.Duration(action) - last - strike)));
                else
                {
                    int thrown = Mathf.Min(TideRules.ShotCount - 1, (int)(release / TideRules.ShotInterval));
                    float since = age - TideRules.Release(thrown);
                    pitch = since < strike ? Mathf.Lerp(-10, 8, since / strike) : Mathf.Lerp(8, -10, (since - strike) / (TideRules.ShotInterval - strike));
                }
                torso.localRotation = Quaternion.Euler(pitch, 0, 0);
                bones["crown"].localScale = new Vector3(1 + Mathf.Clamp01(-pitch / 10) * .15f, 1, 1);
            }
            else if (action == TideRules.Summon && !flight) Ring(effect, .9f * rise, .045f, 0, 360);
            if (dying > 0)
            { bones["crown"].localScale = new Vector3(1 - dying * .7f, 1, 1); materials[3].SetColor("_EmissionColor", Color.black); materials[5].SetColor("_EmissionColor", new Color(1,.5f,.08f) * (1 - dying)); }
        }
        // since = seconds after the first shell's release (NaN = no volley). Shell k is released ShotInterval after
        // shell k-1 and gathers above the crown until then; apex 0 marks a skipped shell.
        internal void Volley(float since, Vector3 from, Vector3[] to, float[] apex)
        {
            int count = to == null || apex == null ? 0 : Math.Min(to.Length, apex.Length);
            bool any = false;
            for (int k = 0; k < shells.Length; k++)
            {
                float local = since - k * TideRules.ShotInterval, gather = k == 0 ? TideRules.Windup(TideRules.Shot) : TideRules.ShotInterval;
                bool live = k < count && apex[k] > 0;
                float flight = live ? TideSummons.Flight(from, to[k]) : 0;
                bool shown = live && local >= -gather && local < flight + TideRules.ShotSplash;
                // Idle sets are only touched on the frame the volley ends, not every frame.
                if (shown || shellsShown) Draw(shells[k], shown ? local : float.NaN, gather, from, live ? to[k] : from, flight, live ? apex[k] : 0);
                any |= shown;
            }
            shellsShown = any;
        }
        // since = seconds after this shell's release: negative while it gathers above the crown, NaN = hidden.
        private static void Draw(ShellFx fx, float since, float gather, Vector3 from, Vector3 to, float flight, float apex)
        {
            bool gathering = since >= -gather && since < 0, flying = since >= 0 && since < flight;
            bool bursting = since >= flight && since < flight + TideRules.ShotSplash;
            fx.orb.gameObject.SetActive(gathering || flying);
            fx.trail.enabled = flying; fx.mark.enabled = fx.timer.enabled = gathering || flying; fx.burst.enabled = bursting;
            if (gathering || flying)
            {
                fx.orb.position = flying ? TideSummons.Arc(from, to, since / flight, apex) : from;
                fx.orb.localScale = Vector3.one * (.36f * Mathf.Clamp01((since + gather) / gather));
                // The inner ring reaches the drawn edge exactly when the shell lands.
                WorldRing(fx.mark, to, TideRules.ShotRing);
                WorldRing(fx.timer, to, TideRules.ShotRing * Mathf.Clamp01((since + gather) / (gather + flight)));
            }
            if (flying)
            {
                float phase = since / flight;
                fx.trail.positionCount = 8;
                for (int i = 0; i < 8; i++) fx.trail.SetPosition(i, TideSummons.Arc(from, to, Mathf.Max(0, phase - i * .03f), apex));
            }
            float t = since - flight;
            if (bursting) WorldRing(fx.burst, to, Mathf.Lerp(.3f, TideRules.ShotRing, Mathf.Clamp01(t / .35f)));
            for (int i = 0; i < fx.spray.Length; i++)
            {
                Transform drop = fx.spray[i]; drop.gameObject.SetActive(bursting);
                if (!bursting) continue;
                float a = i * 2.39996f, radius = .2f + t * 4.2f;
                drop.position = to + new Vector3(Mathf.Sin(a) * radius, .1f + t * 3 - t * t * 5, Mathf.Cos(a) * radius);
                drop.localScale = Vector3.one * (.06f * Mathf.Clamp01(1 - t / TideRules.ShotSplash));
            }
        }
        private static void WorldRing(LineRenderer line, Vector3 center, float radius)
        {
            line.positionCount = 49;
            for (int i = 0; i < 49; i++) { float a = i / 48f * Mathf.PI * 2; line.SetPosition(i, center + new Vector3(Mathf.Sin(a) * radius, .06f, Mathf.Cos(a) * radius)); }
        }
        private void OnDestroy()
        {
            foreach (Texture2D texture in textures) if (texture != null) Destroy(texture);
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
            foreach (Material material in materials) if (material != null) Destroy(material);
            if (glow != null) Destroy(glow); if (water != null) Destroy(water);
        }
    }
}
