using System;
using System.Collections.Generic;
using UnityEngine;

namespace TonyMods
{
    // Original articulated low-poly mesh; no downloaded assets or runtime files.
    public sealed class HorseModel : MonoBehaviour
    {
        [Serializable] public sealed class Definition { public Bone[] bones; public Piece[] parts; }
        [Serializable] public sealed class Bone { public string name, parent; public float[] p; }
        [Serializable] public sealed class Piece { public string name, bone; public float[] p, s, r, color, vertices; public int[] triangles; }
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private readonly List<Mesh> meshes = new List<Mesh>();
        private Material material;
        private Vector3 previous;
        private float speed, phase, airborneBlend;
        private bool started;
        public float SaddleBob { get { return bones.ContainsKey("body") ? bones["body"].localPosition.y : 0; } }

        public static HorseModel Create(Transform parent)
        {
            GameObject go = new GameObject("Double Saddle Horse");
            go.transform.SetParent(parent, false);
            HorseModel model = go.AddComponent<HorseModel>();
            model.Build();
            return model;
        }
        private static Vector3 V(float[] v) { return new Vector3(v[0], v[1], v[2]); }
        private void Build()
        {
            Definition data;
            using (var stream = typeof(HorseModel).Assembly.GetManifestResourceStream("Tony.Horse.model.json"))
            using (var reader = new System.IO.StreamReader(stream)) data = Newtonsoft.Json.JsonConvert.DeserializeObject<Definition>(reader.ReadToEnd());
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Horse shader unavailable");
            material = new Material(shader);
            material.SetFloat("_Smoothness", .24f);
            foreach (Bone b in data.bones)
            {
                Transform node = new GameObject(b.name).transform;
                node.SetParent(String.IsNullOrEmpty(b.parent) ? transform : bones[b.parent], false);
                node.localPosition = V(b.p); bones.Add(b.name, node);
            }
            foreach (Piece p in data.parts)
            {
                GameObject go = new GameObject(p.name);
                go.transform.SetParent(bones[p.bone], false);
                go.transform.localPosition = V(p.p); go.transform.localScale = V(p.s);
                go.transform.localRotation = Quaternion.Euler(V(p.r));
                Mesh mesh = AuthoredMesh(p); meshes.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                Color color = new Color(p.color[0], p.color[1], p.color[2]);
                block.SetColor("_Color", color); block.SetColor("_BaseColor", color); renderer.SetPropertyBlock(block);
            }
        }
        // Each part carries its own authored topology, shared with the preview.
        private static Mesh AuthoredMesh(Piece p)
        {
            if (p.vertices == null || p.vertices.Length % 3 != 0 || p.triangles == null)
                throw new InvalidOperationException("Invalid horse geometry: " + p.name);
            var vertices = new Vector3[p.vertices.Length / 3];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = new Vector3(p.vertices[i*3], p.vertices[i*3+1], p.vertices[i*3+2]);
            // Split corners for the reference's clean, faceted art direction.
            var corners = new Vector3[p.triangles.Length];
            var indices = new int[corners.Length];
            for (int i = 0; i < corners.Length; i++)
            { corners[i] = vertices[p.triangles[i]]; indices[i] = i; }
            Mesh result = new Mesh(); result.name = "Tony Horse " + p.name;
            result.vertices = corners; result.triangles = indices;
            result.RecalculateNormals(); result.RecalculateBounds(); return result;
        }
        public void Animate(float dt, bool airborne)
        {
            Vector3 now = transform.position;
            float distance = started ? Vector3.ProjectOnPlane(now - previous, Vector3.up).magnitude : 0;
            previous = now; started = true;
            float measured = dt > .0001f && distance < 5 ? distance / dt : 0;
            speed = Mathf.Lerp(speed, measured, Mathf.Clamp01(dt * 8));
            phase += dt * Mathf.Lerp(.5f, 2.8f, Mathf.Clamp01(speed / 8)) * Mathf.PI * 2;
            float weight = Mathf.Clamp01(speed / .8f), run = Mathf.Clamp01((speed - 4) / 3);
            airborneBlend = Mathf.MoveTowards(airborneBlend, airborne ? 1f : 0f, dt * 8f);
            for (int i = 0; i < 4; i++)
            {
                float offset = HorseGait.Offset(i, run);
                double angle = phase + offset;
                bones["leg"+i].localRotation = Quaternion.Euler(Mathf.Lerp(HorseGait.Upper(angle, weight, run), -25f, airborneBlend), 0, 0);
                bones["shin"+i].localRotation = Quaternion.Euler(Mathf.Lerp(HorseGait.Lower(angle, weight, run), 65f, airborneBlend), 0, 0);
            }
            bones["body"].localPosition = new Vector3(0, (float)Math.Sin(phase * 2) * .035f * weight * (1f - airborneBlend), 0);
            bones["neck"].localRotation = Quaternion.Euler((float)Math.Sin(phase) * (2 + 4 * weight), 0, 0);
            bones["tail"].localRotation = Quaternion.Euler(8 * weight, (float)Math.Sin(phase * .7) * 12, 0);
            bones["earL"].localRotation = Quaternion.Euler(0, 0, -10 + (float)Math.Sin(phase * .3) * 6);
            bones["earR"].localRotation = Quaternion.Euler(0, 0, 10 - (float)Math.Sin(phase * .3) * 6);
        }
        private void OnDestroy() { foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
    }
}
