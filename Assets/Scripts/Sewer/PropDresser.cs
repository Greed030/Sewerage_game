using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>
    /// Fills the sewer with Y2K life: trash piles, hangout corners, skater spots, glowing mushrooms,
    /// graffiti / posters / stickers / neon on the walls, fairy lights, rats, floating trash and drips.
    /// </summary>
    public static class PropDresser
    {
        const float Rt = LevelBuilder.TunnelR, C = LevelBuilder.TunnelC;
        static System.Random rnd;
        static Transform root;
        static Material wireMat, dripMat;
        static Material[] bulbMats;

        static readonly string[] SmallTrash =
        {
            "Trash_BananaPeel", "Trash_Can_Cola", "Trash_Can_Fizz", "Trash_Can_Blast", "Trash_Bottle", "Trash_PizzaBox", "Trash_Newspaper",
            "Trash_CD", "Trash_Cassette", "Trash_FlipPhone", "Tech_Handheld", "Street_Sneaker", "Trash_Can_Cola", "Trash_BananaPeel",
            "Trash_ChipBag", "Trash_Cup", "Trash_NoodleBox", "Trash_PaperBall", "Trash_ChipBag", "Trash_PaperBall", "Trash_Cup", "Trash_VHSBox",
        };
        static readonly string[] OldGraffiti = { "graffiti_y2k", "graffiti_kingz", "graffiti_rat", "graffiti_boom", "graffiti_2000", "graffiti_glitch", "graffiti_crown", "graffiti_stayweird" };
        static readonly string[] Pieces = { "graffiti_skratch", "graffiti_vybez", "graffiti_krown", "graffiti_noize", "graffiti_slime", "graffiti_ghost", "graffiti_funk" };
        static readonly string[] Graffiti = Pieces;
        static readonly string[] Throwups = { "throwup_rk", "throwup_zx", "throwup_ok", "throwup_2k" };
        static readonly string[] Tags = { "tag_dmtr", "tag_kaos", "tag_rekt", "tag_sk8", "tag_vibe", "tag_boo", "tag_nr1" };
        static readonly string[] Stencils = { "stencil_rat", "stencil_crown", "stencil_smile" };
        static readonly string[] Signs = { "Wall_Sign_Danger", "Wall_Sign_NoEntry", "Wall_Sign_Sector", "Wall_Sign_Exit" };
        static readonly string[] Posters = { "poster_rave", "poster_cyber", "poster_lostrat", "poster_ratking", "poster_sewerfm", "poster_pizza" };
        static readonly string[] Stickers = { "sticker_smiley", "sticker_star", "sticker_heart", "sticker_alien", "sticker_flame", "sticker_y2k", "sticker_cherry", "sticker_butterfly" };
        static readonly (string name, string col)[] Neons =
        {
            ("neon_open", "#FF3EB5"), ("neon_pizza", "#FFB23E"), ("neon_y2k", "#3EF4FF"), ("neon_noswim", "#B6FF3C"),
            ("neon_heart", "#FF2E63"), ("neon_star", "#FFE45C"), ("neon_thisway", "#3EF4FF"),
        };
        static readonly string[] Y2KColors = { "#FF4FD8", "#3EF4FF", "#FFE45C", "#B6FF3C", "#B46BFF" };

        // ------------------------------------------------------------ helpers
        static float R() => (float)rnd.NextDouble();
        static float Rng(float a, float b) => a + (b - a) * R();
        static bool Chance(float p) => R() < p;
        static T Pick<T>(T[] a) => a[rnd.Next(a.Length)];
        static Vector3 Tan(float a) => new Vector3(-Mathf.Sin(a * Mathf.Deg2Rad), 0, Mathf.Cos(a * Mathf.Deg2Rad));
        static Vector3 P(LevelBuilder.RingDef d, float a, float u, float v) => Geo.Polar(a, d.R + u, d.Y + v);
        static float WallU(float v) => Mathf.Sqrt(Mathf.Max(0, Rt * Rt - (v - C) * (v - C)));
        static Quaternion Yaw() => Quaternion.Euler(0, Rng(0, 360), 0);

        /// <summary>Global size boost for all dressing props (cartoon-chunky). Rats stay rat-sized.</summary>
        const float Big = 1.55f, DecalBig = 1.45f;
        /// <summary>Keep the player spawn points clear (big solid props used to box the player in).</summary>
        static LevelBuilder level;
        static bool NearSpawn(Vector3 pos)
        {
            if (level == null) return false;
            foreach (var sp in level.Spawns) { var d = pos - sp; d.y = 0; if (d.sqrMagnitude < 6.5f * 6.5f && Mathf.Abs(pos.y - sp.y) < 4f) return true; }
            return false;
        }

        static GameObject Prop(string name, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            if (NearSpawn(pos)) return null;
            return PropLib.Spawn(name, root, pos, rot, name == "Critter_Rat" ? scale : scale * Big);
        }

        static bool NearOpen(LevelBuilder.RingDef d, float a, float meters)
        {
            if (level != null && level.NearFeature(LevelBuilder.RingIndex(d), a, meters)) return true;
            foreach (var (ra, _) in d.Rooms) if (Mathf.Abs(Mathf.DeltaAngle(a, ra)) * Mathf.Deg2Rad * d.R < meters) return true;
            foreach (float ar in d.Arrivals) if (Mathf.Abs(Mathf.DeltaAngle(a, ar)) * Mathf.Deg2Rad * d.R < meters) return true;
            return false;
        }

        static bool NearSpawn(float a, float meters)
        {
            foreach (float s in new[] { 45f, 135f, 225f, 315f })
                if (Mathf.Abs(Mathf.DeltaAngle(a, s)) * Mathf.Deg2Rad * LevelBuilder.Rings[0].R < meters) return true;
            return false;
        }

        // ------------------------------------------------------------ entry
        public static Transform Dress(LevelBuilder lb, Transform levelRoot, int seed = 2000)
        {
            rnd = new System.Random(seed);
            level = lb;
            root = new GameObject("Dressing").transform;
            root.SetParent(levelRoot, false);
            wireMat = Mat.Get(Mat.Hex("#15151A"), 0.3f);
            bulbMats = new Material[Y2KColors.Length];
            for (int i = 0; i < Y2KColors.Length; i++) bulbMats[i] = Mat.Emissive(Mat.Hex(Y2KColors[i]), 5f);
            var ps = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (ps != null)
            {
                dripMat = new Material(ps) { name = "Drip" };
                dripMat.SetColor("_BaseColor", new Color(0.65f, 0.85f, 0.75f, 0.85f));
            }

            for (int i = 0; i < LevelBuilder.Rings.Length; i++) DressRing(i);
            foreach (var r in lb.Rooms)
            {
                if (r.Kind == RoomKind.Optional) DressLair(r);
                else if (r.Kind == RoomKind.Gate) DressGateRoom(r);
            }
            DressArena(lb);
            return root;
        }

        // ------------------------------------------------------------ rings
        static void DressRing(int i)
        {
            var d = LevelBuilder.Rings[i];
            float circ = 2 * Mathf.PI * d.R, step = 7f;
            int steps = Mathf.FloorToInt(circ / step);
            for (int k = 0; k < steps; k++)
            {
                float a = (k + 0.5f) * 360f / steps + Rng(-1f, 1f);
                if (NearOpen(d, a, 11f)) continue;
                if (i == 0 && NearSpawn(a, 7f)) continue;
                float side = Chance(0.5f) ? 1 : -1;

                float roll = R();
                if (roll < 0.20f) TrashScatter(d, a, side);
                else if (roll < 0.25f) Hangout(d, a, side);
                else if (roll < 0.35f) BigJunk(d, a, side);
                else if (roll < 0.38f) Skater(d, a, side);
                else if (roll < 0.44f) Mushrooms(d, a, side);
                else if (roll < 0.46f) Party(d, a, side);
                else if (roll < 0.52f) Laundry(d, a, side);
                else if (roll < 0.56f) Bathroom(d, a, side);
                else if (roll < 0.62f) StreetCorner(d, a, side);
                else if (roll < 0.66f) VendingSpot(d, a, side);
                else if (roll < 0.69f) SkatePark(d, a, side);
                else if (roll < 0.74f) DrumPile(d, a, side);
                else if (roll < 0.84f) LostStuff(d, a, side);

                // litter everywhere
                int litter = 3 + rnd.Next(5);
                for (int t = 0; t < litter; t++)
                {
                    float lu = (Chance(0.5f) ? 1 : -1) * Rng(2.5f, 5.6f);
                    Prop(Pick(SmallTrash), P(d, a + Rng(-3.4f, 3.4f) / d.R * Mathf.Rad2Deg, lu, 0), Yaw(), Rng(0.9f, 1.3f));
                }
                if (Chance(0.22f)) Prop("Trash_Pile", Around(d, a + 2.5f / d.R * Mathf.Rad2Deg, side * 5.3f, 0, 0), Yaw(), Rng(0.9f, 1.2f));
                if (Chance(0.18f)) Prop(Chance(0.5f) ? "Trash_Bag_Black" : "Trash_Bag_Blue", Around(d, a - 2f / d.R * Mathf.Rad2Deg, -side * 5.3f, 0, 0), Yaw(), Rng(0.9f, 1.2f));
                if (Chance(0.25f)) FloorDecal(P(d, a + Rng(-3, 3) / d.R * Mathf.Rad2Deg, side * Rng(2.8f, 5.2f), 0.013f), Tan(a), Chance(0.5f) ? "puddle_a" : "slime_b", Rng(1.0f, 2.2f));

                // wall-mounted stuff on the opposite wall (and sometimes both)
                if (Chance(0.75f)) WallStuff(d, a + Rng(-1.5f, 1.5f) / d.R * Mathf.Rad2Deg, -side);
                if (Chance(0.3f)) WallStuff(d, a + 3.2f / d.R * Mathf.Rad2Deg, side);

                // walls
                float w = R(), ws = Chance(0.5f) ? 1 : -1;
                if (w < 0.62f) GraffitiWall(d, a, ws);
                else if (w < 0.70f)
                {
                    int n = 1 + rnd.Next(3);
                    for (int p = 0; p < n; p++) WallDecal(d, a + (p - (n - 1) / 2f) * 1.25f / d.R * Mathf.Rad2Deg, ws, Rng(1.9f, 2.2f), Pick(Posters), new Vector2(1f, 1.5f));
                }
                else if (w < 0.78f)
                {
                    int n = 4 + rnd.Next(4);
                    for (int s = 0; s < n; s++) WallDecal(d, a + Rng(-1.6f, 1.6f) / d.R * Mathf.Rad2Deg, ws, Rng(0.8f, 3f), Pick(Stickers), Vector2.one * Rng(0.28f, 0.45f));
                }
                else if (w < 0.84f) NeonSign(d, a, ws);

                // floor
                if (Chance(0.16f)) FloorDecal(P(d, a + Rng(-2, 2) / d.R * Mathf.Rad2Deg, side * Rng(2.8f, 5.4f), 0.012f), Tan(a), Chance(0.5f) ? "puddle_a" : "puddle_b", Rng(1.2f, 2.6f));
                if (Chance(0.07f)) FloorDecal(P(d, a, side * Rng(2.2f, 2.8f), 0.014f), Tan(a), Chance(0.5f) ? "slime_a" : "slime_b", Rng(0.8f, 1.6f));
                if (Chance(0.025f)) FloorDecal(P(d, a, side * 4.3f, 0.016f), Tan(a), "chalk_hopscotch", 3.4f, 1.7f);
            }

            // fairy lights across the vault
            int strings = Mathf.FloorToInt(circ / 45f);
            for (int k = 0; k < strings; k++)
            {
                float a = (k + 0.25f) * 360f / strings;
                if (NearOpen(d, a, 6f)) continue;
                FairyString(P(d, a, -5.3f, 8.0f), P(d, a + 2f / d.R * Mathf.Rad2Deg, 5.3f, 8.2f), 1.5f, 14);
            }
            // drips
            int drips = new[] { 22, 16, 10 }[i];
            for (int k = 0; k < drips; k++)
            {
                float a = R() * 360f, u = Rng(-4f, 4f);
                Drip(P(d, a, u, C + Mathf.Sqrt(Rt * Rt - u * u) - 0.25f));
            }
            // rats
            int rats = new[] { 9, 7, 4 }[i];
            for (int k = 0; k < rats; k++)
            {
                float a = R() * 360f, u = (Chance(0.5f) ? 1 : -1) * 6.0f;
                var g = Prop("Critter_Rat", P(d, a, u, 0), Quaternion.identity, Rng(1.1f, 1.5f));
                if (!g) continue;
                var rc = g.AddComponent<RatCritter>(); rc.RingR = d.R; rc.Y = d.Y; rc.Angle = a; rc.U = u;
            }
            // floating trash in the channel
            int floats = new[] { 10, 8, 5 }[i];
            string[] fl = { "Deco_RubberDuck", "Deco_RubberDuck", "Trash_Bottle", "Trash_Can_Fizz", "Trash_Can_Cola", "Deco_RubberDuck" };
            for (int k = 0; k < floats; k++)
            {
                float a = R() * 360f;
                string n = Pick(fl);
                var rot = n == "Deco_RubberDuck" ? Quaternion.identity : Quaternion.Euler(90, 0, 0);
                var g = Prop(n, P(d, a, 0, -0.17f), rot, n == "Deco_RubberDuck" ? Rng(1.5f, 2.6f) : 1.3f);
                if (!g) continue;
                var fd = g.AddComponent<FloatDrift>(); fd.RingR = d.R; fd.Y = d.Y - (n == "Deco_RubberDuck" ? 0.2f : 0.15f); fd.Angle = a; fd.Offset = Rng(-1.1f, 1.1f); fd.Speed = Rng(0.35f, 0.7f);
            }
        }

        // ------------------------------------------------------------ ring scenes
        static Vector3 Around(LevelBuilder.RingDef d, float a, float u, float alongM, float radialM)
        {
            float uu = Mathf.Clamp(u + radialM, -5.5f, 5.5f);
            if (Mathf.Abs(uu) < 2.4f) uu = Mathf.Sign(u) * 2.4f;
            return P(d, a + alongM / d.R * Mathf.Rad2Deg, uu, 0);
        }

        static void SmallTrashAt(Vector3 p)
        {
            string n = Pick(SmallTrash);
            var rot = Yaw();
            if (n.StartsWith("Trash_Can") || n == "Trash_Bottle")
                if (Chance(0.6f)) { rot *= Quaternion.Euler(90, 0, 0); p += Vector3.up * (n == "Trash_Bottle" ? 0.04f : 0.033f); }
            Prop(n, p, rot, n.Contains("Phone") || n.Contains("Handheld") || n == "Trash_CD" || n == "Trash_Cassette" ? 1.6f : 1.2f);
        }

        static void TrashScatter(LevelBuilder.RingDef d, float a, float side)
        {
            float u = side * Rng(3f, 5.4f);
            int n = 4 + rnd.Next(6);
            for (int k = 0; k < n; k++) SmallTrashAt(Around(d, a, u, Rng(-1.8f, 1.8f), Rng(-0.9f, 0.9f)));
            if (Chance(0.45f))
            {
                int bags = 1 + rnd.Next(3);
                for (int b = 0; b < bags; b++) Prop(Chance(0.65f) ? "Trash_Bag_Black" : "Trash_Bag_Blue", Around(d, a, side * 5.8f, Rng(-1.2f, 1.2f), 0), Yaw(), Rng(0.9f, 1.25f));
            }
        }

        static void Hangout(LevelBuilder.RingDef d, float a, float side)
        {
            Vector3 inward = -Geo.Out(a) * side;
            Prop("Furn_Rug", P(d, a, side * 4.4f, 0), Yaw(), Rng(1.1f, 1.4f));
            Prop("Furn_Couch", P(d, a, side * 5.95f, 0), Quaternion.LookRotation(inward));
            Prop("Furn_LavaLamp", P(d, a + 1.4f / d.R * Mathf.Rad2Deg, side * 6.05f, 0), Yaw(), 1.3f);
            AddLight(P(d, a + 1.4f / d.R * Mathf.Rad2Deg, side * 5.8f, 0.8f), Mat.Hex("#FF4FD8"), 5f, 1.8f);
            Prop("Tech_CRT", P(d, a, side * 2.9f, 0), Quaternion.LookRotation(-inward));
            Prop("Trash_PizzaBoxOpen", P(d, a - 0.6f / d.R * Mathf.Rad2Deg, side * 4.3f, 0.02f), Yaw(), 1.2f);
            if (Chance(0.6f)) Prop("Furn_BeanBag", P(d, a - 2.1f / d.R * Mathf.Rad2Deg, side * 4.6f, 0), Yaw());
            if (Chance(0.7f)) Prop("Tech_Boombox", P(d, a - 1.6f / d.R * Mathf.Rad2Deg, side * 6.0f, 0), Quaternion.LookRotation(inward));
            for (int k = 0; k < 3; k++) SmallTrashAt(Around(d, a, side * 4.2f, Rng(-1.6f, 1.6f), Rng(-0.7f, 0.7f)));
        }

        static void BigJunk(LevelBuilder.RingDef d, float a, float side)
        {
            Vector3 inward = -Geo.Out(a) * side;
            Quaternion face = Quaternion.LookRotation(inward) * Quaternion.Euler(0, Rng(-18, 18), 0);
            switch (rnd.Next(11))
            {
                case 0: Prop("Furn_Couch", P(d, a, side * 5.95f, 0), face * Quaternion.Euler(0, 0, Rng(-4, 4))); break;
                case 1: Prop("Furn_Mattress", P(d, a, side * 4.8f, 0), Quaternion.LookRotation(Tan(a)) * Quaternion.Euler(0, Rng(-15, 15), 0)); break;
                case 2: Prop("Furn_Armchair", P(d, a, side * 5.6f, 0), face); break;
                case 3:
                    Prop("Tech_Arcade", P(d, a, side * 6.05f, 0), Quaternion.LookRotation(inward));
                    AddLight(P(d, a, side * 5.4f, 1.4f), Mat.Hex("#3EF4FF"), 4f, 1.5f);
                    break;
                case 4: Prop("Street_Cart", P(d, a, side * Rng(4f, 5.6f), 0), Chance(0.35f) ? Yaw() * Quaternion.Euler(0, 0, 90) : Yaw()); break;
                case 5: Prop("Trash_TireStack", P(d, a, side * 5.7f, 0), Yaw()); Prop("Trash_Tire", Around(d, a, side * 5.2f, 1.2f, 0), Yaw()); break;
                case 6: Prop("Furn_Pallet", P(d, a, side * 5.0f, 0), Yaw()); if (Chance(0.5f)) Prop("Furn_Pallet", P(d, a, side * 5.0f, 0.15f), Yaw()); break;
                case 7: Prop("Street_Barrier", P(d, a, side * 4.6f, 0), Quaternion.LookRotation(inward) * Quaternion.Euler(0, Rng(-25, 25), 0)); break;
                case 8:
                    Prop("Trash_Cardboard", P(d, a, side * 5.7f, 0), Yaw());
                    if (Chance(0.6f)) Prop("Trash_Cardboard", P(d, a, side * 5.7f, 0.44f), Yaw(), 0.8f);
                    Prop("Trash_Cardboard", Around(d, a, side * 5.5f, 0.8f, 0), Yaw(), 0.9f);
                    break;
                case 9: Prop("Furn_Planks", P(d, a, side * 4.5f, 0), Yaw()); break;
                default:
                    for (int b = 0; b < 3; b++) Prop(Chance(0.6f) ? "Trash_Bag_Black" : "Trash_Bag_Blue", Around(d, a, side * 5.7f, Rng(-1.3f, 1.3f), Rng(-0.4f, 0)), Yaw(), Rng(0.9f, 1.3f));
                    break;
            }
            for (int k = 0; k < 2; k++) SmallTrashAt(Around(d, a, side * 4.6f, Rng(-2f, 2f), Rng(-1f, 1f)));
        }

        static void Skater(LevelBuilder.RingDef d, float a, float side)
        {
            for (int k = 0; k < 2 + rnd.Next(2); k++) Prop("Street_Cone", P(d, a + (k * 1.4f - 1.4f) / d.R * Mathf.Rad2Deg, side * 3.6f, 0), Yaw());
            Prop("Street_Skateboard", Around(d, a, side * 4.6f, Rng(-1, 1), 0), Yaw(), 1.2f);
            Prop("Street_Sneaker", Around(d, a, side * 5.2f, Rng(-1.5f, 1.5f), 0), Yaw(), 1.4f);
            if (Chance(0.5f)) Prop("Tech_Boombox", P(d, a + 2f / d.R * Mathf.Rad2Deg, side * 6.0f, 0), Quaternion.LookRotation(-Geo.Out(a) * side));
            WallDecal(d, a, side, Rng(2f, 2.8f), Pick(Graffiti), new Vector2(4.4f, 2.2f));
        }

        static void Mushrooms(LevelBuilder.RingDef d, float a, float side)
        {
            int n = 1 + rnd.Next(3);
            string col = Pick(new[] { "#3EF4FF", "#FF4FD8", "#B6FF3C" });
            Vector3 c = Vector3.zero;
            for (int k = 0; k < n; k++)
            {
                Vector3 p = Around(d, a, side * Rng(2.6f, 5.9f), Rng(-1.5f, 1.5f), 0);
                c += p;
                Prop("Deco_Mushrooms", p, Yaw(), Rng(0.8f, 1.8f));
            }
            AddLight(c / n + Vector3.up * 0.6f, Mat.Hex(col), 4.5f, 1.6f);
        }

        static void Party(LevelBuilder.RingDef d, float a, float side)
        {
            Prop("Deco_Balloons", P(d, a, side * 5.9f, 0), Yaw(), Rng(1.1f, 1.5f));
            Prop("Tech_Boombox", P(d, a + 1f / d.R * Mathf.Rad2Deg, side * 5.8f, 0), Quaternion.LookRotation(-Geo.Out(a) * side));
            for (int k = 0; k < 4; k++) SmallTrashAt(Around(d, a, side * 4.8f, Rng(-1.5f, 1.5f), Rng(-0.8f, 0.8f)));
        }

        // ------------------------------------------------------------ walls / floor / lights
        static void WallDecal(LevelBuilder.RingDef d, float a, float side, float v, string tex, Vector2 size, float glow = 0f)
        {
            if (NearOpen(d, a, 4f + size.x * 0.75f)) return;
            size *= DecalBig;
            if (v + size.y * 0.5f > 6.6f) v = 6.6f - size.y * 0.5f;   // keep inside the wall band
            if (v - size.y * 0.5f < 0.15f) v = 0.15f + size.y * 0.5f;
            CurvedDecal(d, a, side, v, tex, size, glow, 0.05f + 0.012f * rnd.Next(5), Rng(-3f, 3f));
        }

        /// <summary>Decal mesh that hugs the round tunnel wall (both curvatures), so big pieces never float or clip.</summary>
        static void CurvedDecal(LevelBuilder.RingDef d, float a, float side, float v, string tex, Vector2 size, float glow, float off, float tiltDeg)
        {
            float rr = Rt - off;
            float phi0 = Mathf.Asin(Mathf.Clamp((v - C) / rr, -0.99f, 0.99f));
            int nx = Mathf.Clamp(Mathf.CeilToInt(size.x / 0.5f), 2, 16), ny = Mathf.Clamp(Mathf.CeilToInt(size.y / 0.5f), 2, 10);
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float tilt = tiltDeg * Mathf.Deg2Rad, ct = Mathf.Cos(tilt), st = Mathf.Sin(tilt);
            for (int y = 0; y <= ny; y++)
                for (int x = 0; x <= nx; x++)
                {
                    float sx = (x / (float)nx - 0.5f) * size.x, sy = (y / (float)ny - 0.5f) * size.y;
                    float px = sx * ct - sy * st, py = sx * st + sy * ct;   // slight rotation on the wall
                    float phi = phi0 + py / rr;
                    float u = rr * Mathf.Cos(phi), vv = C + rr * Mathf.Sin(phi);
                    float ang = a - side * px / (d.R + side * u) * Mathf.Rad2Deg;   // +x = viewer's right on both walls
                    verts.Add(P(d, ang, side * u, vv)); uvs.Add(new Vector2(x / (float)nx, y / (float)ny));
                }
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int i0 = y * (nx + 1) + x, i1 = i0 + 1, i2 = i0 + nx + 1, i3 = i2 + 1;
                    tris.Add(i0); tris.Add(i2); tris.Add(i1); tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            var m = new Mesh { name = "WallDecal_" + tex };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            MeshKit.Created.Add(m);
            var g = new GameObject("Decal_" + tex);
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = PropLib.Decal(tex, glow);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            g.layer = Geo.CamIgnoreLayer;
        }

        /// <summary>Wall-mounted model: back plane tangent to the round wall at height vc (contact height).</summary>
        static GameObject WallProp(LevelBuilder.RingDef d, float a, float side, float baseV, float halfH, string name, float scale = 1f, float push = 0.04f)
        {
            if (!PropLib.Has(name)) return null;
            scale *= Big;
            float vc = baseV + halfH * scale, uw = WallU(vc);
            Vector3 wallPt = P(d, a, side * (uw - push), vc);
            Vector3 n = -(Geo.Out(a) * (side * uw) + Vector3.up * (vc - C)).normalized;  // inward wall normal
            Vector3 upv = Vector3.ProjectOnPlane(Vector3.up, n).normalized;
            var rot = Quaternion.LookRotation(n, upv);
            return PropLib.Spawn(name, root, wallPt - upv * (halfH * scale), rot, scale);
        }

        static Quaternion FaceIn(float a, float side, float jitter = 10f) => Quaternion.LookRotation(-Geo.Out(a) * side) * Quaternion.Euler(0, Rng(-jitter, jitter), 0);

        static void SlimeStream(Vector3 top, float bottomY, float r)
        {
            var g = Geo.Prim(PrimitiveType.Cylinder, root, "SlimeStream", new Vector3(top.x, (top.y + bottomY) / 2, top.z), new Vector3(r * 2, (top.y - bottomY) / 2, r * 2), Mat.Emissive(Mat.Hex("#5CE02A"), 0.7f));
            g.layer = Geo.CamIgnoreLayer;
            var q = PropLib.DecalQuad(root, Chance(0.5f) ? "slime_a" : "slime_b", new Vector3(top.x, bottomY + 0.015f, top.z), Vector3.down, Vector3.forward, Vector2.one * (r * 9f));
            q.transform.Rotate(0, 0, Rng(0, 360), Space.Self);
        }

        // ------------------------------------------------------------ v2: wall props + new scenes
        static void WallStuff(LevelBuilder.RingDef d, float a, float side)
        {
            if (NearOpen(d, a, 9f)) return;
            float roll = R();
            Vector3 inward = -Geo.Out(a) * side;
            if (roll < 0.12f)
            {   // elbow pipe pouring slime into the walkway
                float sc = Rng(1.0f, 1.3f);
                var g = WallProp(d, a, side, 1.0f, 1.2f, "Wall_PipeElbow", sc);
                if (g) { var mouth = g.transform.position + g.transform.forward * 0.85f * sc * Big + g.transform.up * 1.0f * sc * Big; SlimeStream(mouth, d.Y + 0.02f, 0.07f); }
            }
            else if (roll < 0.24f)
            {   // broken leaking stub
                float v = Rng(1.4f, 3.6f);
                var g = WallProp(d, a, side, v, 0.2f, "Wall_PipeStub", Rng(1f, 1.4f));
                if (g) Drip(g.transform.position + g.transform.forward * 0.75f * g.transform.localScale.x + Vector3.up * 0.2f * g.transform.localScale.x);
            }
            else if (roll < 0.32f) WallProp(d, a, side, 0.6f, 0.7f, "Wall_PipeValve", 1.2f);
            else if (roll < 0.42f) { WallProp(d, a, side, 0f, 2.1f, "Wall_PipeVertical", 1.3f); if (Chance(0.5f)) WallProp(d, a + 0.6f / d.R * Mathf.Rad2Deg, side, 0f, 2.1f, "Wall_PipeVertical", 1.3f); }
            else if (roll < 0.52f) WallProp(d, a, side, Rng(3.2f, 4.6f), 0.5f, "Wall_PipeBundle", 1.2f);
            else if (roll < 0.60f) WallProp(d, a, side, 0.2f, 1.1f, "Wall_BoardedPlanks", 1.2f);
            else if (roll < 0.66f) WallProp(d, a, side, 0f, 1.0f, "Wall_PlankLean", 1.1f, 0.25f);
            else if (roll < 0.71f) WallProp(d, a, side, 0f, 1.6f, "Wall_Ladder", 1.2f);
            else if (roll < 0.77f) { WallProp(d, a, side, 0f, 1.0f, "Wall_JunctionBox", 1.2f); AddLight(P(d, a, side * 5.6f, 1.6f), Mat.Hex("#FF2E3A"), 3f, 0.8f); }
            else if (roll < 0.84f) WallProp(d, a, side, Rng(2.4f, 3.2f), 0.3f, Pick(Signs), 1.4f);
            else if (roll < 0.88f) WallProp(d, a, side, 0.4f, 0.5f, "Wall_Vent", 1.4f);
            else if (roll < 0.93f) { var g = WallProp(d, a, side, 4.2f, 0.3f, "Wall_LampCage", 1.3f); if (g) AddLight(g.transform.position + g.transform.forward * 0.6f, Mat.Hex("#FFD27A"), 9f, 2.2f); }
            else if (roll < 0.97f) WallProp(d, a, side, 1.0f, 0.3f, "Wall_Shelf", 1.2f);
            else WallProp(d, a, side, 0.3f, 0.7f, "Wall_Hose", 1.2f);
        }

        static void GraffitiWall(LevelBuilder.RingDef d, float a, float side)
        {
            float w = R();
            if (w < 0.45f) WallDecal(d, a, side, Rng(2.5f, 3.3f), Pick(Pieces), new Vector2(6.4f, 3.2f) * Rng(0.9f, 1.15f));
            else if (w < 0.75f) WallDecal(d, a, side, Rng(1.7f, 2.7f), Pick(Throwups), new Vector2(3.6f, 1.8f) * Rng(0.85f, 1.1f));
            else if (w < 0.85f) WallDecal(d, a, side, Rng(2.0f, 3.0f), Pick(OldGraffiti), new Vector2(4.4f, 2.2f));
            else WallDecal(d, a, side, Rng(1.6f, 2.4f), Pick(Stencils), Vector2.one * Rng(1.0f, 1.5f));
            int tags = rnd.Next(2, 5);
            for (int t = 0; t < tags; t++)
                WallDecal(d, a + Rng(-3.5f, 3.5f) / d.R * Mathf.Rad2Deg, Chance(0.6f) ? side : -side, Rng(0.7f, 4.4f), Pick(Tags), new Vector2(1.8f, 0.9f) * Rng(0.6f, 1.2f));
        }

        static void Laundry(LevelBuilder.RingDef d, float a, float side)
        {
            float u = side * 5.6f;
            Prop("Furn_WashingMachine", Around(d, a, u, -0.9f, 0), FaceIn(a, side, 12));
            Prop("Furn_Fridge", Around(d, a, u, 0.4f, 0), FaceIn(a, side, 8));
            if (Chance(0.5f)) Prop("Furn_TV_Stand", Around(d, a, u, 1.9f, 0), FaceIn(a, side, 15));
            for (int k = 0; k < 2; k++) SmallTrashAt(Around(d, a, side * 4.2f, Rng(-1.5f, 1.5f), 0));
        }
        static void Bathroom(LevelBuilder.RingDef d, float a, float side)
        {
            float u = side * 5.4f;
            Prop("Furn_Bathtub", Around(d, a, u, 0, 0), Quaternion.LookRotation(Tan(a)) * Quaternion.Euler(0, Rng(-8, 8), 0), 1.1f);
            Prop("Furn_Toilet", Around(d, a, u, 1.8f, 0.2f), FaceIn(a, side, 25));
            AddLight(P(d, a, u, 0.8f), Mat.Hex("#7CFF3A"), 4f, 1.2f);
        }
        static void StreetCorner(LevelBuilder.RingDef d, float a, float side)
        {
            float u = side * 5.6f;
            Prop("Street_Hydrant", Around(d, a, u, -1.0f, 0), FaceIn(a, side, 40), 1.1f);
            Prop("Street_StopSign", Around(d, a, u, 0.4f, 0.2f), FaceIn(a, side, 30), 1.1f);
            if (Chance(0.6f)) Prop("Street_Bicycle", Around(d, a, side * 4.4f, 1.6f, 0), Yaw());
            if (Chance(0.5f)) Prop("Street_WetFloor", Around(d, a, side * 3.4f, -0.4f, 0), Yaw(), 1.2f);
        }
        static void VendingSpot(LevelBuilder.RingDef d, float a, float side)
        {
            var g = Prop("Tech_Vending", Around(d, a, side * 5.7f, 0, 0), FaceIn(a, side, 4), 1.1f);
            AddLight(P(d, a, side * 4.6f, 1.4f), Mat.Hex("#7DF9FF"), 6f, 2f);
            for (int k = 0; k < 4; k++) Prop(Pick(new[] { "Trash_Can_Cola", "Trash_Can_Fizz", "Trash_Can_Blast" }), Around(d, a, side * 4.6f, Rng(-1.2f, 1.2f), Rng(-0.6f, 0.6f)), Quaternion.Euler(90, Rng(0, 360), 0), 1.3f);
        }
        static void SkatePark(LevelBuilder.RingDef d, float a, float side)
        {
            Prop("Street_QuarterPipe", Around(d, a, side * 5.9f, 0, 0), FaceIn(a, side, 0));
            Prop("Street_Skateboard", Around(d, a, side * 3.6f, Rng(-1, 1), 0), Yaw(), 1.2f);
            Prop("Trash_PaintCans", Around(d, a, side * 4.4f, 2.2f, 0), Yaw(), 1.2f);
            WallDecal(d, a, side, 3.4f, Pick(Pieces), new Vector2(6.4f, 3.2f));
        }
        static void DrumPile(LevelBuilder.RingDef d, float a, float side)
        {
            int n = 2 + rnd.Next(3);
            for (int k = 0; k < n; k++)
            {
                var p = Around(d, a, side * 5.4f, (k - n / 2f) * 0.75f, Rng(-0.3f, 0.3f));
                if (k == n - 1 && Chance(0.5f)) Prop("Trash_Drum", p + Vector3.up * 0.32f, Quaternion.Euler(90, Rng(0, 360), 0) , 1.1f);
                else Prop("Trash_Drum", p, Yaw(), 1.1f);
            }
            AddLight(P(d, a, side * 5f, 1.4f), Mat.Hex("#7CFF3A"), 4f, 1f);
        }
        static void LostStuff(LevelBuilder.RingDef d, float a, float side)
        {
            string[] pool = { "Trash_Suitcase", "Trash_VHSBox", "Trash_Umbrella", "Deco_Gnome", "Trash_PaintCans", "Furn_OfficeChair" };
            int n = 2 + rnd.Next(2);
            for (int k = 0; k < n; k++) Prop(Pick(pool), Around(d, a, side * Rng(4.4f, 5.6f), Rng(-2f, 2f), 0), Yaw(), Rng(1.0f, 1.3f));
        }

        static void NeonSign(LevelBuilder.RingDef d, float a, float side)
        {
            var (tex, col) = Pick(Neons);
            float v = 4.4f;
            WallDecal(d, a, side, v, tex, new Vector2(2.8f, 1.4f), 2.2f);
            var l = AddLight(P(d, a, side * (WallU(v) - 1.2f), v), Mat.Hex(col), 8f, 3f);
            if (Chance(0.4f)) { var f = l.gameObject.AddComponent<Flicker>(); f.Target = l; f.Base = 3f; }
        }

        static void FloorDecal(Vector3 pos, Vector3 along, string tex, float w, float h = -1)
        {
            if (h < 0) h = w * Rng(0.7f, 1.0f);
            var q = PropLib.DecalQuad(root, tex, pos, Vector3.down, along, new Vector2(w, h));
            q.transform.Rotate(0, 0, Rng(0, 360), Space.Self);
        }

        static Light AddLight(Vector3 pos, Color c, float range, float intensity) => Geo.PointLight(root, pos, c, range, intensity);

        static void FairyString(Vector3 a, Vector3 b, float sag, int bulbs)
        {
            var g = new GameObject("FairyLights").transform;
            g.SetParent(root, false);
            Vector3 prev = a;
            int segs = bulbs * 2;
            for (int k = 1; k <= segs; k++)
            {
                float t = (float)k / segs;
                Vector3 p = Vector3.Lerp(a, b, t) + Vector3.down * sag * 4f * t * (1 - t);
                Vector3 dd = p - prev;
                var w = Geo.Prim(PrimitiveType.Cylinder, g, "Wire", (p + prev) / 2, new Vector3(0.025f, dd.magnitude / 2, 0.025f), wireMat);
                w.transform.rotation = Quaternion.FromToRotation(Vector3.up, dd.normalized);
                if (k % 2 == 1)
                    Geo.Prim(PrimitiveType.Sphere, g, "Bulb", p + Vector3.down * 0.08f, Vector3.one * 0.13f, bulbMats[(k / 2) % bulbMats.Length]);
                prev = p;
            }
            Geo.PointLight(g, Vector3.Lerp(a, b, 0.5f) + Vector3.down * (sag + 0.3f), Mat.Hex(Pick(Y2KColors)), 9f, 2.2f);
            Geo.SetLayer(g.gameObject, Geo.CamIgnoreLayer);
        }

        static void Drip(Vector3 pos)
        {
            if (dripMat == null) return;
            var g = new GameObject("Drip");
            g.transform.SetParent(root, false);
            g.transform.position = pos;
            var ps = g.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = 1.3f; main.startSpeed = 0f; main.startSize = 0.06f;
            main.gravityModifier = 1.1f; main.maxParticles = 12;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = Rng(0.5f, 2.2f);
            var sh = ps.shape; sh.enabled = false;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;
            r.sharedMaterial = dripMat;
        }

        // ------------------------------------------------------------ rooms
        static Vector3 RoomP(RoomInfo r, float ang, float dist, float v = 0) => r.Center + Geo.Out(ang) * dist + Vector3.up * v;

        static void RoomWallDecal(RoomInfo r, float ang, float v, string tex, Vector2 size, float glow = 0)
        {
            float RR = r.Size / 2;
            size *= DecalBig;
            var q = PropLib.DecalQuad(root, tex, RoomP(r, ang, RR - 0.08f, v), Geo.Out(ang), Vector3.up, size, glow);
            q.transform.Rotate(0, 0, Rng(-4f, 4f), Space.Self);
        }

        /// <summary>Optional rooms: cozy Y2K lair of the room's boss.</summary>
        static void DressLair(RoomInfo r)
        {
            float RR = r.Size / 2, a = r.Angle;
            float sideA = a + 90f;
            Vector3 toCenter = -Geo.Out(sideA);
            Prop("Furn_Couch", RoomP(r, sideA, RR - 1.3f), Quaternion.LookRotation(toCenter));
            Prop("Furn_Rug", RoomP(r, sideA, RR - 3.3f), Yaw(), 1.5f);
            Prop("Tech_CRT", RoomP(r, sideA, RR - 5.4f), Quaternion.LookRotation(-toCenter));
            Prop("Trash_PizzaBoxOpen", RoomP(r, sideA + 8f, RR - 3.3f, 0.02f), Yaw(), 1.2f);
            foreach (float off in new[] { -14f, 14f })
            {
                Prop("Furn_LavaLamp", RoomP(r, sideA + off, RR - 1.1f), Yaw(), 1.4f);
                AddLight(RoomP(r, sideA + off, RR - 1.5f, 0.9f), Mat.Hex("#FF4FD8"), 5f, 1.6f);
            }
            Prop("Furn_BeanBag", RoomP(r, a - 90f + 20f, RR - 2.2f), Yaw());
            Prop("Tech_Arcade", RoomP(r, a - 90f - 10f, RR - 0.9f), Quaternion.LookRotation(-Geo.Out(a - 100f)));
            Prop("Deco_Balloons", RoomP(r, a + 160f, RR - 1.6f), Yaw(), 1.4f);
            for (int k = 0; k < 6; k++) Prop(Pick(SmallTrash), RoomP(r, a + Rng(60, 300), Rng(2.5f, RR - 1.5f)), Yaw(), 1.3f);
            DiscoBall(r.Center + Vector3.up * 9.5f, 1.8f);
            Prop("Tech_Vending", RoomP(r, a + 200f, RR - 0.9f), Quaternion.LookRotation(-Geo.Out(a + 200f)));
            Prop("Furn_Fridge", RoomP(r, a + 215f, RR - 0.8f), Quaternion.LookRotation(-Geo.Out(a + 215f)));
            Prop("Deco_Gnome", RoomP(r, sideA + 25f, RR - 1.2f), Quaternion.LookRotation(toCenter), 1.6f);
            for (int k = 0; k < 4; k++) RoomWallDecal(r, a + Rng(60, 300), Rng(0.8f, 3.4f), Pick(Tags), new Vector2(1.6f, 0.8f));
            RoomWallDecal(r, a + 230f, 3f, Pick(Throwups), new Vector2(3.4f, 1.7f));
            RoomWallDecal(r, a + 130f, 2.6f, Pick(Graffiti), new Vector2(5f, 2.5f));
            RoomWallDecal(r, a - 130f, 2.2f, Pick(Posters), new Vector2(1.2f, 1.8f));
            RoomWallDecal(r, a - 145f, 2.1f, Pick(Posters), new Vector2(1.2f, 1.8f));
            var (tex, col) = Pick(Neons);
            RoomWallDecal(r, a + 180f, 5.5f, tex, new Vector2(3.2f, 1.6f), 2.2f);
            AddLight(RoomP(r, a + 180f, RR - 1.5f, 5.5f), Mat.Hex(col), 8f, 3f);
            for (int k = 0; k < 6; k++) RoomWallDecal(r, a + Rng(50, 310), Rng(0.8f, 3f), Pick(Stickers), Vector2.one * Rng(0.3f, 0.5f));
        }

        /// <summary>Hatch rooms: messy arena, centre kept clear for the hatch.</summary>
        static void DressGateRoom(RoomInfo r)
        {
            float RR = r.Size / 2, a = r.Angle;
            for (int k = 0; k < 5; k++)
            {
                float ang = a + Rng(55, 305);
                switch (rnd.Next(5))
                {
                    case 0: Prop("Trash_TireStack", RoomP(r, ang, RR - 1.2f), Yaw()); break;
                    case 1: Prop("Trash_Bag_Black", RoomP(r, ang, RR - 1f), Yaw(), 1.2f); break;
                    case 2: Prop("Street_Cone", RoomP(r, ang, RR - 1.6f), Yaw()); break;
                    case 3: Prop("Street_Barrier", RoomP(r, ang, RR - 1.4f), Quaternion.LookRotation(-Geo.Out(ang))); break;
                    default: Prop("Trash_Cardboard", RoomP(r, ang, RR - 1.1f), Yaw()); break;
                }
            }
            for (int k = 0; k < 6; k++) Prop(Pick(SmallTrash), RoomP(r, a + Rng(0, 360), Rng(5.0f, RR - 1.2f)), Yaw(), 1.3f);
            RoomWallDecal(r, a + 180f, 2.8f, Pick(Graffiti), new Vector2(5.5f, 2.75f));
            RoomWallDecal(r, a + 250f, 2.0f, Pick(Throwups), new Vector2(3.2f, 1.6f));
            for (int k = 0; k < 5; k++) RoomWallDecal(r, a + Rng(50, 310), Rng(0.8f, 3.6f), Pick(Tags), new Vector2(1.6f, 0.8f));
            for (int k = 0; k < 3; k++) { float ang = a + Rng(60, 300); Prop("Trash_Drum", RoomP(r, ang, RR - 1.1f), Yaw(), 1.1f); }
            Prop("Street_WetFloor", RoomP(r, a + 30f, 3.2f), Yaw(), 1.3f);
            RoomWallDecal(r, a + 110f, 2.4f, Pick(Graffiti), new Vector2(4f, 2f));
            for (int k = 0; k < 5; k++) RoomWallDecal(r, a + Rng(50, 310), Rng(0.8f, 3.2f), Pick(Stickers), Vector2.one * Rng(0.3f, 0.5f));
            FloorDecal(RoomP(r, a + 200f, RR * 0.7f, 0.012f), Vector3.forward, "puddle_a", Rng(1.5f, 2.5f));
        }

        static void DiscoBall(Vector3 pos, float scale)
        {
            var ball = Prop("Deco_DiscoBall", pos - Vector3.up * 0.8f * scale, Quaternion.identity, scale);
            if (!ball) return;
            var sp = ball.AddComponent<Spinner>();
            sp.Speed = 25f;
            sp.OrbitR = 1.4f * scale;
            sp.Orbit = new Light[3];
            for (int i = 0; i < 3; i++) sp.Orbit[i] = Geo.PointLight(ball.transform, pos, Mat.Hex(Y2KColors[i]), 9f * scale * 0.6f, 3f);
        }

        // ------------------------------------------------------------ arena: the trash throne
        static void DressArena(LevelBuilder lb)
        {
            Vector3 c = new Vector3(0, LevelBuilder.ArenaY, 0);
            float AR = LevelBuilder.ArenaR;
            Vector3 throne = c + new Vector3(0, 0, 10f);
            Quaternion face = Quaternion.LookRotation(Vector3.back);
            Prop("Trash_TireStack", throne + new Vector3(-0.8f, 0, 0), Yaw(), 1.2f);
            Prop("Trash_TireStack", throne + new Vector3(0.8f, 0, 0), Yaw(), 1.2f);
            Prop("Trash_TireStack", throne + new Vector3(0, 0, 0.7f), Yaw(), 1.2f);
            Prop("Furn_Couch", throne + new Vector3(0, 0.95f, 0.2f), face, 1.3f);
            Prop("Tech_Arcade", throne + new Vector3(-3.2f, 0, 0.6f), face * Quaternion.Euler(0, 20, 0), 1.2f);
            Prop("Tech_Arcade", throne + new Vector3(3.2f, 0, 0.6f), face * Quaternion.Euler(0, -20, 0), 1.2f);
            Prop("Deco_Balloons", throne + new Vector3(-2f, 0, 1.6f), Yaw(), 1.8f);
            Prop("Deco_Balloons", throne + new Vector3(2f, 0, 1.6f), Yaw(), 1.8f);
            foreach (float x in new[] { -1.6f, 1.6f }) Prop("Furn_LavaLamp", throne + new Vector3(x, 0, -1.2f), Yaw(), 2f);
            Prop("Furn_Rug", throne + new Vector3(0, 0, -2.2f), Yaw(), 2.2f);
            for (int k = 0; k < 10; k++) Prop(Pick(SmallTrash), throne + new Vector3(Rng(-4, 4), 0, Rng(-3, 2)), Yaw(), 1.4f);
            // neon KING above the throne
            PropLib.DecalQuad(root, "neon_king", Geo.Polar(90, AR - 0.1f, LevelBuilder.ArenaY + 8f), Geo.Out(90), Vector3.up, new Vector2(9f, 4.5f), 2.5f);
            AddLight(Geo.Polar(90, AR - 3f, LevelBuilder.ArenaY + 8f), Mat.Hex("#B46BFF"), 18f, 5f);
            AddLight(throne + Vector3.up * 3f, Mat.Hex("#FFD36B"), 10f, 3f);
            DiscoBall(c + Vector3.up * 16f, 3.2f);

            // perimeter: junk + mushrooms + graffiti
            for (int k = 0; k < 12; k++)
            {
                float ang = k * 30f + 15f + 15f;
                if (Mathf.Abs(Mathf.DeltaAngle(ang, 90)) < 25) continue;
                Vector3 p = c + Geo.Out(ang) * (AR - 3f);
                if (k % 3 == 0) { Prop("Deco_Mushrooms", p, Yaw(), Rng(1.5f, 2.5f)); AddLight(p + Vector3.up, Mat.Hex(Pick(new[] { "#3EF4FF", "#FF4FD8", "#B6FF3C" })), 6f, 2f); }
                else if (k % 3 == 1) { Prop("Trash_Bag_Black", p, Yaw(), 1.3f); Prop("Trash_Bag_Blue", p + Geo.Out(ang + 90) * 1f, Yaw(), 1.1f); }
                else Prop("Trash_TireStack", p, Yaw());
                PropLib.DecalQuad(root, Graffiti[k % Graffiti.Length], c + Geo.Out(ang + 15f) * (AR - 0.12f) + Vector3.up * Rng(2.5f, 4f), Geo.Out(ang + 15f), Vector3.up, new Vector2(6f, 3f));
            }
            // fairy lights between the columns
            for (int k = 0; k < 12; k++)
            {
                float a0 = k * 30f + 15f, a1 = a0 + 30f;
                FairyString(c + Geo.Out(a0) * (AR - 1.8f) + Vector3.up * 11f, c + Geo.Out(a1) * (AR - 1.8f) + Vector3.up * 11f, 2.5f, 20);
            }
            for (int k = 0; k < 6; k++) FloorDecal(c + Geo.Out(Rng(0, 360)) * Rng(14, 28) + Vector3.up * 0.012f, Vector3.forward, Chance(0.5f) ? "slime_a" : "puddle_b", Rng(1.5f, 3f));
        }
    }
}
