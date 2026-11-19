using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    public enum RoomKind { Gate, Optional, Arena }

    public class RoomInfo
    {
        public int Level;
        public float Angle;
        public RoomKind Kind;
        public Vector3 Center;     // floor center
        public float Size;         // diameter
        public Gate Gate;          // hatch that opens when the boss dies (Gate rooms)
        public Vector3 ChestPos;   // reward spot (Optional rooms)
        public string HatchName => $"Hatch_L{Level + 1}_{Angle:0}";
    }

    /// <summary>
    /// Builds the sewer from the design sketch (scale x2.5, round tunnels):
    /// 3 concentric ring pipes, each deeper than the previous, cylindrical boss chambers between rings,
    /// floor hatches dropping through shafts to the next ring, crown arena at the bottom.
    /// </summary>
    public partial class LevelBuilder
    {
        // ---- tunnel cross-section
        public const float TunnelR = 7.5f;       // pipe radius (15 m diameter)
        public const float TunnelC = 3.5f;       // pipe center above walkway
        public const float ChannelHalf = 2f, ChannelDepth = 0.45f;
        public const float WalkU = 4.3f;         // walkway center offset from pipe axis
        // ---- passages (doors / connectors / landing tunnels)
        const float PassR = 3.3f, PassC = 1.6f;
        const float HoleHalf = 4.6f, HoleTop = 5.6f;
        const float WallU = 6.7f;                // where the pipe wall is at walkway height
        const float ShaftR = 3.8f;
        const float RoomH = 12f;

        public struct RingDef
        {
            public float R, Y, RoomR, RoomRadius;
            public (float angle, RoomKind kind)[] Rooms;
            public float[] Arrivals; // angles where tunnels from the level above enter (outer wall)
        }

        public static readonly RingDef[] Rings =
        {
            new RingDef { R = 170, Y = 0,   RoomR = 145,  RoomRadius = 10, Arrivals = new float[0],
                Rooms = new[] { (0f, RoomKind.Gate), (90f, RoomKind.Optional), (180f, RoomKind.Gate), (270f, RoomKind.Optional) } },
            new RingDef { R = 115, Y = -25, RoomR = 80,   RoomRadius = 10, Arrivals = new[] { 0f, 180f },
                Rooms = new[] { (90f, RoomKind.Gate), (210f, RoomKind.Optional), (330f, RoomKind.Optional) } },
            new RingDef { R = 45,  Y = -50, RoomR = 27.5f, RoomRadius = 7.5f, Arrivals = new[] { 90f },
                Rooms = new[] { (0f, RoomKind.Gate), (90f, RoomKind.Optional), (180f, RoomKind.Gate), (270f, RoomKind.Optional) } },
        };
        public const float ArenaY = -75f, ArenaR = 34f, ArenaH = 20f;

        public readonly List<RoomInfo> Rooms = new();
        public readonly List<Vector3> Spawns = new();
        public readonly List<(int level, Vector3 pos, float angle, float u)> CrawlerSpots = new();
        /// <summary>Empty spots for your own environment models (see SewerGame.EnvironmentPrefabs).</summary>
        public readonly List<(Vector3 pos, Quaternion rot)> PropSlots = new();
        public Vector3 CrownPos;
        public RoomInfo Arena;

        Transform root;
        Material mBrick, mConcrete, mStone, mWater, mChamber, mTiles, mMetal, mRust, mWood, mLamp, mDark, mCopper, mTeal, mSlime;
        Color lampColor = Color.white;

        // per-ring cartoon palette: 1 warm red-orange, 2 mossy green, 3 purple, 4 (arena) gold
        static readonly string[] RingBrick = { "cbrick1", "cbrick2", "cbrick3", "cbrick4" };
        static readonly string[] RingLamp = { "#FFB45C", "#D8FF8A", "#E8A8FF", "#FFD36B" };
        static readonly Color[] RingSlab = { new Color(1f, 0.93f, 0.86f), new Color(0.86f, 1f, 0.9f), new Color(0.93f, 0.88f, 1f), new Color(1f, 0.95f, 0.8f) };
        static readonly Color[] RingStone = { new Color(1f, 0.9f, 0.82f), new Color(0.88f, 1f, 0.88f), new Color(0.92f, 0.86f, 1f), new Color(1f, 0.92f, 0.7f) };

        void SetRingMats(int i)
        {
            mBrick = Mat.Tex(RingBrick[i], 3.5f, 0.12f);
            mConcrete = Mat.Tex("cslab", 4.5f, 0.1f, RingSlab[i]);
            mChamber = Mat.Tex("cstone", 1.4f, 0.08f, RingStone[i]);
            mLamp = Mat.Emissive(Mat.Hex(RingLamp[i]), 3.5f);
            lampColor = Mat.Hex(RingLamp[i]);
        }
        System.Random rnd;

        /// <summary>Computes rooms, spawns etc. without creating geometry (used when the level is baked into the scene).</summary>
        public void BuildData()
        {
            Rooms.Clear(); Spawns.Clear(); CrawlerSpots.Clear(); PropSlots.Clear();
            rnd = new System.Random(42);
            for (int i = 0; i < Rings.Length; i++)
                foreach (var (angle, kind) in Rings[i].Rooms)
                {
                    var d = Rings[i];
                    Vector3 c = Geo.Polar(angle, d.RoomR, d.Y);
                    Rooms.Add(new RoomInfo
                    {
                        Level = i, Angle = angle, Kind = kind, Center = c, Size = d.RoomRadius * 2,
                        ChestPos = c - Geo.Out(angle) * (d.RoomRadius - 2.2f)
                    });
                }
            Arena = new RoomInfo { Level = 3, Kind = RoomKind.Arena, Center = new Vector3(0, ArenaY, 0), Size = ArenaR * 2 };
            Rooms.Add(Arena);
            CrownPos = new Vector3(0, ArenaY + 1.5f, 0);
            MakeBreaches();

            foreach (float a in new[] { 45f, 135f, 225f, 315f })
                Spawns.Add(Geo.Polar(a, Rings[0].R + WalkU, Rings[0].Y + 0.1f));

            int[] counts = { 16, 11, 7 };
            for (int i = 0; i < Rings.Length; i++)
            {
                int placed = 0, guard = 0;
                while (placed < counts[i] && guard++ < 1000)
                {
                    float a = (float)rnd.NextDouble() * 360f;
                    if (NearOpening(i, a, 14f) || (i == 0 && NearSpawn(a, 12f))) continue;
                    float u = rnd.NextDouble() > 0.5 ? WalkU : -WalkU;
                    CrawlerSpots.Add((i, Geo.Polar(a, Rings[i].R + u, Rings[i].Y + 0.2f), a, u));
                    placed++;
                }
                // prop slots every ~30 m, alternating walkways, pushed against the wall
                var d = Rings[i];
                int n = Mathf.FloorToInt(2 * Mathf.PI * d.R / 30f);
                for (int k = 0; k < n; k++)
                {
                    float a = 360f * (k + 0.5f) / n;
                    if (NearOpening(i, a, 9f)) continue;
                    float u = (k % 2 == 0 ? 1 : -1) * 5.7f;
                    Vector3 p = Geo.Polar(a, d.R + u, d.Y);
                    PropSlots.Add((p, Quaternion.LookRotation(-Geo.Out(a) * Mathf.Sign(u))));
                }
            }
            foreach (var r in Rooms)
            {
                if (r.Kind == RoomKind.Arena) continue;
                float rr = r.Size / 2 - 1.6f;
                foreach (float off in new[] { 120f, 240f })
                {
                    float a = r.Angle + off;
                    PropSlots.Add((r.Center + Geo.Out(a) * rr, Quaternion.LookRotation(-Geo.Out(a))));
                }
            }
        }

        public bool NearOpening(int ring, float a, float meters)
        {
            if (NearFeature(ring, a, meters)) return true;
            var d = Rings[ring];
            foreach (var (ra, _) in d.Rooms) if (Mathf.Abs(Mathf.DeltaAngle(a, ra)) * Mathf.Deg2Rad * d.R < meters) return true;
            foreach (float ar in d.Arrivals) if (Mathf.Abs(Mathf.DeltaAngle(a, ar)) * Mathf.Deg2Rad * d.R < meters) return true;
            return false;
        }

        bool NearSpawn(float a, float meters)
        {
            foreach (float s in new[] { 45f, 135f, 225f, 315f })
                if (Mathf.Abs(Mathf.DeltaAngle(a, s)) * Mathf.Deg2Rad * Rings[0].R < meters) return true;
            return false;
        }

        public Transform Build(Transform parent)
        {
            BuildData();
            root = new GameObject("Level").transform;
            root.SetParent(parent, false);

            SetRingMats(0);
            mStone = Mat.Tex("cchannel", 3f, 0.45f);
            mWater = Mat.TexGlow("cwater", 6f, 0.75f, 0.55f);
            mTiles = Mat.Tex("ctiles", 4f, 0.2f);
            mCopper = Mat.Tex("metal", 2f, 0.35f, Mat.Hex("#E08A3C"), 0.35f);
            mTeal = Mat.Tex("metal", 2f, 0.35f, Mat.Hex("#3FC4AE"), 0.3f);
            mSlime = Mat.Emissive(Mat.Hex("#7CFF3A"), 2.2f);
            mMetal = Mat.Tex("metal", 4.2f, 0.5f, null, 0.5f);
            mRust = Mat.Tex("metal", 2f, 0.35f, Mat.Hex("#B47A55"), 0.3f);
            mWood = Mat.Tex("wood", 1.4f, 0.15f);
            mLamp = Mat.Emissive(Mat.Hex("#FFF1C2"), 3f);
            mDark = Mat.Get(Mat.Hex("#0C0D0B"), 0f);

            for (int i = 0; i < Rings.Length; i++) BuildRing(i);
            foreach (var r in Rooms) if (r.Kind != RoomKind.Arena) BuildRoom(r);
            BuildArena();
            return root;
        }

        // ================================================================ rings
        void BuildRing(int i)
        {
            var d = Rings[i];
            SetRingMats(i);
            var ring = new GameObject($"Ring{i + 1}").transform;
            ring.SetParent(root, false);
            // 47 / 141 vault segments: the fine profile (one segment = half a brick) shares every vertex of the coarse one
            var prof = MeshKit.SewerProfile(TunnelR, TunnelC, ChannelHalf, ChannelDepth, 47);
            var fineProf = MeshKit.SewerProfile(TunnelR, TunnelC, ChannelHalf, ChannelDepth, 141);
            var mats = new[] { mBrick, mConcrete, mStone };

            bool Hole(float a, Vector2 mid)
            {
                if (mid.y < -0.1f || mid.y > HoleTop) return false;
                if (BreachHole(i, a, mid)) return true;
                if (mid.x < 0)
                {
                    foreach (var (ra, _) in d.Rooms)
                        if (Mathf.Abs(Mathf.DeltaAngle(a, ra)) * Mathf.Deg2Rad * (d.R + mid.x) < HoleHalf) return true;
                }
                else
                    foreach (float ar in d.Arrivals)
                        if (Mathf.Abs(Mathf.DeltaAngle(a, ar)) * Mathf.Deg2Rad * (d.R + mid.x) < HoleHalf) return true;
                return false;
            }

            const float chunk = 15f;
            foreach (var (p0, p1, fine) in SplitRing(i, chunk))
            {
                    float stepL = 2.5f;
                    if (fine)
                    {
                        float arc = (p1 - p0) * Mathf.Deg2Rad * d.R;
                        int n = Mathf.Max(1, Mathf.RoundToInt(arc / (BrickW / 4f)));
                        stepL = arc / n * 1.0001f;
                    }
                    var m = MeshKit.SweepRing($"Ring{i + 1}_{p0:0.#}", fine ? fineProf : prof, d.R, d.Y, p0, p1, stepL, 3, Hole, true);
                    MeshKit.MeshObject(ring, $"Pipe_{p0:0.#}", m, mats, true);
            }
            for (float a = 0; a < 360f - 0.01f; a += chunk)
            {
                var w = MeshKit.RingStrip($"Water{i + 1}_{a:0}", d.R, d.Y - 0.15f, ChannelHalf, a, a + chunk, 3f);
                MeshKit.MeshObject(ring, $"Water_{a:0}", w, new[] { mWater }, false).AddComponent<WaterFlow>();
            }

            // door frames
            foreach (var (ra, _) in d.Rooms) Frame(ring, Geo.Polar(ra, d.R - WallU - 0.4f, d.Y), Quaternion.LookRotation(Geo.Out(ra)));
            foreach (float ar in d.Arrivals) Frame(ring, Geo.Polar(ar, d.R + WallU + 0.4f, d.Y), Quaternion.LookRotation(-Geo.Out(ar)));

            // pipes along the vault, split around openings
            foreach (var (a0, a1) in ArcsBetween(i, 7f))
            {
                var p1 = MeshKit.SweepRing("PipeHi", MeshKit.PipeProfile(new Vector2(5.3f, 7.8f), 0.45f, 12), d.R, d.Y, a0, a1, 3f, 1);
                MeshKit.MeshObject(ring, "PipeHi", p1, new[] { mCopper }, false);
                // chunky cartoon flanges on the big pipe
                int fl = Mathf.Max(1, Mathf.FloorToInt((a1 - a0) * Mathf.Deg2Rad * d.R / 9f));
                for (int f = 0; f <= fl; f++)
                {
                    float fa = Mathf.Lerp(a0, a1, (float)f / fl);
                    var fg = Geo.Prim(PrimitiveType.Cylinder, ring, "Flange", Geo.Polar(fa, d.R + 5.3f, d.Y + 7.8f), new Vector3(1.25f, 0.14f, 1.25f), mCopper);
                    fg.transform.rotation = Quaternion.FromToRotation(Vector3.up, Tangent(fa));
                }
                var p2 = MeshKit.SweepRing("PipeLo", MeshKit.PipeProfile(new Vector2(-6.15f, 5.7f), 0.28f, 10), d.R, d.Y, a0, a1, 3f, 1);
                MeshKit.MeshObject(ring, "PipeLo", p2, new[] { mTeal }, false);
                var p3 = MeshKit.SweepRing("PipeHi2", MeshKit.PipeProfile(new Vector2(-4.6f, 8.6f), 0.35f, 10), d.R, d.Y, a0, a1, 3f, 1);
                MeshKit.MeshObject(ring, "PipeHi2", p3, new[] { mMetal }, false);
            }

            // ceiling lamps
            int lamps = Mathf.FloorToInt(2 * Mathf.PI * d.R / 22f);
            for (int k = 0; k < lamps; k++)
            {
                float a = 360f * k / lamps;
                if (NearDivider(i, a, 1f)) continue;
                Quaternion rot = Quaternion.LookRotation(Tangent(a));
                Geo.TBox(ring, "LampHousing", Geo.Polar(a, d.R, d.Y + TunnelC + TunnelR - 0.5f), rot, new Vector3(0.9f, 0.35f, 2.2f), mMetal, false);
                Geo.TBox(ring, "LampGlow", Geo.Polar(a, d.R, d.Y + TunnelC + TunnelR - 0.72f), rot, new Vector3(0.6f, 0.1f, 1.9f), mLamp, false);
                Geo.PointLight(ring, Geo.Polar(a, d.R, d.Y + TunnelC + TunnelR - 1.6f), lampColor, 24f, 8f);
            }

            // wall outlets with grates every ~45 m on the outer wall
            int outs = Mathf.FloorToInt(2 * Mathf.PI * d.R / 45f);
            for (int k = 0; k < outs; k++)
            {
                float a = 360f * (k + 0.3f) / outs;
                if (NearOpening(i, a, 10f)) continue;
                Vector3 o = Geo.Out(a);
                var outlet = Geo.Prim(PrimitiveType.Cylinder, ring, "Outlet", Geo.Polar(a, d.R + 6.95f, d.Y + 2.2f), new Vector3(2f, 0.7f, 2f), mRust);
                outlet.transform.rotation = Quaternion.LookRotation(o) * Quaternion.Euler(90, 0, 0);
                var hole = Geo.Prim(PrimitiveType.Cylinder, ring, "OutletDark", Geo.Polar(a, d.R + 6.22f, d.Y + 2.2f), new Vector3(1.6f, 0.02f, 1.6f), mDark);
                hole.transform.rotation = outlet.transform.rotation;
                for (int b = -2; b <= 2; b++)
                    Geo.TBox(ring, "OutletBar", Geo.Polar(a, d.R + 6.15f, d.Y + 2.2f) + Tangent(a) * (b * 0.3f), Quaternion.LookRotation(o),
                        new Vector3(0.08f, 1.6f, 0.08f), mMetal, false);
                if (k % 2 == 0)
                {
                    // toxic slime pouring out and flowing to the channel
                    Geo.Prim(PrimitiveType.Cylinder, ring, "SlimeFall", Geo.Polar(a, d.R + 6.05f, d.Y + 1.0f), new Vector3(0.5f, 1.05f, 0.5f), mSlime);
                    Geo.TBox(ring, "SlimeFlow", Geo.Polar(a, d.R + 4.1f, d.Y + 0.02f), Quaternion.LookRotation(o), new Vector3(0.8f, 0.04f, 4.1f), mWater, false);
                    Geo.PointLight(ring, Geo.Polar(a, d.R + 5.2f, d.Y + 0.8f), Mat.Hex("#7CFF3A"), 6f, 2f);
                }
            }

            BuildFeatures(i, ring);

            // scattered props (barrels, crates) against the walls
            int props = new[] { 12, 9, 5 }[i];
            for (int k = 0; k < props; k++)
            {
                float a = (float)rnd.NextDouble() * 360f;
                if (NearOpening(i, a, 10f) || (i == 0 && NearSpawn(a, 6f))) continue;
                float u = (rnd.NextDouble() > 0.5 ? 1 : -1) * (5.6f - (float)rnd.NextDouble() * 0.6f);
                Vector3 p = Geo.Polar(a, d.R + u, d.Y);
                if (rnd.NextDouble() < 0.55) Barrel(ring, p);
                else Crate(ring, p, Quaternion.Euler(0, (float)rnd.NextDouble() * 360, 0));
            }
        }

        List<(float, float)> ArcsBetween(int i, float meters)
        {
            var d = Rings[i];
            var cuts = new List<float>();
            foreach (var (ra, _) in d.Rooms) cuts.Add(ra);
            foreach (float ar in d.Arrivals) cuts.Add(ar);
            cuts.Sort();
            var res = new List<(float, float)>();
            float half = meters / d.R * Mathf.Rad2Deg;
            for (int k = 0; k < cuts.Count; k++)
            {
                float a0 = cuts[k] + half, a1 = (k + 1 < cuts.Count ? cuts[k + 1] : cuts[0] + 360) - half;
                if (a1 > a0) res.Add((a0, a1));
            }
            if (cuts.Count == 0) res.Add((0, 360));
            return res;
        }

        static Vector3 Tangent(float a) => new Vector3(-Mathf.Sin(a * Mathf.Deg2Rad), 0, Mathf.Cos(a * Mathf.Deg2Rad));

        /// <summary>Stone frame + cartoon arch around a round opening. archSide: +1 if rot.forward faces the open space.</summary>
        void Frame(Transform parent, Vector3 basePos, Quaternion rot, float archSide = 1f)
        {
            Vector3 L(float x, float y, float z) => basePos + rot * new Vector3(x, y, z);
            float px = PassR + 1.3f;
            Geo.TBox(parent, "FramePillar", L(-px, 3.5f, 0), rot, new Vector3(2.6f, 7f, 2.2f), mChamber);
            Geo.TBox(parent, "FramePillar", L(px, 3.5f, 0), rot, new Vector3(2.6f, 7f, 2.2f), mChamber);
            Geo.TBox(parent, "FrameLintel", L(0, 5.8f, 0), rot, new Vector3(2 * px + 2.6f, 1.8f, 2.2f), mChamber);
            Geo.TBox(parent, "FrameStep", L(0, -0.2f, 0), rot, new Vector3(2 * px + 2.6f, 0.4f, 2.2f), mConcrete);
            // cartoon arch of chunky stone blocks around the round opening
            float rr = PassR + 0.6f;
            const int blocks = 11;
            for (int k = 0; k < blocks; k++)
            {
                float t = Mathf.Lerp(-28f, 208f, (k + 0.5f) / blocks) * Mathf.Deg2Rad;
                Vector3 lp = new Vector3(Mathf.Cos(t) * rr, PassC + Mathf.Sin(t) * rr, 0.55f * archSide);
                if (lp.y < 0.2f) continue;
                Quaternion br = rot * Quaternion.Euler(0, 0, t * Mathf.Rad2Deg - 90f);
                Geo.TBox(parent, "ArchStone", basePos + rot * lp, br, new Vector3(1.35f, 1.15f, 2.6f), mChamber);
            }
        }

        // ================================================================ rooms
        void BuildRoom(RoomInfo info)
        {
            var d = Rings[info.Level];
            SetRingMats(info.Level);
            float RR = d.RoomRadius, y = d.Y, angle = info.Angle;
            Vector3 C = info.Center, o = Geo.Out(angle);
            var room = new GameObject($"Room_L{info.Level + 1}_{angle:0}_{info.Kind}").transform;
            room.SetParent(root, false);
            bool gate = info.Kind == RoomKind.Gate;

            // chamber
            var wall = MeshKit.CylinderWall("ChamberWall", C, RR, y, RoomH, 2f, new List<(float, float, float)> { (angle, HoleHalf, HoleTop) });
            MeshKit.MeshObject(room, "ChamberWall", wall, new[] { mBrick }, true);
            var floor = MeshKit.Disk("ChamberFloor", C, RR + 0.8f, 0.5f, true, gate ? C : (Vector3?)null, ShaftR);
            MeshKit.MeshObject(room, "ChamberFloor", floor, new[] { mTiles }, true);
            var ceil = MeshKit.Disk("ChamberCeil", C + Vector3.up * RoomH, RR + 0.8f, 1f, false);
            MeshKit.MeshObject(room, "ChamberCeil", ceil, new[] { mConcrete }, false);
            for (int k = 0; k < 8; k++)
            {
                float a = angle + 22.5f + k * 45f;
                if (Mathf.Abs(Mathf.DeltaAngle(a, angle)) < 30) continue;
                Geo.TBox(room, "Rib", C + Geo.Out(a) * (RR - 0.35f) + Vector3.up * RoomH / 2, Quaternion.LookRotation(Geo.Out(a)), new Vector3(1.4f, RoomH, 0.9f), mChamber);
            }
            Geo.PointLight(room, C + Vector3.up * (RoomH - 2f), gate ? Mat.Hex("#FF9A6B") : Mat.Hex("#8FD3FF"), RR * 2.6f, 9f);
            Geo.TBox(room, "LampGlow", C + Vector3.up * (RoomH - 0.3f), Quaternion.identity, new Vector3(1.6f, 0.2f, 1.6f), mLamp, false);

            // connector passage room <-> ring
            float start = d.R - WallU, end = d.RoomR + RR - 0.5f;
            var pass = MeshKit.SweepStraight("Connector", MeshKit.PassageProfile(PassR, PassC, 16), Geo.Polar(angle, start, y), -o, start - end, 2f, 2);
            MeshKit.MeshObject(room, "Connector", pass, new[] { mBrick, mConcrete }, true);
            Frame(room, C + o * (RR + 0.3f), Quaternion.LookRotation(o), -1f);

            // props in the chamber
            int props = 2 + rnd.Next(3);
            for (int k = 0; k < props; k++)
            {
                float a = angle + 60 + (float)rnd.NextDouble() * 240f;
                Vector3 p = C + Geo.Out(a) * (RR - 1.5f);
                if (rnd.NextDouble() < 0.5) Barrel(room, p); else Crate(room, p, Quaternion.Euler(0, a, 0));
            }

            if (gate)
            {
                // hatch over the shaft — opens when the room's boss dies
                var hatch = Geo.Prim(PrimitiveType.Cylinder, room, info.HatchName, C + Vector3.up * 0.06f, new Vector3(ShaftR * 2 + 0.8f, 0.06f, ShaftR * 2 + 0.8f), mMetal);
                var bc = hatch.AddComponent<BoxCollider>();
                bc.size = new Vector3(1f, 2f, 1f);
                for (int b = -3; b <= 3; b++)
                    Geo.TBox(room, "Bar", C + Vector3.up * 0.18f + Quaternion.LookRotation(o) * new Vector3(b * 0.9f, 0, 0), Quaternion.LookRotation(o),
                        new Vector3(0.18f, 0.18f, 7.4f - Mathf.Abs(b) * 0.7f), mRust, false).transform.SetParent(hatch.transform, true);
                info.Gate = hatch.AddComponent<Gate>();

                bool toArena = info.Level == Rings.Length - 1;
                float lowY = toArena ? ArenaY + ArenaH : Rings[info.Level + 1].Y;
                // the opening must fit INSIDE the round landing passage, otherwise there are gaps to the outside
                var gaps = toArena ? null : new List<(float, float, float)> { (angle + 180f, 1.9f, 3.9f) };
                var shaft = MeshKit.CylinderWall("Shaft", C, ShaftR, lowY, y - lowY, 0.75f, gaps, true, 0.75f);
                MeshKit.MeshObject(room, "Shaft", shaft, new[] { mBrick }, true);
                for (float hy = lowY + 1; hy < y - 0.5f; hy += 0.6f)
                    Geo.TBox(room, "Rung", new Vector3(C.x, hy, C.z) + o * (ShaftR - 0.25f), Quaternion.LookRotation(o), new Vector3(0.9f, 0.08f, 0.08f), mRust, false);

                if (!toArena)
                {
                    var nd = Rings[info.Level + 1];
                    MeshKit.MeshObject(room, "ShaftFloor", MeshKit.Disk("ShaftFloor", new Vector3(C.x, lowY, C.z), ShaftR + 0.6f, 0.5f, true), new[] { mConcrete }, true);
                    float s = d.RoomR - ShaftR + 0.3f, e = nd.R + WallU;
                    var land = MeshKit.SweepStraight("Landing", MeshKit.PassageProfile(PassR, PassC, 16), Geo.Polar(angle, s, lowY), -o, s - e, 2f, 2);
                    MeshKit.MeshObject(room, "Landing", land, new[] { mBrick, mConcrete }, true);
                    // stone frame where the landing tunnel meets the shaft: hides the paper-thin wall edge and the gap to the void
                    {
                        Vector3 inDir = -o, sideV = Vector3.Cross(Vector3.up, inDir);
                        Vector3 J = new Vector3(C.x, lowY, C.z) + inDir * (ShaftR - 0.35f);
                        Quaternion fr = Quaternion.LookRotation(inDir);
                        foreach (float sg in new[] { -1f, 1f })
                            Geo.TBox(room, "ShaftJamb", J + sideV * sg * 2.15f + Vector3.up * 2.05f, fr, new Vector3(0.65f, 4.1f, 1.3f), mChamber);
                        Geo.TBox(room, "ShaftLintel", J + Vector3.up * 4.35f, fr, new Vector3(4.95f, 0.6f, 1.3f), mChamber);
                    }
                    Geo.PointLight(room, Geo.Polar(angle, (s + e) / 2, lowY + 4f), Mat.Hex("#CFE6A0"), 14f, 4f);
                    Geo.PointLight(room, new Vector3(C.x, lowY + 3f, C.z), Mat.Hex("#CFE6A0"), 10f, 3f);
                }
            }
        }

        // ================================================================ arena
        void BuildArena()
        {
            SetRingMats(3);
            var a = new GameObject("CrownArena").transform;
            a.SetParent(root, false);
            Vector3 c = new Vector3(0, ArenaY, 0);
            MeshKit.MeshObject(a, "ArenaFloor", MeshKit.Disk("ArenaFloor", c, ArenaR + 0.8f, 1f, true), new[] { mTiles }, true);
            var holes = new List<(Vector3, float)>();
            foreach (var r in Rooms) if (r.Level == Rings.Length - 1 && r.Kind == RoomKind.Gate) holes.Add((r.Center, ShaftR));
            MeshKit.MeshObject(a, "ArenaCeil", MeshKit.Disk("ArenaCeil", c + Vector3.up * ArenaH, ArenaR + 0.8f, 1f, false, holes), new[] { mConcrete }, false);
            MeshKit.MeshObject(a, "ArenaWall", MeshKit.CylinderWall("ArenaWall", c, ArenaR, ArenaY, ArenaH, 2.5f, null), new[] { mBrick }, true);
            for (int k = 0; k < 12; k++)
            {
                float ang = k * 30f + 15f;
                Geo.TBox(a, "Column", c + Geo.Out(ang) * (ArenaR - 0.8f) + Vector3.up * ArenaH / 2, Quaternion.LookRotation(Geo.Out(ang)), new Vector3(2f, ArenaH, 1.6f), mChamber);
                Geo.PointLight(a, c + Geo.Out(ang) * (ArenaR - 3f) + Vector3.up * 6f, Mat.Hex("#B07CFF"), 16f, 5f);
            }
            var moat = MeshKit.RingStrip("Moat", 11f, ArenaY + 0.03f, 2f, 0, 360, 2f);
            MeshKit.MeshObject(a, "Moat", moat, new[] { mWater }, false).AddComponent<WaterFlow>();
            Geo.TBox(a, "Pedestal", c + Vector3.up * 0.2f, Quaternion.identity, new Vector3(4f, 0.4f, 4f), mChamber);
            Geo.TBox(a, "PedestalTop", c + Vector3.up * 0.44f, Quaternion.identity, new Vector3(4.6f, 0.08f, 4.6f), Mat.Get(Mat.Hex("#B89A54"), 0.6f, Mat.Hex("#2A1E00")));
            Geo.PointLight(a, c + Vector3.up * 9f, Mat.Hex("#FFD36B"), 40f, 14f);
            for (int k = 0; k < 6; k++) Barrel(a, c + Geo.Out(k * 60 + 10) * (ArenaR - 3.5f));
        }

        // ================================================================ props
        void Barrel(Transform parent, Vector3 p)
        {
            var tints = new[] { "#B5532B", "#3B6E8F", "#6F7D3A", "#8C8C84" };
            var m = Mat.Tex("metal", 1.5f, 0.4f, Mat.Hex(tints[rnd.Next(tints.Length)]), 0.4f);
            var g = Geo.Prim(PrimitiveType.Cylinder, parent, "Barrel", p + Vector3.up * 0.6f, new Vector3(0.9f, 0.6f, 0.9f), m, true);
            g.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0);
            Geo.Prim(PrimitiveType.Cylinder, g.transform, "Rim", new Vector3(0, 0.55f, 0), new Vector3(1.04f, 0.04f, 1.04f), mMetal);
            Geo.Prim(PrimitiveType.Cylinder, g.transform, "Rim", new Vector3(0, -0.55f, 0), new Vector3(1.04f, 0.04f, 1.04f), mMetal);
            Geo.SetLayer(g, Geo.CamIgnoreLayer);
        }

        void Crate(Transform parent, Vector3 p, Quaternion rot)
        {
            float s = 0.9f + (float)rnd.NextDouble() * 0.5f;
            Geo.SetLayer(Geo.TBox(parent, "Crate", p + Vector3.up * s / 2, rot, Vector3.one * s, mWood), Geo.CamIgnoreLayer);
            if (rnd.NextDouble() < 0.4)
            {
                float s2 = s * 0.7f;
                Geo.SetLayer(Geo.TBox(parent, "Crate", p + Vector3.up * (s + s2 / 2), rot * Quaternion.Euler(0, 25, 0), Vector3.one * s2, mWood), Geo.CamIgnoreLayer);
            }
        }

        /// <summary>Which ring/arena level a height belongs to (0..3).</summary>
        public static int LevelOf(float y)
        {
            if (y > -12) return 0;
            if (y > -37) return 1;
            if (y > -62) return 2;
            return 3;
        }
    }
}
