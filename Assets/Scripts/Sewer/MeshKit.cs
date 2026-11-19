using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>
    /// Procedural meshes for round sewer tunnels. UVs are in meters (materials set tiling),
    /// every face is generated to face the inside of the tunnel/room.
    /// </summary>
    public static class MeshKit
    {
        /// <summary>Every mesh created at runtime (so the editor bake can save them as assets).</summary>
        public static readonly List<Mesh> Created = new();

        public struct Seg
        {
            public Vector2 A, B, NA, NB;  // profile points (u = side, v = up) and normals
            public int Sub;               // submesh / material index
            public bool Wall;             // can be cut for door openings
        }

        // ------------------------------------------------------------ profiles
        /// <summary>Round sewer pipe: brick vault, two concrete walkways, a water channel in the middle.</summary>
        public static List<Seg> SewerProfile(float radius, float centerV, float channelHalf, float channelDepth, int arcSegs)
        {
            var p = new List<Seg>();
            float w0 = Mathf.Sqrt(radius * radius - centerV * centerV); // half width at floor level (v = 0)
            Vector2 c = new Vector2(0, centerV);
            float aStart = Mathf.Atan2(-centerV, -w0), aEnd = Mathf.Atan2(-centerV, w0);
            if (aStart < 0) aStart += 2 * Mathf.PI; // ~ 208°
            // arc from inner floor edge over the top to the outer floor edge (decreasing angle)
            for (int i = 0; i < arcSegs; i++)
            {
                float t0 = (float)i / arcSegs, t1 = (float)(i + 1) / arcSegs;
                float a0 = Mathf.Lerp(aStart, aEnd, t0), a1 = Mathf.Lerp(aStart, aEnd, t1);
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                p.Add(new Seg { A = c + d0 * radius, B = c + d1 * radius, NA = -d0, NB = -d1, Sub = 0, Wall = true });
            }
            Vector2 up = Vector2.up;
            p.Add(new Seg { A = new Vector2(w0, 0), B = new Vector2(channelHalf, 0), NA = up, NB = up, Sub = 1 });
            p.Add(new Seg { A = new Vector2(channelHalf, 0), B = new Vector2(channelHalf, -channelDepth), NA = Vector2.left, NB = Vector2.left, Sub = 2 });
            p.Add(new Seg { A = new Vector2(channelHalf, -channelDepth), B = new Vector2(-channelHalf, -channelDepth), NA = up, NB = up, Sub = 2 });
            p.Add(new Seg { A = new Vector2(-channelHalf, -channelDepth), B = new Vector2(-channelHalf, 0), NA = Vector2.right, NB = Vector2.right, Sub = 2 });
            p.Add(new Seg { A = new Vector2(-channelHalf, 0), B = new Vector2(-w0, 0), NA = up, NB = up, Sub = 1 });
            return p;
        }

        /// <summary>Round passage with a flat floor (doors / connectors / landing tunnels).</summary>
        public static List<Seg> PassageProfile(float radius, float centerV, int arcSegs)
        {
            var p = new List<Seg>();
            float w0 = Mathf.Sqrt(radius * radius - centerV * centerV);
            Vector2 c = new Vector2(0, centerV);
            float aStart = Mathf.Atan2(-centerV, -w0) + 2 * Mathf.PI, aEnd = Mathf.Atan2(-centerV, w0);
            for (int i = 0; i < arcSegs; i++)
            {
                float a0 = Mathf.Lerp(aStart, aEnd, (float)i / arcSegs), a1 = Mathf.Lerp(aStart, aEnd, (float)(i + 1) / arcSegs);
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                p.Add(new Seg { A = c + d0 * radius, B = c + d1 * radius, NA = -d0, NB = -d1, Sub = 0, Wall = true });
            }
            p.Add(new Seg { A = new Vector2(w0, 0), B = new Vector2(-w0, 0), NA = Vector2.up, NB = Vector2.up, Sub = 1 });
            return p;
        }

        /// <summary>Small closed circle profile (outward normals) for pipes along walls.</summary>
        public static List<Seg> PipeProfile(Vector2 center, float r, int segs)
        {
            var p = new List<Seg>();
            for (int i = 0; i < segs; i++)
            {
                float a0 = 2 * Mathf.PI * i / segs, a1 = 2 * Mathf.PI * (i + 1) / segs;
                Vector2 d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)), d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                p.Add(new Seg { A = center + d0 * r, B = center + d1 * r, NA = d0, NB = d1, Sub = 0 });
            }
            return p;
        }

        // ------------------------------------------------------------ sweeps
        public delegate bool HoleTest(float along, Vector2 segMid);

        class Builder
        {
            public readonly List<Vector3> V = new();
            public readonly List<Vector3> N = new();
            public readonly List<Vector2> UV = new();
            public readonly List<int>[] T;
            public Builder(int subs) { T = new List<int>[subs]; for (int i = 0; i < subs; i++) T[i] = new List<int>(); }

            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd,
                             Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, int sub)
            {
                int i = V.Count;
                V.Add(a); V.Add(b); V.Add(c); V.Add(d);
                N.Add(na); N.Add(nb); N.Add(nc); N.Add(nd);
                UV.Add(ua); UV.Add(ub); UV.Add(uc); UV.Add(ud);
                Vector3 fn = Vector3.Cross(b - a, c - a);
                Vector3 want = na + nb + nc + nd;
                if (Vector3.Dot(fn, want) >= 0) { T[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 }); }
                else { T[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 }); }
            }

            public bool Double;

            public Mesh Build(string name)
            {
                if (Double)
                {
                    int n0 = V.Count;
                    for (int i = 0; i < n0; i++) { V.Add(V[i]); N.Add(-N[i]); UV.Add(UV[i]); }
                    foreach (var t in T)
                    {
                        int c = t.Count;
                        for (int i = 0; i < c; i += 3) { t.Add(t[i] + n0); t.Add(t[i + 2] + n0); t.Add(t[i + 1] + n0); }
                    }
                }
                var m = new Mesh { name = name };
                if (V.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV);
                m.subMeshCount = T.Length;
                for (int i = 0; i < T.Length; i++) m.SetTriangles(T[i], i);
                m.RecalculateBounds();
                m.RecalculateTangents();
                Created.Add(m);
                return m;
            }
        }

        /// <summary>Sweep a profile along a ring arc (center at origin). u = radial offset (+ outward), v = up.</summary>
        public static Mesh SweepRing(string name, List<Seg> prof, float R, float y, float a0Deg, float a1Deg, float stepLen,
                                     int subs, HoleTest hole = null, bool doubleSided = false)
        {
            var b = new Builder(subs) { Double = doubleSided };
            float arc = (a1Deg - a0Deg) * Mathf.Deg2Rad * R;
            int steps = Mathf.Max(1, Mathf.CeilToInt(arc / stepLen));
            var pv = ProfileV(prof);
            for (int k = 0; k < steps; k++)
            {
                float t0 = a0Deg + (a1Deg - a0Deg) * k / steps, t1 = a0Deg + (a1Deg - a0Deg) * (k + 1) / steps;
                Vector3 o0 = Geo.Out(t0), o1 = Geo.Out(t1);
                float s0 = t0 * Mathf.Deg2Rad * R, s1 = t1 * Mathf.Deg2Rad * R;
                float mid = (t0 + t1) / 2;
                for (int i = 0; i < prof.Count; i++)
                {
                    var sg = prof[i];
                    if (hole != null && sg.Wall && hole(mid, (sg.A + sg.B) * 0.5f)) continue;
                    Vector3 P(Vector3 o, Vector2 q) => o * (R + q.x) + Vector3.up * (y + q.y);
                    Vector3 Nn(Vector3 o, Vector2 n) => (o * n.x + Vector3.up * n.y).normalized;
                    b.Quad(P(o0, sg.A), P(o1, sg.A), P(o1, sg.B), P(o0, sg.B),
                           Nn(o0, sg.NA), Nn(o1, sg.NA), Nn(o1, sg.NB), Nn(o0, sg.NB),
                           new Vector2(s0, pv[i]), new Vector2(s1, pv[i]), new Vector2(s1, pv[i + 1]), new Vector2(s0, pv[i + 1]), sg.Sub);
                }
            }
            return b.Build(name);
        }

        /// <summary>Sweep a profile along a straight line. u axis = Cross(up, dir).</summary>
        public static Mesh SweepStraight(string name, List<Seg> prof, Vector3 start, Vector3 dir, float length, float stepLen, int subs,
                                         HoleTest hole = null, bool doubleSided = true)
        {
            var b = new Builder(subs) { Double = doubleSided };
            dir = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / stepLen));
            var pv = ProfileV(prof);
            for (int k = 0; k < steps; k++)
            {
                float s0 = length * k / steps, s1 = length * (k + 1) / steps;
                Vector3 c0 = start + dir * s0, c1 = start + dir * s1;
                for (int i = 0; i < prof.Count; i++)
                {
                    var sg = prof[i];
                    if (hole != null && sg.Wall && hole((s0 + s1) / 2, (sg.A + sg.B) * 0.5f)) continue;
                    Vector3 P(Vector3 c, Vector2 q) => c + side * q.x + Vector3.up * q.y;
                    Vector3 Nn(Vector2 n) => (side * n.x + Vector3.up * n.y).normalized;
                    b.Quad(P(c0, sg.A), P(c1, sg.A), P(c1, sg.B), P(c0, sg.B),
                           Nn(sg.NA), Nn(sg.NA), Nn(sg.NB), Nn(sg.NB),
                           new Vector2(s0, pv[i]), new Vector2(s1, pv[i]), new Vector2(s1, pv[i + 1]), new Vector2(s0, pv[i + 1]), sg.Sub);
                }
            }
            return b.Build(name);
        }

        static float[] ProfileV(List<Seg> prof)
        {
            var v = new float[prof.Count + 1];
            for (int i = 0; i < prof.Count; i++) v[i + 1] = v[i] + Vector2.Distance(prof[i].A, prof[i].B);
            return v;
        }

        /// <summary>Vertical cylinder wall (normals inward). Gaps: (angleDeg, halfWidth m, gapTop m above y0).</summary>
        public static Mesh CylinderWall(string name, Vector3 center, float r, float y0, float height, float segLen,
                                        List<(float angle, float halfWidth, float top)> gaps, bool inward = true, float rowH = 1.5f)
        {
            var b = new Builder(1) { Double = true };
            center = new Vector3(center.x, 0f, center.z); // height comes from y0 only
            int n = Mathf.Max(12, Mathf.CeilToInt(2 * Mathf.PI * r / segLen));
            int rows = Mathf.Max(1, Mathf.CeilToInt(height / rowH));
            for (int k = 0; k < n; k++)
            {
                float a0 = 360f * k / n, a1 = 360f * (k + 1) / n, am = (a0 + a1) / 2;
                Vector3 o0 = Geo.Out(a0), o1 = Geo.Out(a1);
                for (int j = 0; j < rows; j++)
                {
                    float h0 = height * j / rows, h1 = height * (j + 1) / rows;
                    bool skip = false;
                    if (gaps != null)
                        foreach (var g in gaps)
                            if (Mathf.Abs(Mathf.DeltaAngle(am, g.angle)) * Mathf.Deg2Rad * r < g.halfWidth && (h0 + h1) / 2 < g.top) skip = true;
                    if (skip) continue;
                    Vector3 n0 = inward ? -o0 : o0, n1 = inward ? -o1 : o1;
                    float s0 = a0 * Mathf.Deg2Rad * r, s1 = a1 * Mathf.Deg2Rad * r;
                    b.Quad(center + o0 * r + Vector3.up * (y0 + h0), center + o1 * r + Vector3.up * (y0 + h0),
                           center + o1 * r + Vector3.up * (y0 + h1), center + o0 * r + Vector3.up * (y0 + h1),
                           n0, n1, n1, n0, new Vector2(s0, h0), new Vector2(s1, h0), new Vector2(s1, h1), new Vector2(s0, h1), 0);
                }
            }
            return b.Build(name);
        }

        /// <summary>Flat disk made of grid cells (top or bottom facing) with an optional circular hole.</summary>
        public static Mesh Disk(string name, Vector3 center, float r, float cell, bool faceUp, Vector3? holeCenter = null, float holeR = 0)
            => Disk(name, center, r, cell, faceUp, holeCenter.HasValue ? new List<(Vector3, float)> { (holeCenter.Value, holeR) } : null);

        public static Mesh Disk(string name, Vector3 center, float r, float cell, bool faceUp, List<(Vector3 c, float r)> holes)
        {
            var b = new Builder(1);
            int n = Mathf.CeilToInt(r / cell);
            Vector3 nrm = faceUp ? Vector3.up : Vector3.down;
            for (int i = -n; i < n; i++)
                for (int j = -n; j < n; j++)
                {
                    Vector3 c = center + new Vector3((i + 0.5f) * cell, 0, (j + 0.5f) * cell);
                    if (new Vector2(c.x - center.x, c.z - center.z).magnitude > r) continue;
                    bool inHole = false;
                    if (holes != null)
                        foreach (var h in holes)
                            if (new Vector2(c.x - h.c.x, c.z - h.c.z).magnitude < h.r) inHole = true;
                    if (inHole) continue;
                    Vector3 p0 = center + new Vector3(i * cell, 0, j * cell);
                    Vector3 p1 = p0 + new Vector3(cell, 0, 0), p2 = p0 + new Vector3(cell, 0, cell), p3 = p0 + new Vector3(0, 0, cell);
                    b.Quad(p0, p1, p2, p3, nrm, nrm, nrm, nrm,
                        new Vector2(p0.x, p0.z), new Vector2(p1.x, p1.z), new Vector2(p2.x, p2.z), new Vector2(p3.x, p3.z), 0);
                }
            return b.Build(name);
        }

        /// <summary>Flat ring strip (water) along a ring arc.</summary>
        public static Mesh RingStrip(string name, float R, float y, float halfWidth, float a0, float a1, float stepLen)
        {
            var b = new Builder(1);
            int steps = Mathf.Max(1, Mathf.CeilToInt((a1 - a0) * Mathf.Deg2Rad * R / stepLen));
            for (int k = 0; k < steps; k++)
            {
                float t0 = a0 + (a1 - a0) * k / steps, t1 = a0 + (a1 - a0) * (k + 1) / steps;
                Vector3 o0 = Geo.Out(t0), o1 = Geo.Out(t1);
                float s0 = t0 * Mathf.Deg2Rad * R, s1 = t1 * Mathf.Deg2Rad * R;
                Vector3 up = Vector3.up;
                b.Quad(o0 * (R - halfWidth) + up * y, o1 * (R - halfWidth) + up * y, o1 * (R + halfWidth) + up * y, o0 * (R + halfWidth) + up * y,
                    up, up, up, up, new Vector2(s0, 0), new Vector2(s1, 0), new Vector2(s1, 2 * halfWidth), new Vector2(s0, 2 * halfWidth), 0);
            }
            return b.Build(name);
        }

        /// <summary>Axis-aligned box mesh of the given size with UVs in meters (no texture stretching).</summary>
        public static Mesh Box(Vector3 size)
        {
            string key = $"Box_{size.x:0.##}_{size.y:0.##}_{size.z:0.##}";
            if (BoxCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var b = new Builder(1);
            Vector3 h = size / 2;
            void F(Vector3 n, Vector3 u, Vector3 v, float su, float sv)
            {
                Vector3 c = Vector3.Scale(n, h);
                Vector3 a = c - u * su / 2 - v * sv / 2, bb = c + u * su / 2 - v * sv / 2, cc = c + u * su / 2 + v * sv / 2, d = c - u * su / 2 + v * sv / 2;
                b.Quad(a, bb, cc, d, n, n, n, n, new Vector2(0, 0), new Vector2(su, 0), new Vector2(su, sv), new Vector2(0, sv), 0);
            }
            F(Vector3.up, Vector3.right, Vector3.forward, size.x, size.z);
            F(Vector3.down, Vector3.right, Vector3.forward, size.x, size.z);
            F(Vector3.right, Vector3.forward, Vector3.up, size.z, size.y);
            F(Vector3.left, Vector3.forward, Vector3.up, size.z, size.y);
            F(Vector3.forward, Vector3.right, Vector3.up, size.x, size.y);
            F(Vector3.back, Vector3.right, Vector3.up, size.x, size.y);
            var m = b.Build(key);
            BoxCache[key] = m;
            return m;
        }
        static readonly Dictionary<string, Mesh> BoxCache = new();

        // ------------------------------------------------------------ objects
        public static GameObject MeshObject(Transform parent, string name, Mesh mesh, Material[] mats, bool collider)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (collider) g.AddComponent<MeshCollider>().sharedMesh = mesh;
            return g;
        }
    }
}
