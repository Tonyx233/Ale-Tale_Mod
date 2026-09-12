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
        [Serializable] public sealed class Piece { public string name, bone; public float[] p, s, r, color; }
        private readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
        private Mesh mesh;
        private Material material;
        private Vector3 previous;
        private float speed, phase;
        private bool started;

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
            using (var reader = new System.IO.StreamReader(stream)) data = JsonUtility.FromJson<Definition>(reader.ReadToEnd());
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) throw new InvalidOperationException("Horse shader unavailable");
            material = new Material(shader);
            mesh = FacetedMesh();
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
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                Color color = new Color(p.color[0], p.color[1], p.color[2]);
                block.SetColor("_Color", color); block.SetColor("_BaseColor", color); renderer.SetPropertyBlock(block);
            }
        }
        // Eight-sided ellipsoid with flat triangle normals. Shared by all body pieces.
        private static Mesh FacetedMesh()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            for (int ring = 0; ring < 6; ring++)
                for (int side = 0; side < 10; side++)
                {
                    Vector3 a = Point(ring, side), b = Point(ring + 1, side);
                    Vector3 c = Point(ring + 1, side + 1), d = Point(ring, side + 1);
                    AddTriangle(vertices, indices, a, c, b); AddTriangle(vertices, indices, a, d, c);
                }
            Mesh m = new Mesh(); m.name = "Tony Original Horse Facets";
            m.SetVertices(vertices); m.SetTriangles(indices, 0); m.RecalculateNormals(); m.RecalculateBounds(); return m;
        }
        private static Vector3 Point(int ring, int side)
        {
            float latitude = Mathf.PI * ring / 6, longitude = 2 * Mathf.PI * side / 10;
            return new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude)) * .5f;
        }
        private static void AddTriangle(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i+1); t.Add(i+2); }
        public void Animate(float dt)
        {
            Vector3 now = transform.position;
            float distance = started ? Vector3.ProjectOnPlane(now - previous, Vector3.up).magnitude : 0;
            previous = now; started = true;
            float measured = dt > .0001f && distance < 5 ? distance / dt : 0;
            speed = Mathf.Lerp(speed, measured, Mathf.Clamp01(dt * 8));
            phase += dt * Mathf.Lerp(.5f, 2.8f, Mathf.Clamp01(speed / 8)) * Mathf.PI * 2;
            float weight = Mathf.Clamp01(speed / .8f), run = Mathf.Clamp01((speed - 4) / 3);
            for (int i = 0; i < 4; i++)
            {
                float offset = HorseGait.Offset(i, run);
                double angle = phase + offset;
                bones["leg"+i].localRotation = Quaternion.Euler(HorseGait.Upper(angle, weight, run), 0, 0);
                bones["shin"+i].localRotation = Quaternion.Euler(HorseGait.Lower(angle, weight, run), 0, 0);
            }
            bones["body"].localPosition = new Vector3(0, (float)Math.Sin(phase * 2) * .035f * weight, 0);
            bones["neck"].localRotation = Quaternion.Euler((float)Math.Sin(phase) * (2 + 4 * weight), 0, 0);
            bones["tail"].localRotation = Quaternion.Euler(8 * weight, (float)Math.Sin(phase * .7) * 12, 0);
            bones["earL"].localRotation = Quaternion.Euler(0, 0, -10 + (float)Math.Sin(phase * .3) * 6);
            bones["earR"].localRotation = Quaternion.Euler(0, 0, 10 - (float)Math.Sin(phase * .3) * 6);
        }
        private void OnDestroy() { if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
    }
}
