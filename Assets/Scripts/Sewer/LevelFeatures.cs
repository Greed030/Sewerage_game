using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    public enum FeatureKind { Fan, Grate, Divider, Climb }

    /// <summary>
    /// Tunnel features that split the rings into sections: fan bulkheads, grate bulkheads, split paths (a dividing wall),
    /// wall breaches with collapsed brick, plus extra ceiling pipes and cables.
    /// </summary>
    public partial class LevelBuilder
    {
        public static readonly (int ring, float angle, FeatureKind kind)[] Features =
        {
            (0, 20f, FeatureKind.Fan), (0, 65f, FeatureKind.Divider), (0, 115f, FeatureKind.Grate), (0, 155f, FeatureKind.Divider),
            (0, 200f, FeatureKind.Fan), (0, 245f, FeatureKind.Climb), (0, 295f, FeatureKind.Grate), (0, 335f, FeatureKind.Divider),
            (1, 45f, FeatureKind.Fan), (1, 120f, FeatureKind.Divider), (1, 150f, FeatureKind.Grate),
            (1, 240f, FeatureKind.Divider), (1, 270f, FeatureKind.Fan), (1, 300f, FeatureKind.Climb),
            (2, 45f, FeatureKind.Fan), (2, 135f, FeatureKind.Divider), (2, 225f, FeatureKind.Grate), (2, 315f, FeatureKind.Climb),
        };
        const float DividerHalf = 14f;

        public struct Breach { public int Ring; public float Angle, Side, Half, V0, V1; }
        public readonly List<Breach> Breaches = new();

        static float FeatureHalf(FeatureKind k) => k == FeatureKind.Divider ? DividerHalf + 1f : 2f;

        public static int RingIndex(RingDef d)
        {
            for (int i = 0; i < Rings.Length; i++) if (Mathf.Approximately(Rings[i].R, d.R)) return i;
            return -1;
        }

        /// <summary>True near a feature or a breach (keeps props, mobs and decals out of them).</summary>
        public bool NearFeature(int ring, float a, float meters)
        {
            float R = Rings[ring].R;
            foreach (var f in Features)
                if (f.ring == ring && Mathf.Abs(Mathf.DeltaAngle(a, f.angle)) * Mathf.Deg2Rad * R < meters + FeatureHalf(f.kind)) return true;
            foreach (var b in Breaches)
                if (b.Ring == ring && Mathf.Abs(Mathf.DeltaAngle(a, b.Angle)) * Mathf.Deg2Rad * R < meters * 0.5f + b.Half + 1.5f) return true;
            return false;
        }

        static bool NearDivider(int ring, float a, float meters)
        {
            foreach (var f in Features)
                if (f.ring == ring && f.kind == FeatureKind.Divider && Mathf.Abs(Mathf.DeltaAngle(a, f.angle)) * Mathf.Deg2Rad * Rings[ring].R < DividerHalf + meters) return true;
            return false;
        }

        void MakeBreaches()
        {
            Breaches.Clear();
            var br = new System.Random(7);
            int[] counts = { 12, 8, 4 };
            for (int i = 0; i < Rings.Length; i++)
            {
                int placed = 0, guard = 0;
                while (placed < counts[i] && guard++ < 500)
                {
                    float a = (float)br.NextDouble() * 360f;
                    if (NearOpening(i, a, 12f) || (i == 0 && NearSpawn(a, 9f))) continue;
                    Breaches.Add(new Breach
                    {
                        Ring = i, Angle = a, Side = br.NextDouble() < 0.5 ? 1 : -1,
                        Half = 1.3f + (float)br.NextDouble() * 1.1f, V0 = 0.9f, V1 = 3.0f + (float)br.NextDouble() * 1.0f
                    });
                    placed++;
                }
            }
        }

        // ---- brick grid of the cartoon brick texture (4 x 8 bricks per 3.5 m tile, odd rows offset by half a brick)
        public const float BrickTile = 3.5f, BrickW = BrickTile / 4f, BrickH = BrickTile / 8f;
        static readonly float VaultStart = Mathf.Atan2(-TunnelC, -Mathf.Sqrt(TunnelR * TunnelR - TunnelC * TunnelC)) + 2 * Mathf.PI;
        static readonly float VaultEnd = Mathf.Atan2(-TunnelC, Mathf.Sqrt(TunnelR * TunnelR - TunnelC * TunnelC));

        /// <summary>Profile arc length (texture v, meters) of a vault point.</summary>
        static float PvOf(Vector2 uv)
        {
            float th = Mathf.Atan2(uv.y - TunnelC, uv.x);
            if (th < VaultEnd - 1e-3f) th += 2 * Mathf.PI;
            return (VaultStart - th) * TunnelR;
        }
        /// <summary>Vault point (u, v) and angle for a profile arc length.</summary>
        static float ThetaOf(float pv) => VaultStart - pv / TunnelR;

        static (int col, int row) Cell(float s, float pv)
        {
            int row = Mathf.FloorToInt(pv / BrickH);
            float off = (row & 1) == 0 ? BrickW / 2 : 0;
            return (Mathf.FloorToInt((s - off) / BrickW), row);
        }
        static (float s, float pv) CellCenter(int col, int row)
        {
            float off = (row & 1) == 0 ? BrickW / 2 : 0;
            return ((col + 0.5f) * BrickW + off, (row + 0.5f) * BrickH);
        }

        /// <summary>Is this brick knocked out? Noisy ellipse, decided per whole brick so the hole edge follows the mortar.</summary>
        bool BrickGone(int ring, Breach b, int col, int row)
        {
            var (sc, pvc) = CellCenter(col, row);
            float R = Rings[ring].R;
            float z = Mathf.DeltaAngle(b.Angle, sc / R * Mathf.Rad2Deg) * Mathf.Deg2Rad * R;
            float th = ThetaOf(pvc);
            float u = Mathf.Cos(th) * TunnelR, v = TunnelC + Mathf.Sin(th) * TunnelR;
            if (Mathf.Sign(u) != b.Side) return false;
            float vc = (b.V0 + b.V1) / 2, rv = (b.V1 - b.V0) / 2;
            float nx = z / b.Half, ny = (v - vc) / rv;
            float ang = Mathf.Atan2(ny, nx), ph = b.Angle * 0.37f;
            float rho = 1f + 0.2f * Mathf.Sin(3 * ang + ph) + 0.1f * Mathf.Sin(5 * ang + ph * 2.3f);
            uint hsh = (uint)(col * 73856093) ^ (uint)(row * 19349663) ^ (uint)(ring * 83492791);
            hsh = (hsh ^ (hsh >> 13)) * 1274126177u;
            rho += ((hsh & 1023) / 1023f - 0.5f) * 0.35f;
            return nx * nx + ny * ny < rho * rho;
        }

        bool BreachHole(int ring, float a, Vector2 mid)
        {
            float R = Rings[ring].R;
            foreach (var b in Breaches)
            {
                if (b.Ring != ring || Mathf.Sign(mid.x) != b.Side) continue;
                if (Mathf.Abs(Mathf.DeltaAngle(a, b.Angle)) * Mathf.Deg2Rad * R > b.Half * 1.6f + 1f) continue;
                var (col, row) = Cell(a * Mathf.Deg2Rad * R, PvOf(mid));
                if (BrickGone(ring, b, col, row)) return true;
            }
            return false;
        }

        /// <summary>Pieces of a whole ring: coarse pieces (max 15 deg) and, around breaches, fine pieces aligned to the brick grid.</summary>
        List<(float, float, bool)> SplitRing(int ring, float chunk)
        {
            float R = Rings[ring].R, k = Mathf.Deg2Rad * R, q = BrickW / 4f;
            var fine = new List<(float, float)>();
            foreach (var b in Breaches)
            {
                if (b.Ring != ring) continue;
                float half = (b.Half * 1.6f + 1.8f) / k;
                float f0 = Mathf.Floor((b.Angle - half) * k / q) * q / k, f1 = Mathf.Ceil((b.Angle + half) * k / q) * q / k;
                if (f0 < 0 || f1 > 360) continue;
                fine.Add((f0, f1));
            }
            fine.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            var res = new List<(float, float, bool)>();
            void Coarse(float x0, float x1)
            {
                int n = Mathf.Max(1, Mathf.CeilToInt((x1 - x0) / chunk - 1e-4f));
                for (int m = 0; m < n; m++) res.Add((Mathf.Lerp(x0, x1, (float)m / n), Mathf.Lerp(x0, x1, (float)(m + 1) / n), false));
            }
            float cur = 0;
            foreach (var (f0, f1) in fine)
            {
                if (f0 < cur) { if (f1 > cur) { res.Add((cur, f1, true)); cur = f1; } continue; }
                if (f0 > cur + 1e-4f) Coarse(cur, f0);
                res.Add((f0, f1, true)); cur = f1;
            }
            if (cur < 360f - 1e-4f) Coarse(cur, 360f);
            return res;
        }

        // ================================================================ batching helpers
        class Batch
        {
            readonly Dictionary<Material, List<CombineInstance>> parts = new();
            public void Add(Mesh m, Material mat, Vector3 pos, Quaternion rot, Vector3 scale)
            {
                if (!parts.TryGetValue(mat, out var l)) parts[mat] = l = new List<CombineInstance>();
                l.Add(new CombineInstance { mesh = m, transform = Matrix4x4.TRS(pos, rot, scale) });
            }
            public void Box(Material mat, Vector3 pos, Quaternion rot, Vector3 size) => Add(MeshKit.Box(size), mat, pos, rot, Vector3.one);
            /// <summary>Unit cube scaled (for many randomly sized bits like bricks: no per-size mesh).</summary>
            public void Cube(Material mat, Vector3 pos, Quaternion rot, Vector3 size) => Add(MeshKit.Box(Vector3.one), mat, pos, rot, size);
            public void Flush(Transform parent, string name, bool collider)
            {
                foreach (var kv in parts)
                {
                    if (kv.Value.Count == 0) continue;
                    var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                    mesh.RecalculateBounds();
                    MeshKit.Created.Add(mesh);
                    MeshKit.MeshObject(parent, name, mesh, new[] { kv.Key }, collider);
                }
                parts.Clear();
            }
        }

        /// <summary>Thin tubes along polylines (cables), all in one mesh.</summary>
        class Tubes
        {
            readonly List<Vector3> v = new(), n = new();
            readonly List<int> t = new();
            public void Add(List<Vector3> pts, float r, int sides = 5)
            {
                if (pts.Count < 2) return;
                int start = v.Count;
                Vector3 prevUp = Vector3.up;
                for (int i = 0; i < pts.Count; i++)
                {
                    Vector3 dir = (i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).normalized;
                    Vector3 side = Vector3.Cross(dir, prevUp);
                    if (side.sqrMagnitude < 1e-4f) side = Vector3.Cross(dir, Vector3.right);
                    side.Normalize();
                    Vector3 up = Vector3.Cross(side, dir).normalized; prevUp = up;
                    for (int s = 0; s < sides; s++)
                    {
                        float ang = s * Mathf.PI * 2 / sides;
                        Vector3 nn = side * Mathf.Cos(ang) + up * Mathf.Sin(ang);
                        v.Add(pts[i] + nn * r); n.Add(nn);
                    }
                }
                for (int i = 0; i < pts.Count - 1; i++)
                    for (int s = 0; s < sides; s++)
                    {
                        int a = start + i * sides + s, b = start + i * sides + (s + 1) % sides, c = a + sides, d = b + sides;
                        t.Add(a); t.Add(c); t.Add(b); t.Add(b); t.Add(c); t.Add(d);
                    }
            }
            public void Flush(Transform parent, string name, Material mat)
            {
                if (v.Count == 0) return;
                var m = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
                MeshKit.Created.Add(m);
                MeshKit.MeshObject(parent, name, m, new[] { mat }, false);
                v.Clear(); n.Clear(); t.Clear();
            }
        }

        static Mesh sphereMesh, cylMesh;
        static Mesh BuiltinMesh(PrimitiveType p)
        {
            var g = GameObject.CreatePrimitive(p);
            var m = g.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(g);
            return m;
        }

        // ================================================================ per ring
        void BuildFeatures(int i, Transform ring)
        {
            sphereMesh ??= BuiltinMesh(PrimitiveType.Sphere);
            cylMesh ??= BuiltinMesh(PrimitiveType.Cylinder);
            var d = Rings[i];
            var fx = new GameObject("Features").transform;
            fx.SetParent(ring, false);
            foreach (var f in Features)
            {
                if (f.ring != i) continue;
                if (f.kind == FeatureKind.Fan) FanBulkhead(fx, d, f.angle);
                else if (f.kind == FeatureKind.Grate) GrateBulkhead(fx, d, f.angle);
                else if (f.kind == FeatureKind.Climb) ClimbBulkhead(fx, d, f.angle);
                else Divider(fx, d, f.angle);
            }
            foreach (var b in Breaches) if (b.Ring == i) BreachDressing(fx, i, d, b);
            CollapsePiles(fx, i, d);
            CeilingPipes(fx, i, d);
            Cables(fx, i, d);
        }

        static float WallAt(float v) => Mathf.Sqrt(Mathf.Max(0, TunnelR * TunnelR - (v - TunnelC) * (v - TunnelC)));
        static Vector3 L(RingDef d, float a, float u, float v) => Geo.Polar(a, d.R + u, d.Y + v);

        static List<(float, float)> Subtract(List<(float, float)> iv, float s0, float s1)
        {
            var r = new List<(float, float)>();
            foreach (var (a, b) in iv)
            {
                if (s1 <= a || s0 >= b) { r.Add((a, b)); continue; }
                if (s0 > a) r.Add((a, s0));
                if (s1 < b) r.Add((s1, b));
            }
            return r;
        }

        // ------------------------------------------------------------ fan bulkhead (like a big ventilation fan in the tunnel)
        void FanBulkhead(Transform parent, RingDef d, float a)
        {
            var go = new GameObject($"FanBulkhead_{a:0}").transform; go.SetParent(parent, false);
            Vector3 O = Geo.Polar(a, d.R, d.Y), t = Tangent(a), o = Geo.Out(a);
            Quaternion rot = Quaternion.LookRotation(t);
            Vector3 P(float u, float v, float z = 0) => O + o * u + Vector3.up * v + t * z;
            const float fanV = 6.0f, fr = 3.0f, thick = 1.0f;

            var wall = new Batch(); var trim = new Batch();
            var holes = new List<(float u, float v, float r, float floor)> { (0, fanV, fr, -9f), (-WalkU, DoorC, DoorR, 0f), (WalkU, DoorC, DoorR, 0f) };
            HoledWall(wall, mBrick, P, rot, thick, holes);
            ArchRing(trim, mChamber, P, rot, 0, fanV, fr + 0.3f, 22, 0, 360, thick + 0.5f);
            foreach (float s in new[] { -1f, 1f })
            {
                ArchRing(trim, mChamber, P, rot, s * WalkU, DoorC, DoorR + 0.3f, 12, -32f, 212f, thick + 0.4f);
                trim.Box(mConcrete, P(s * WalkU, 0.05f), rot, new Vector3(2.8f, 0.1f, thick + 0.6f));
            }
            // guard grills on both faces of the duct
            for (int k = -3; k <= 3; k++)
            {
                float y = k * fr / 3.6f, hw = Mathf.Sqrt(Mathf.Max(0, fr * fr - y * y));
                foreach (float z in new[] { -1.15f, 1.15f })
                    trim.Box(mMetal, P(0, fanV + y, z), rot, new Vector3(hw * 2, 0.08f, 0.08f));
            }
            foreach (float z in new[] { -1.15f, 1.15f }) trim.Box(mMetal, P(0, fanV, z), rot, new Vector3(0.08f, fr * 2, 0.08f));
            wall.Flush(go, "BulkheadWall", true);
            trim.Flush(go, "BulkheadTrim", false);

            // the duct (round tube through the wall)
            var duct = MeshKit.SweepStraight("FanDuct", MeshKit.PipeProfile(new Vector2(0, fanV), fr + 0.05f, 24), O - t * 1.25f, t, 2.5f, 1.25f, 1);
            MeshKit.MeshObject(go, "FanDuct", duct, new[] { mRust }, false);
            // invisible blocker over the fan opening
            foreach (float tw in new[] { 0f, 45f })
            {
                var block = new GameObject("FanBlock"); block.transform.SetParent(go, false);
                block.transform.SetPositionAndRotation(P(0, fanV), rot * Quaternion.Euler(0, 0, tw));
                block.AddComponent<BoxCollider>().size = new Vector3(fr * 1.6f, fr * 1.6f, 2.4f);
            }

            // fan: hub + 5 twisted blades, spinning
            var fan = new GameObject("Fan").transform; fan.SetParent(go, false);
            fan.SetPositionAndRotation(P(0, fanV), rot);
            var hub = Geo.Prim(PrimitiveType.Cylinder, fan, "Hub", Vector3.zero, new Vector3(1.1f, 0.35f, 1.1f), mRust);
            hub.transform.localRotation = Quaternion.Euler(90, 0, 0);
            float len = fr - 0.75f;
            for (int k = 0; k < 5; k++)
            {
                Quaternion br = Quaternion.Euler(0, 0, k * 72f);
                var bl = Geo.TBox(fan, "Blade", fan.position, fan.rotation, new Vector3(1.3f, len, 0.08f), mCopper, false);
                bl.transform.localPosition = br * new Vector3(0, len / 2 + 0.45f, 0);
                bl.transform.localRotation = br * Quaternion.AngleAxis(28f, Vector3.up);
            }
            fan.gameObject.AddComponent<FanSpin>().Speed = 55f + (a % 30f);
            Geo.PointLight(go, P(0, fanV, -3f), Mat.Hex("#FFD08A"), 13f, 4f);
            Geo.PointLight(go, P(0, fanV, 3f), Mat.Hex("#FFD08A"), 13f, 4f);
        }

        const float DoorR = 1.95f, DoorC = 1.5f;   // round doorway: circle cut by the floor (2.6 m wide at the floor, 3.45 m high)

        /// <summary>Brick wall filling the tunnel cross-section (0.25 m rows) with round holes; a hole never goes below its floor.</summary>
        static void HoledWall(Batch wall, Material m, System.Func<float, float, float, Vector3> P, Quaternion rot, float thick, List<(float u, float v, float r, float floor)> holes)
        {
            const float rowH = 0.25f;
            for (float v0 = 0; v0 < TunnelC + TunnelR; v0 += rowH)
            {
                float v1 = v0 + rowH;
                float vn = TunnelC >= v0 && TunnelC <= v1 ? TunnelC : (Mathf.Abs(v0 - TunnelC) < Mathf.Abs(v1 - TunnelC) ? v0 : v1);
                float w = WallAt(vn) + 0.2f;
                var iv = new List<(float, float)> { (-w, w) };
                foreach (var h in holes)
                {
                    if (v1 <= h.floor + 1e-3f) continue;
                    float vm = (v0 + v1) / 2;                       // row middle: smooth stepped circle, the stone ring hides the steps
                    float dv = Mathf.Abs(vm - h.v);
                    if (dv >= h.r) continue;
                    float hw = Mathf.Sqrt(h.r * h.r - dv * dv);
                    iv = Subtract(iv, h.u - hw, h.u + hw);
                }
                foreach (var (s0, s1) in iv)
                    if (s1 - s0 > 0.04f) wall.Box(m, P((s0 + s1) / 2, (v0 + v1) / 2, 0), rot, new Vector3(s1 - s0, rowH, thick));
            }
        }

        /// <summary>Chunky stone blocks around a round opening (degrees, 0 = right, 90 = top).</summary>
        static void ArchRing(Batch b, Material m, System.Func<float, float, float, Vector3> P, Quaternion rot, float cu, float cv, float rr, int blocks, float fromDeg, float toDeg, float depth)
        {
            bool full = toDeg - fromDeg >= 359f;
            for (int k = 0; k < blocks; k++)
            {
                float t = Mathf.Lerp(fromDeg, toDeg, full ? (float)k / blocks : (k + 0.5f) / blocks) * Mathf.Deg2Rad;
                float x = Mathf.Cos(t) * rr, y = Mathf.Sin(t) * rr;
                if (cv + y < 0.15f) continue;
                float arc = (toDeg - fromDeg) * Mathf.Deg2Rad * rr / blocks;
                b.Box(m, P(cu + x, cv + y, 0), rot * Quaternion.Euler(0, 0, t * Mathf.Rad2Deg), new Vector3(0.7f, arc + 0.06f, depth));
            }
        }

        // ------------------------------------------------------------ climb wall: round hole up high, a ledge in front to climb onto
        void ClimbBulkhead(Transform parent, RingDef d, float a)
        {
            var go = new GameObject($"ClimbBulkhead_{a:0}").transform; go.SetParent(parent, false);
            Vector3 O = Geo.Polar(a, d.R, d.Y), t = Tangent(a), o = Geo.Out(a);
            Quaternion rot = Quaternion.LookRotation(t);
            Vector3 P(float u, float v, float z = 0) => O + o * u + Vector3.up * v + t * z;
            const float thick = 1.0f, ledgeH = 2.1f, hr = 1.55f, hc = ledgeH + 0.95f, depth = 1.7f;
            float hu = (a % 2f) < 1f ? WalkU : -WalkU;                  // which walkway gets the hole
            var wall = new Batch(); var stone = new Batch();
            HoledWall(wall, mBrick, P, rot, thick, new List<(float, float, float, float)> { (hu, hc, hr, ledgeH) });
            ArchRing(stone, mChamber, P, rot, hu, hc, hr + 0.3f, 14, -28f, 208f, thick + 0.4f);
            // ledges on both sides + the sill through the wall, all at ledge height
            foreach (float z in new[] { -1f, 1f })
            {
                stone.Box(mChamber, P(hu, ledgeH / 2, z * (thick / 2 + depth / 2)), rot, new Vector3(3.6f, ledgeH, depth));
                stone.Box(mConcrete, P(hu, ledgeH + 0.06f, z * (thick / 2 + depth / 2)), rot, new Vector3(3.8f, 0.12f, depth + 0.1f));
                // a couple of loose bricks / a crate-sized step look on the ledge face
                stone.Box(mBrick, P(hu + 1.2f * z, 0.3f, z * (thick / 2 + depth + 0.25f)), rot * Quaternion.Euler(0, 12f * z, 0), new Vector3(0.9f, 0.6f, 0.5f));
            }
            stone.Box(mConcrete, P(hu, ledgeH + 0.06f, 0), rot, new Vector3(2.9f, 0.12f, thick + 0.02f));
            wall.Flush(go, "ClimbWall", true);
            stone.Flush(go, "ClimbLedges", true);
            Geo.PointLight(go, P(hu, ledgeH + 1.2f, -2.2f), lampColor, 11f, 3.5f);
            Geo.PointLight(go, P(hu, ledgeH + 1.2f, 2.2f), lampColor, 11f, 3.5f);
        }

        // ------------------------------------------------------------ grate bulkhead with a broken gap and an open gate
        void GrateBulkhead(Transform parent, RingDef d, float a)
        {
            var go = new GameObject($"GrateBulkhead_{a:0}").transform; go.SetParent(parent, false);
            Vector3 O = Geo.Polar(a, d.R, d.Y), t = Tangent(a), o = Geo.Out(a);
            Quaternion rot = Quaternion.LookRotation(t);
            Vector3 P(float u, float v, float z = 0) => O + o * u + Vector3.up * v + t * z;
            var bars = new Batch(); var deco = new Batch();
            const float gateTop = 4.0f;
            float broken0 = WalkU - 1.3f, broken1 = WalkU + 1.2f;      // +side: bent bars, squeeze through
            float gate0 = -WalkU - 1.3f, gate1 = -WalkU + 1.3f;        // -side: open gate
            var rr = new System.Random((int)a * 31 + 5);
            for (float u = -7.2f; u <= 7.21f; u += 0.42f)
            {
                float top = TunnelC + Mathf.Sqrt(Mathf.Max(0, TunnelR * TunnelR - u * u)) + 0.1f;
                float bot = Mathf.Abs(u) < ChannelHalf ? -ChannelDepth : (Mathf.Abs(u) > WallU ? TunnelC - Mathf.Sqrt(Mathf.Max(0, TunnelR * TunnelR - u * u)) : 0f);
                if (u > broken0 && u < broken1)
                {
                    // stub hanging from the top, tip bent toward the viewer
                    float end = 3.0f + (float)rr.NextDouble() * 1.4f;
                    bars.Box(mRust, P(u, (top + end) / 2), rot, new Vector3(0.1f, top - end, 0.1f));
                    bars.Box(mRust, P(u, end - 0.35f, -0.25f), rot * Quaternion.Euler(-40f + (float)rr.NextDouble() * 20f, 0, (float)rr.NextDouble() * 30f - 15f), new Vector3(0.1f, 0.8f, 0.1f));
                    continue;
                }
                if (u > gate0 && u < gate1) { bars.Box(mRust, P(u, (top + gateTop) / 2), rot, new Vector3(0.1f, top - gateTop, 0.1f)); continue; }
                bars.Box(mRust, P(u, (top + bot) / 2), rot, new Vector3(0.1f, top - bot, 0.1f));
            }
            foreach (float v in new[] { 1.0f, gateTop, 6.6f, 9.1f })
            {
                float w = WallAt(v) + 0.1f;
                var iv = new List<(float, float)> { (-w, w) };
                if (v < gateTop) { iv = Subtract(iv, broken0, broken1); iv = Subtract(iv, gate0, gate1); }
                else if (v < 4.5f) iv = Subtract(iv, broken0, broken1);
                foreach (var (s0, s1) in iv) bars.Box(mRust, P((s0 + s1) / 2, v), rot, new Vector3(s1 - s0, 0.16f, 0.16f));
            }
            // heavy posts
            foreach (float u in new[] { -2.3f, 2.3f, gate0 - 0.15f, gate1 + 0.15f })
            {
                float top = TunnelC + Mathf.Sqrt(TunnelR * TunnelR - u * u);
                float bot = Mathf.Abs(u) < ChannelHalf ? -ChannelDepth : 0f;
                bars.Box(mMetal, P(u, (top + bot) / 2), rot, new Vector3(0.32f, top - bot, 0.32f));
            }
            // fallen bars on the walkway under the broken part
            deco.Box(mRust, P(WalkU + 0.3f, 0.06f, -1.4f), Quaternion.LookRotation(o) * Quaternion.Euler(90, 20, 0), new Vector3(0.1f, 2.6f, 0.1f));
            deco.Box(mRust, P(WalkU - 0.4f, 0.06f, -0.9f), Quaternion.LookRotation(o) * Quaternion.Euler(90, -35, 0), new Vector3(0.1f, 1.9f, 0.1f));
            bars.Flush(go, "Grate", true);
            deco.Flush(go, "GrateDeco", false);

            // open gate leaf, hinged at the outer post
            var leaf = new GameObject("GateLeaf").transform; leaf.SetParent(go, false);
            leaf.SetPositionAndRotation(P(gate0, 0), rot * Quaternion.Euler(0, -72f, 0));
            var lb = new Batch();
            float lw = gate1 - gate0;
            for (float x = 0.2f; x < lw; x += 0.42f) lb.Box(mRust, leaf.TransformPoint(new Vector3(x, gateTop / 2, 0)), leaf.rotation, new Vector3(0.09f, gateTop - 0.2f, 0.09f));
            foreach (float y in new[] { 0.2f, 1.9f, gateTop - 0.15f }) lb.Box(mMetal, leaf.TransformPoint(new Vector3(lw / 2, y, 0)), leaf.rotation, new Vector3(lw, 0.14f, 0.14f));
            lb.Flush(go, "GateLeafMesh", true);
            Object.DestroyImmediate(leaf.gameObject);
            Geo.PointLight(go, P(0, 7f, -2.5f), lampColor, 14f, 3f);
        }

        // ------------------------------------------------------------ split path: a dividing wall on pillars over the channel
        void Divider(Transform parent, RingDef d, float a)
        {
            var go = new GameObject($"Divider_{a:0}").transform; go.SetParent(parent, false);
            float half = DividerHalf / d.R * Mathf.Rad2Deg;
            float a0 = a - half, a1 = a + half;
            var wall = new Batch(); var stone = new Batch();
            int segs = Mathf.CeilToInt(2 * DividerHalf / 2f);
            const float bottom = 1.4f, top = 11.05f, th = 1.0f;
            float segLen = 2 * DividerHalf / segs;
            for (int k = 0; k < segs; k++)
            {
                float am = Mathf.Lerp(a0, a1, (k + 0.5f) / segs);
                Quaternion r = Quaternion.LookRotation(Tangent(am));
                wall.Box(mBrick, L(d, am, 0, (bottom + top) / 2), r, new Vector3(th, top - bottom, segLen + 0.06f));
                stone.Box(mChamber, L(d, am, 0, bottom + 0.15f), r, new Vector3(th + 0.25f, 0.35f, segLen + 0.06f));
                stone.Box(mChamber, L(d, am, 0, 6.4f), r, new Vector3(th + 0.18f, 0.25f, segLen + 0.06f));
                if (k % 2 == 0)
                    stone.Box(mChamber, L(d, Mathf.Lerp(a0, a1, (float)k / segs), 0, (bottom - ChannelDepth) / 2), r, new Vector3(1.25f, bottom + ChannelDepth, 0.9f));
            }
            stone.Box(mChamber, L(d, a1, 0, (bottom - ChannelDepth) / 2), Quaternion.LookRotation(Tangent(a1)), new Vector3(1.25f, bottom + ChannelDepth, 0.9f));
            wall.Flush(go, "DividerWall", true);
            stone.Flush(go, "DividerStone", true);
            // round end columns with "this way" neons
            foreach (float ae in new[] { a0, a1 })
            {
                var col = Geo.Prim(PrimitiveType.Cylinder, go, "DividerEnd", L(d, ae, 0, (top - ChannelDepth) / 2), new Vector3(1.5f, (top + ChannelDepth) / 2, 1.5f), mChamber, true);
                Vector3 face = Tangent(ae) * (ae == a0 ? -1 : 1);
                PropLib.DecalQuad(go, "neon_thisway", L(d, ae, 0, 4.2f) + face * 0.78f, -face, Vector3.up, new Vector2(2.4f, 1.2f), 2.2f);
            }
            // lamps and graffiti on both faces
            for (int k = 0; k < 3; k++)
            {
                float am = Mathf.Lerp(a0, a1, (k + 0.5f) / 3f);
                Quaternion r = Quaternion.LookRotation(Tangent(am));
                foreach (float s in new[] { -1f, 1f })
                {
                    Vector3 n = Geo.Out(am) * s;
                    Geo.TBox(go, "WallLamp", L(d, am, s * 0.62f, 5.4f), r, new Vector3(0.3f, 0.35f, 1.2f), mMetal, false);
                    Geo.TBox(go, "WallLampGlow", L(d, am, s * 0.8f, 5.3f), r, new Vector3(0.06f, 0.2f, 1.0f), mLamp, false);
                    Geo.PointLight(go, L(d, am, s * 2.0f, 5.0f), lampColor, 13f, 4f);
                    int seed = Mathf.Abs((int)(a * 7 + s * 3 + k));
                    string tex = k == 1 ? Pieces[seed % Pieces.Length] : Throwups[seed % Throwups.Length];
                    Vector2 size = k == 1 ? new Vector2(6.4f, 3.2f) : new Vector2(3.6f, 1.8f);
                    PropLib.DecalQuad(go, tex, L(d, am + (k == 1 ? 0 : 2.2f / d.R * Mathf.Rad2Deg), s * 0.515f, k == 1 ? 8.6f : 2.6f), -n, Vector3.up, size);
                }
            }
        }
        static readonly string[] Pieces = { "graffiti_skratch", "graffiti_vybez", "graffiti_krown", "graffiti_noize", "graffiti_slime", "graffiti_ghost", "graffiti_funk" };
        static readonly string[] Throwups = { "throwup_rk", "throwup_zx", "throwup_ok", "throwup_2k" };

        // ------------------------------------------------------------ wall breach: knocked-out bricks, brick thickness at the edge, dark cavity, rubble
        void BreachDressing(Transform parent, int i, RingDef d, Breach b)
        {
            var go = new GameObject($"Breach_{b.Angle:0}").transform; go.SetParent(parent, false);
            var rr = new System.Random((int)(b.Angle * 100) + i);
            float Rf(float x, float y) => x + (y - x) * (float)rr.NextDouble();
            float s = b.Side, a = b.Angle, R = d.R;
            Vector3 t = Tangent(a), o = Geo.Out(a);
            var dark = Mat.Tex(RingBrick[i], BrickTile, 0.05f, new Color(0.16f, 0.14f, 0.18f));
            var dirt = Mat.Get(Mat.Hex("#5B4636"), 0.1f);
            var edge = new Batch(); var loose = new Batch();

            // world position / outward normal of a vault point given (s along the ring, pv along the profile)
            Vector3 WP(float sAlong, float pv, float push, out Vector3 outN, out Vector3 upV, out Vector3 tan)
            {
                float ad = sAlong / R * Mathf.Rad2Deg, th = ThetaOf(pv);
                Vector3 oo = Geo.Out(ad);
                outN = oo * Mathf.Cos(th) + Vector3.up * Mathf.Sin(th);
                upV = -oo * Mathf.Sin(th) + Vector3.up * Mathf.Cos(th);
                tan = Tangent(ad);
                return Geo.Polar(ad, R + Mathf.Cos(th) * TunnelR, d.Y + TunnelC + Mathf.Sin(th) * TunnelR) + outN * push;
            }

            float sb = a * Mathf.Deg2Rad * R;
            float vc = (b.V0 + b.V1) / 2;
            float pvC = PvOf(new Vector2(s * WallAt(vc), vc));
            int c0 = Mathf.FloorToInt((sb - b.Half * 1.6f - 1f) / BrickW), c1 = Mathf.CeilToInt((sb + b.Half * 1.6f + 1f) / BrickW);
            int r0 = Mathf.FloorToInt((pvC - 2.6f) / BrickH), r1 = Mathf.CeilToInt((pvC + 2.6f) / BrickH);
            for (int row = r0; row <= r1; row++)
                for (int col = c0 - 1; col <= c1 + 1; col++)
                {
                    if (BrickGone(i, b, col, row)) continue;
                    bool border = BrickGone(i, b, col - 1, row) || BrickGone(i, b, col + 1, row);
                    // offset rows: a brick touches two bricks above and two below
                    int oc = (row & 1) == 0 ? 1 : -1;
                    foreach (int rn in new[] { row - 1, row + 1 })
                        border |= BrickGone(i, b, col, rn) || BrickGone(i, b, col + oc, rn);
                    if (!border) continue;
                    var (sc, pvc) = CellCenter(col, row);
                    // the brick's own body behind the wall surface: through the hole you see its sides = real wall thickness
                    Vector3 p = WP(sc, pvc, 0.03f + 0.22f, out var n, out var up, out var tn);
                    edge.Box(mBrick, p, Quaternion.LookRotation(n, up), new Vector3(BrickW - 0.03f, BrickH - 0.03f, 0.44f));
                    // some edge bricks are loose and stick out a bit
                    if (rr.NextDouble() < 0.28)
                    {
                        Vector3 lp = WP(sc, pvc, -Rf(0.03f, 0.1f), out _, out _, out _);
                        loose.Box(mBrick, lp, Quaternion.LookRotation(n, up) * Quaternion.Euler(Rf(-6, 6), Rf(-6, 6), Rf(-9, 9)), new Vector3(BrickW - 0.05f, BrickH - 0.05f, 0.3f));
                    }
                }
            // second, darker layer of bricks behind + invisible wall (you can look in, not walk in)
            Vector3 cp = WP(sb, pvC, 0.9f, out var cn, out var cu, out _);
            Quaternion cr = Quaternion.LookRotation(cn, cu);
            edge.Box(dark, cp, cr, new Vector3(b.Half * 2 + 3f, (b.V1 - b.V0) + 3f, 0.3f));
            for (int k = -1; k <= 1; k += 2)
            {
                edge.Box(dark, WP(sb + k * (b.Half * 1.6f + 1.1f), pvC, 0.5f, out _, out _, out _), cr, new Vector3(0.3f, (b.V1 - b.V0) + 3f, 0.9f));
                edge.Box(dark, WP(sb, pvC + k * ((b.V1 - b.V0) / 2 + 1.5f), 0.5f, out _, out _, out _), cr, new Vector3(b.Half * 2 + 3f, 0.3f, 0.9f));
            }
            edge.Flush(go, "BreachEdge", false);
            loose.Flush(go, "BreachLoose", false);
            var block = new GameObject("BreachBlock"); block.transform.SetParent(go, false);
            block.transform.SetPositionAndRotation(WP(sb, pvC, -0.05f, out _, out _, out _), cr);
            block.AddComponent<BoxCollider>().size = new Vector3(b.Half * 2 + 1.5f, (b.V1 - b.V0) + 1.5f, 0.1f);

            Vector3 P(float u, float v, float z = 0) => L(d, a, s * u, v) + t * z;
            var rub = new Batch();
            rub.Add(sphereMesh, dirt, P(5.7f, -0.05f), Quaternion.LookRotation(t), new Vector3(1.7f, 0.7f, Rf(1.4f, 2.0f) * b.Half));
            int nb = 16 + rr.Next(10);
            for (int k = 0; k < nb; k++)
            {
                float u = Rf(3.8f, 6.5f), z = Rf(-b.Half - 1.3f, b.Half + 1.3f);
                rub.Cube(mBrick, P(u, Rf(0.08f, 0.28f) * (u > 5.2f ? 1.6f : 1f), z), Quaternion.Euler(Rf(-30, 30), Rf(0, 360), Rf(-30, 30)), new Vector3(BrickW * 0.6f, 0.22f, 0.26f) * Rf(0.7f, 1.05f));
            }
            rub.Flush(go, "Rubble", false);
            if (rr.NextDouble() < 0.45) Geo.PointLight(go, WP(sb, pvC, 0.5f, out _, out _, out _), Mat.Hex(rr.NextDouble() < 0.5 ? "#7CFF3A" : "#B46BFF"), 4f, 1.6f);
            else
            {
                var glowMat = Mat.Emissive(Mat.Hex("#FFE45C"), 4f);   // eyes in the dark
                Vector3 e = WP(sb + Rf(-0.4f, 0.4f), pvC + Rf(-0.4f, 0.2f), 0.7f, out _, out _, out var et);
                Geo.Prim(PrimitiveType.Sphere, go, "Eye", e - et * 0.12f, Vector3.one * 0.08f, glowMat);
                Geo.Prim(PrimitiveType.Sphere, go, "Eye", e + et * 0.12f, Vector3.one * 0.08f, glowMat);
            }
        }

        // ------------------------------------------------------------ collapsed brick piles (no hole) under cracked walls
        void CollapsePiles(Transform parent, int i, RingDef d)
        {
            var rr = new System.Random(900 + i);
            float Rf(float x, float y) => x + (y - x) * (float)rr.NextDouble();
            var dirt = Mat.Get(Mat.Hex("#5B4636"), 0.1f);
            var bt = new Batch();
            int count = new[] { 14, 9, 5 }[i], placed = 0, guard = 0;
            while (placed < count && guard++ < 300)
            {
                float a = Rf(0, 360);
                if (NearOpening(i, a, 8f) || (i == 0 && NearSpawn(a, 8f))) continue;
                placed++;
                float s = rr.NextDouble() < 0.5 ? 1 : -1;
                Vector3 t = Tangent(a), o = Geo.Out(a) * s;
                Vector3 P(float u, float v, float z = 0) => L(d, a, s * u, v) + t * z;
                bt.Add(sphereMesh, dirt, P(6.0f, 0f), Quaternion.LookRotation(t), new Vector3(1.8f, 0.55f, Rf(2.0f, 3.4f)));
                int n = 12 + rr.Next(10);
                for (int k = 0; k < n; k++)
                    bt.Cube(mBrick, P(Rf(4.4f, 6.5f), Rf(0.08f, 0.35f), Rf(-1.8f, 1.8f)), Quaternion.Euler(Rf(-35, 35), Rf(0, 360), Rf(-35, 35)), new Vector3(0.5f, 0.24f, 0.26f) * Rf(0.7f, 1.05f));
            }
            bt.Flush(parent, "CollapsePiles", false);
        }

        // ------------------------------------------------------------ extra pipes along the vault, with hangers
        void CeilingPipes(Transform parent, int i, RingDef d)
        {
            var hang = new Batch();
            var pipes = new (Vector2 c, float r, Material m)[]
            {
                (new Vector2(2.4f, 10.2f), 0.32f, mRust), (new Vector2(-1.25f, 10.5f), 0.22f, mCopper), (new Vector2(0.95f, 10.55f), 0.15f, mMetal),
                (new Vector2(3.7f, 9.75f), 0.2f, mTeal), (new Vector2(-3.6f, 9.9f), 0.15f, mCopper), (new Vector2(-6.0f, 7.0f), 0.2f, mRust),
            };
            foreach (var (a0, a1) in ArcsBetween(i, 7f))
            {
                foreach (var (c, r, m) in pipes)
                {
                    var mesh = MeshKit.SweepRing("PipeX", MeshKit.PipeProfile(c, r, 10), d.R, d.Y, a0, a1, 3f, 1);
                    MeshKit.MeshObject(parent, "PipeX", mesh, new[] { m }, false);
                }
                int n = Mathf.FloorToInt((a1 - a0) * Mathf.Deg2Rad * d.R / 8f);
                for (int k = 0; k <= n; k++)
                {
                    float a = Mathf.Lerp(a0, a1, n == 0 ? 0.5f : (float)k / n);
                    Quaternion rot = Quaternion.LookRotation(Tangent(a));
                    for (int p = 0; p < 5; p++)
                    {
                        var (c, r, _) = pipes[p];
                        float top = TunnelC + Mathf.Sqrt(TunnelR * TunnelR - c.x * c.x) + 0.1f;
                        hang.Box(mMetal, L(d, a, c.x, (c.y + top) / 2), rot, new Vector3(0.06f, top - c.y, 0.06f));
                        hang.Box(mMetal, L(d, a, c.x, c.y), rot, new Vector3(r * 2 + 0.1f, 0.05f, 0.12f));
                    }
                    hang.Box(mMetal, L(d, a, -6.0f - 0.45f, 7.0f), rot, new Vector3(0.9f, 0.08f, 0.12f));
                }
            }
            hang.Flush(parent, "PipeHangers", false);
        }

        // ------------------------------------------------------------ cable bundles sagging along the walls + cables across the vault
        void Cables(Transform parent, int i, RingDef d)
        {
            var black = new Tubes(); var color = new Tubes(); var clips = new Batch();
            var cableMat = Mat.Get(Mat.Hex("#17161C"), 0.35f);
            var cableCol = Mat.Get(Mat.Hex(new[] { "#E8453C", "#3EB4FF", "#E8C23C" }[i]), 0.35f);
            var rr = new System.Random(300 + i);
            const float span = 5f;
            foreach (var (a0, a1) in ArcsBetween(i, 7f))
            {
                float len = (a1 - a0) * Mathf.Deg2Rad * d.R;
                int spans = Mathf.Max(1, Mathf.FloorToInt(len / span));
                foreach (float s in new[] { -1f, 1f })
                {
                    for (int c = 0; c < 4; c++)
                    {
                        float v = 6.1f + c * 0.22f, u = s * (WallAt(v) - 0.22f - c * 0.05f), sag = 0.35f + c * 0.12f;
                        var pts = new List<Vector3>();
                        for (int k = 0; k < spans; k++)
                            for (int q = 0; q < 6; q++)
                            {
                                float tt = q / 6f, aa = Mathf.Lerp(a0, a1, (k + tt) / spans);
                                float drop = sag * 4 * tt * (1 - tt);
                                pts.Add(L(d, aa, u - s * drop * 0.35f, v - drop));
                            }
                        pts.Add(L(d, a1, u, v));
                        (c == 2 ? color : black).Add(pts, c == 0 ? 0.06f : 0.04f);
                    }
                    for (int k = 0; k <= spans; k++)
                    {
                        float aa = Mathf.Lerp(a0, a1, (float)k / spans);
                        clips.Box(mMetal, L(d, aa, s * (WallAt(6.4f) - 0.15f), 6.4f), Quaternion.LookRotation(Tangent(aa)), new Vector3(0.3f, 0.9f, 0.1f));
                    }
                }
                // across the vault every ~25 m, plus loose cables hanging from the ceiling
                int across = Mathf.FloorToInt(len / 25f);
                for (int k = 0; k < across; k++)
                {
                    float aa = Mathf.Lerp(a0, a1, (k + 0.5f) / across);
                    if (NearFeature(i, aa, 2f)) continue;
                    var pts = new List<Vector3>();
                    for (int q = 0; q <= 16; q++)
                    {
                        float tt = q / 16f, u = Mathf.Lerp(-5.8f, 5.8f, tt);
                        pts.Add(L(d, aa, u, 7.9f - 1.3f * 4 * tt * (1 - tt)));
                    }
                    black.Add(pts, 0.05f);
                    if (rr.NextDouble() < 0.5)
                    {
                        var hangPts = new List<Vector3>();
                        float hu = (float)rr.NextDouble() * 6f - 3f, ht = TunnelC + Mathf.Sqrt(TunnelR * TunnelR - hu * hu) - 0.1f, hl = 1.8f + (float)rr.NextDouble() * 2f;
                        for (int q = 0; q <= 8; q++) { float tt = q / 8f; hangPts.Add(L(d, aa + 3f / d.R * Mathf.Rad2Deg, hu + Mathf.Sin(tt * 2.5f) * 0.25f, ht - hl * tt)); }
                        color.Add(hangPts, 0.045f);
                    }
                }
            }
            black.Flush(parent, "Cables", cableMat);
            color.Flush(parent, "CablesColor", cableCol);
            clips.Flush(parent, "CableClips", false);
        }
    }
}
