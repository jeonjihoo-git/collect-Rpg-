using System.Collections.Generic;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>바닥 장판/예고/베기 궤적용 평면 메시(XZ 평면, +Z 가 정면).</summary>
    public static class ProcMesh
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = key;
            Cache[key] = m;
            return m;
        }

        /// <summary>원판(반지름 1). 스케일로 크기 조절.</summary>
        public static Mesh Disc(int seg = 48) => Cached("disc" + seg, () => Sector(360f, 1f, 0f, seg));

        /// <summary>고리(외곽 1, 내곽 inner).</summary>
        public static Mesh Ring(float inner, int seg = 64) => Cached($"ring{inner:0.00}_{seg}", () => Sector(360f, 1f, inner, seg));

        /// <summary>부채꼴(반지름 1, 각도 arcDeg, 내곽 inner).</summary>
        public static Mesh Cone(float arcDeg, float inner = 0f) => Cached($"cone{arcDeg:0}_{inner:0.00}", () => Sector(arcDeg, 1f, inner, Mathf.Max(8, (int)(arcDeg / 6f))));

        /// <summary>직사각형(폭 1, 길이 1, 시작점이 원점, +Z 로 뻗음).</summary>
        public static Mesh Rect() => Cached("rect", () =>
        {
            var m = new Mesh();
            m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0.5f, 0, 1), new Vector3(-0.5f, 0, 1) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        /// <summary>초승달 베기 궤적(가운데 두껍고 양끝 가는 호).</summary>
        public static Mesh Crescent(float arcDeg) => Cached($"crescent{arcDeg:0}", () =>
        {
            int seg = Mathf.Max(10, (int)(arcDeg / 5f));
            var verts = new List<Vector3>();
            var cols = new List<Color>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float u = i / (float)seg;
                float a = Mathf.Deg2Rad * (-arcDeg * 0.5f + arcDeg * u);
                float thick = Mathf.Sin(u * Mathf.PI) * 0.35f;
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                verts.Add(dir * (1f - thick));
                verts.Add(dir * 1f);
                float alpha = Mathf.Sin(u * Mathf.PI);
                cols.Add(new Color(1, 1, 1, 0f));
                cols.Add(new Color(1, 1, 1, alpha));
                if (i < seg)
                {
                    int k = i * 2;
                    tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                }
            }
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        });

        private static Mesh Sector(float arcDeg, float outer, float inner, int seg)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = Mathf.Deg2Rad * (-arcDeg * 0.5f + arcDeg * i / seg);
                var dir = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
                verts.Add(dir * inner);
                verts.Add(dir * outer);
                if (i < seg)
                {
                    int k = i * 2;
                    tris.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 });
                }
            }
            var cols = new List<Color>(verts.Count);
            for (int i = 0; i < verts.Count; i++) cols.Add(Color.white);
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetColors(cols);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static GameObject Flat(string name, Transform parent, Mesh mesh, Material mat, float y = 0.02f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0, y, 0);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, Quaternion? rot = null)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = rot ?? Quaternion.identity;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }
    }
}
