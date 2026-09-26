using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TonyMods
{
    public sealed class TideModel : MonoBehaviour
    {
        [Serializable] private sealed class Bone { public string name, parent; public float[] p; }
        [Serializable] private sealed class Part { public string name, bone; public int material; public float[] vertices; public int[] triangles; }
        [Serializable] private sealed class Definition { public Bone[] bones; public Part[] parts; public float[][] materials; }
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> materials = new List<Material>();
        private Transform orb;
        private LineRenderer effect, warning;
        private Material glow;
        private readonly List<Transform> droplets = new List<Transform>();
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
                material.SetFloat("_Metallic", i == 1 ? .08f : .6f); material.SetFloat("_Smoothness", .3f);
                if (i == 3 || i == 5) { material.EnableKeyword("_EMISSION"); material.SetColor("_EmissionColor", color * (i == 3 ? .35f : 1.4f)); }
                materials.Add(material);
            }
            foreach (Bone bone in data.bones)
            {
                Transform node = new GameObject(bone.name).transform;
                node.SetParent(String.IsNullOrEmpty(bone.parent) ? transform : bones[bone.parent], false);
                node.localPosition = new Vector3(bone.p[0], bone.p[1], bone.p[2]); bones.Add(bone.name, node);
            }
            // One flat-shaded mesh per bone and material: same look as 64 separate parts, far fewer renderers.
            var groups = new Dictionary<string, List<Vector3>>(); var owners = new Dictionary<string, Part>();
            foreach (Part part in data.parts)
            {
                string key = part.bone + "|" + part.material; List<Vector3> corners;
                if (!groups.TryGetValue(key, out corners)) { groups.Add(key, corners = new List<Vector3>()); owners.Add(key, part); }
                foreach (int index in part.triangles)
                { int at = index * 3; corners.Add(new Vector3(part.vertices[at], part.vertices[at + 1], part.vertices[at + 2])); }
            }
            foreach (var group in groups)
            {
                Part part = owners[group.Key];
                var go = new GameObject("Tidefork " + group.Key); go.transform.SetParent(bones[part.bone], false);
                var triangles = new int[group.Value.Count];
                for (int i = 0; i < triangles.Length; i++) triangles[i] = i;
                var mesh = new Mesh { name = go.name, vertices = group.Value.ToArray(), triangles = triangles };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterial = materials[part.material];
            }
            GameObject ball = GameObject.CreatePrimitive(PrimitiveType.Sphere); ball.name = "Tidefork thrown seal";
            DestroyImmediate(ball.GetComponent<Collider>()); ball.GetComponent<MeshRenderer>().sharedMaterial = materials[1];
            orb = ball.transform; orb.SetParent(transform, false); orb.localScale = Vector3.one * .27f;
            Shader fxShader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? shader;
            glow = new Material(fxShader); glow.SetColor("_Color", new Color(.15f, .9f, 1)); glow.SetColor("_BaseColor", new Color(.15f, .9f, 1));
            effect = MakeLine("Tidefork attack water", .055f); warning = MakeLine("Tidefork telegraph", .022f);
            for (int i = 0; i < 12; i++)
            {
                GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere); drop.name = "Tidefork water droplet " + i;
                DestroyImmediate(drop.GetComponent<Collider>()); drop.GetComponent<MeshRenderer>().sharedMaterial = materials[3];
                drop.transform.SetParent(transform, false); drop.transform.localScale = Vector3.one * .035f;
                droplets.Add(drop.transform); drop.SetActive(false);
            }
        }
        private LineRenderer MakeLine(string name, float width)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = glow; line.useWorldSpace = false;
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
                float radius = action == TideRules.Wave ? Mathf.Min(4, .4f + t * 9) : .25f + t * 1.4f;
                Vector3 center = action == TideRules.Bite ? Vector3.forward * 1.65f : Vector3.zero;
                drop.localPosition = center + new Vector3(Mathf.Sin(a) * radius, (action == TideRules.Wave ? .1f : 1.2f) + t * 2 - t * t * 4, Mathf.Cos(a) * radius);
                drop.localScale = Vector3.one * (.04f * Mathf.Clamp01(1 - t / .65f));
            }
            materials[3].SetColor("_EmissionColor", new Color(.06f, .7f, .8f) * (action == TideRules.Wave ? .3f + charge * 3 : .18f));
            if (action == TideRules.Bite)
            {
                torso.localRotation = Quaternion.Euler(-charge * 7, 90 * Mathf.Clamp01(age / .3f), 0);
                torso.localPosition += new Vector3(0, -charge * .08f, release >= 0 ? pulse * .28f : -charge * .08f);
                bones["jaw"].localRotation = Quaternion.Euler(0, 0, release < 0 ? charge * 28 : -2);
                if (release < 0)
                { warning.enabled = true; warning.positionCount = 5; warning.SetPositions(new[] {new Vector3(-.65f,.035f,0),new Vector3(-.65f,.035f,2.3f),new Vector3(.65f,.035f,2.3f),new Vector3(.65f,.035f,0),new Vector3(-.65f,.035f,0)}); }
                if (release >= 0 && release < .25f)
                { effect.enabled = true; effect.positionCount = 3; effect.SetPositions(new[] {new Vector3(0,1.25f,.6f),new Vector3(.04f,1.22f,1.5f),new Vector3(0,1.18f,2.3f)}); }
            }
            else if (action == TideRules.Sweep)
            {
                torso.localRotation = Quaternion.Euler(0, release < 0 ? -30 * charge : -30 - 120 * Mathf.Clamp01(release / .35f), 0);
                if (release < 0) Ring(warning, 2.6f, .035f, -60, 60);
                if (release >= 0 && release < .4f) Ring(effect, 2.6f, .75f, -60, 60);
            }
            else if (action == TideRules.Wave)
            {
                torso.localPosition += Vector3.down * (release < 0 ? charge * .18f : 0);
                bones["crown"].localScale = new Vector3(1 + charge * .2f, 1, 1);
                if (release < 0) Ring(warning, 4, .04f, 0, 360);
                if (release >= 0 && release < .6f) Ring(effect, Mathf.Lerp(.5f, 4, Mathf.Clamp01(release / .4f)), .1f, 0, 360);
            }
            else if (action == TideRules.Summon && !flight) Ring(effect, .9f * rise, .045f, 0, 360);
            if (dying > 0)
            { bones["crown"].localScale = new Vector3(1 - dying * .7f, 1, 1); materials[3].SetColor("_EmissionColor", Color.black); materials[5].SetColor("_EmissionColor", new Color(1,.5f,.08f) * (1 - dying)); }
        }
        private void OnDestroy()
        { foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh); foreach (Material material in materials) if (material != null) Destroy(material); if (glow != null) Destroy(glow); }
    }
}
