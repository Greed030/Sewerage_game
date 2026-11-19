using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>Common base: health, knockback, stun, gravity-driven CharacterController movement.</summary>
    [RequireComponent(typeof(CharacterController))]
    public abstract class Enemy : MonoBehaviour
    {
        public static readonly List<Enemy> All = new();

        public Health Hp;
        protected CharacterController cc;
        protected Transform visual;
        protected Vector3 knock;
        protected float yVel, stunUntil;
        public bool Stunned => Time.time < stunUntil;

        protected virtual void Awake()
        {
            cc = GetComponent<CharacterController>();
            Hp = GetComponent<Health>();
            All.Add(this);
            if (Hp) Hp.Damaged += _ => { if (!Hp.Dead && visual) StartCoroutine(Squash()); };
        }

        Vector3 visualBase; bool baseSet; int squashes;
        protected Vector3 VisualBase { get { if (!baseSet && visual) { visualBase = visual.localScale; baseSet = true; } return visualBase; } }

        /// <summary>Cartoon squash & stretch when hit.</summary>
        IEnumerator Squash()
        {
            var b = VisualBase; int id = ++squashes;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.22f)
            {
                if (id != squashes || !visual || Hp.Dead) yield break;
                float k = Mathf.Sin(t * Mathf.PI) * (1 - t);
                visual.localScale = Vector3.Scale(b, new Vector3(1 + 0.35f * k, 1 - 0.3f * k, 1 + 0.35f * k));
                yield return null;
            }
            if (visual) visual.localScale = b;
        }

        protected virtual void OnDestroy() => All.Remove(this);

        public void Knock(Vector3 v) => knock += v * KnockScale;
        protected virtual float KnockScale => 1f;
        public void Stun(float t)
        {
            stunUntil = Mathf.Max(stunUntil, Time.time + t);
            StartCoroutine(Fx.StunStars(transform, t, cc.height + 0.3f));
        }

        protected Player Target
        {
            get
            {
                var p = SewerGame.Instance.Player;
                return p && !p.Hp.Dead && !p.Invisible && SewerGame.Instance.Playing ? p : null;
            }
        }

        protected void Step(Vector3 horiz)
        {
            if (cc.isGrounded && yVel < 0) yVel = -2f;
            yVel -= 20f * Time.deltaTime;
            knock = Vector3.MoveTowards(knock, Vector3.zero, 20f * Time.deltaTime);
            cc.Move((horiz + knock + Vector3.up * yVel) * Time.deltaTime);
            if (horiz.sqrMagnitude > 0.05f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(horiz.normalized), 8f * Time.deltaTime);
        }

        protected IEnumerator DieRoutine(float shrink = 0.4f)
        {
            enabled = false;
            cc.enabled = false;
            Vector3 s0 = transform.localScale;
            for (float t = 0; t < 1; t += Time.deltaTime / shrink)
            {
                transform.localScale = Vector3.Lerp(s0, new Vector3(s0.x * 1.4f, 0.05f, s0.z * 1.4f), t);
                yield return null;
            }
            Destroy(gameObject);
        }
    }

    /// <summary>Sewer crawler: patrols the ring, chases you when close. Can be avoided.</summary>
    public class Crawler : Enemy
    {
        float ringR, angle, dir = 1, nextBite;
        Transform legsA, legsB, bodyT;
        Quaternion legsA0 = Quaternion.identity, legsB0 = Quaternion.identity;
        Vector3 body0;
        static readonly string[] Variants = { "Crawler_Toxic", "Crawler_Grape", "Crawler_Rust" };

        public static Crawler Spawn(Vector3 pos, float ringRadius, float angle, Transform parent)
        {
            var g = new GameObject("Crawler");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var cc = g.AddComponent<CharacterController>();
            cc.height = 0.9f; cc.radius = 0.45f; cc.center = new Vector3(0, 0.45f, 0); cc.stepOffset = 0.55f;
            var hp = g.AddComponent<Health>(); hp.ResetHealth(35);
            var c = g.AddComponent<Crawler>();
            c.ringR = ringRadius; c.angle = angle; c.dir = Random.value > 0.5f ? 1 : -1;
            c.BuildVisual();
            Geo.SetLayer(g, Geo.CamIgnoreLayer);
            hp.Died += () => c.StartCoroutine(c.Death());
            return c;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        void BuildVisual()
        {
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            var model = PropLib.SpawnMob(Variants[Random.Range(0, Variants.Length)], visual);
            if (model)
            {
                visual.localScale = Vector3.one * 0.95f;
                legsA = FindDeep(model.transform, "LegsA"); legsB = FindDeep(model.transform, "LegsB"); bodyT = FindDeep(model.transform, "Body");
                if (legsA) legsA0 = legsA.localRotation;
                if (legsB) legsB0 = legsB.localRotation;
                if (bodyT) body0 = bodyT.localPosition;
                return;
            }
            Object.Destroy(visual.gameObject);
            var body = Mat.Get(Mat.Hex("#5B6B3A"), 0.4f);
            var dark = Mat.Get(Mat.Hex("#2C331C"), 0.3f);
            var eye = Mat.Get(Mat.Hex("#FFF36B"), 0.8f, Mat.Hex("#C8B800"));
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            Geo.Prim(PrimitiveType.Sphere, visual, "Body", new Vector3(0, 0.45f, 0), new Vector3(1.0f, 0.6f, 1.25f), body);
            Geo.Prim(PrimitiveType.Sphere, visual, "Hump", new Vector3(0, 0.75f, -0.15f), new Vector3(0.6f, 0.35f, 0.6f), dark);
            Geo.Prim(PrimitiveType.Sphere, visual, "EyeL", new Vector3(-0.2f, 0.6f, 0.55f), Vector3.one * 0.2f, eye);
            Geo.Prim(PrimitiveType.Sphere, visual, "EyeR", new Vector3(0.2f, 0.6f, 0.55f), Vector3.one * 0.2f, eye);
            legsA = new GameObject("LegsA").transform; legsA.SetParent(visual, false);
            legsB = new GameObject("LegsB").transform; legsB.SetParent(visual, false);
            for (int i = 0; i < 3; i++)
                for (int s = -1; s <= 1; s += 2)
                {
                    var parent = (i + (s > 0 ? 1 : 0)) % 2 == 0 ? legsA : legsB;
                    var leg = Geo.Prim(PrimitiveType.Cube, parent, "Leg", new Vector3(0.55f * s, 0.25f, -0.35f + i * 0.35f), new Vector3(0.45f, 0.08f, 0.1f), dark);
                    leg.transform.localRotation = Quaternion.Euler(0, 0, -30f * s);
                }
        }


        IEnumerator Lunge()
        {
            for (float t = 0; t < 1; t += Time.deltaTime / 0.18f)
            {
                if (!visual || Hp.Dead) yield break;
                visual.localPosition = Vector3.forward * Mathf.Sin(t * Mathf.PI) * 0.45f + Vector3.up * Mathf.Sin(t * Mathf.PI) * 0.15f;
                yield return null;
            }
            if (visual) visual.localPosition = Vector3.zero;
        }

        Color GooColor()
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && m.name.StartsWith("Skin") && m.HasProperty("_BaseColor")) return m.GetColor("_BaseColor");
            return Mat.Hex("#7CFF3A");
        }

        /// <summary>Flip on its back, legs twitch, then pop into goo.</summary>
        IEnumerator Death()
        {
            enabled = false; cc.enabled = false;
            Color goo = GooColor();
            Fx.Pop(transform.position + Vector3.up * 1.2f, "hit_bonk", 1.3f);
            var b = VisualBase;
            Vector3 p0 = transform.position;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.35f)
            {
                if (!visual) break;
                visual.localPosition = Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.8f + 0.45f * t);
                visual.localRotation = Quaternion.Euler(0, 0, 180f * Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            for (float t = 0; t < 0.7f; t += Time.deltaTime)
            {
                float w = Mathf.Sin(t * 60f) * 35f * (1 - t / 0.7f);
                if (legsA) legsA.localRotation = Quaternion.AngleAxis(w, Vector3.up) * legsA0;
                if (legsB) legsB.localRotation = Quaternion.AngleAxis(-w, Vector3.up) * legsB0;
                yield return null;
            }
            for (float t = 0; t < 1; t += Time.deltaTime / 0.14f)
            {
                if (visual) visual.localScale = Vector3.Scale(b, new Vector3(1 + 0.5f * t, 1 - 0.9f * t, 1 + 0.5f * t));
                yield return null;
            }
            Fx.Burst(p0 + Vector3.up * 0.5f, goo, 22, 6f, 0.35f, 0.8f, 1.6f, "fx_blob", false, Mat.Hex("#B6FF3C"), 0.4f);
            Fx.Burst(p0 + Vector3.up * 0.5f, Color.white, 8, 5f, 0.3f, 0.35f, 0f, "fx_star", true);
            Fx.Splat(p0, 2.2f, Random.value < 0.5f ? "slime_a" : "slime_b");
            Fx.Pop(p0 + Vector3.up * 1.0f, "hit_splat", 1.5f);
            Destroy(gameObject);
        }

        void Update()
        {
            if (Hp.Dead) return;
            Vector3 horiz = Vector3.zero;
            var t = Target;
            if (!Stunned)
            {
                if (t && Mathf.Abs(t.transform.position.y - transform.position.y) < 2.5f &&
                    (t.transform.position - transform.position).sqrMagnitude < 13f * 13f)
                {
                    Vector3 to = t.transform.position - transform.position; to.y = 0;
                    horiz = to.normalized * 5.2f;
                    if (to.sqrMagnitude < 1.3f * 1.3f && Time.time > nextBite)
                    {
                        nextBite = Time.time + 0.9f;
                        t.Hp.Hit(10);
                        t.Knock(to.normalized * 5f);
                        StartCoroutine(Lunge());
                    }
                }
                else
                {
                    // patrol along the ring
                    angle += dir * (2.2f / ringR) * Mathf.Rad2Deg * Time.deltaTime;
                    Vector3 goal = Geo.Polar(angle, ringR, transform.position.y);
                    Vector3 to = goal - transform.position; to.y = 0;
                    horiz = to.sqrMagnitude > 0.01f ? to.normalized * 2.2f : Vector3.zero;
                    if (Random.value < 0.002f) dir = -dir;
                }
            }
            Step(horiz);
            float wig = Mathf.Sin(Time.time * 18f) * (horiz.sqrMagnitude > 0.1f ? 18f : 3f);
            if (legsA) legsA.localRotation = Quaternion.AngleAxis(wig, legsA.parent.InverseTransformDirection(transform.up)) * legsA0;
            if (legsB) legsB.localRotation = Quaternion.AngleAxis(-wig, legsB.parent.InverseTransformDirection(transform.up)) * legsB0;
            if (bodyT) bodyT.localPosition = body0 + bodyT.parent.InverseTransformDirection(transform.up) * (Mathf.Abs(Mathf.Sin(Time.time * 18f)) * (horiz.sqrMagnitude > 0.1f ? 0.04f : 0.01f));
        }
    }

    /// <summary>Room guardian. Wakes up when you enter its room. Telegraphs a ground slam.</summary>
    public class Boss : Enemy
    {
        public string Title;
        public RoomInfo Room;
        public bool Awake_;
        public System.Action OnDefeated;

        float speed, slamDmg, slamR, nextAttack;
        bool attacking;
        Transform body, armL, armR;
        Vector3 bodyScale0 = new Vector3(1.5f, 1.6f, 1.3f);
        Quaternion armL0, armR0;
        static int modelIdx;
        GameObject warn;
        protected override float KnockScale => 0.15f;

        public static Boss Spawn(RoomInfo room, string title, float hp, float scale, Color color, Transform parent)
        {
            var g = new GameObject($"Boss_{title}");
            g.transform.SetParent(parent, false);
            Vector3 toRing = Vector3.ProjectOnPlane(room.Center, Vector3.up).normalized;
            Vector3 pos = room.Kind == RoomKind.Arena ? room.Center + new Vector3(0, 0.2f, -5f) : room.Center - toRing * (room.Size * 0.2f) + Vector3.up * 0.2f;
            g.transform.position = pos;
            if (toRing.sqrMagnitude > 0.1f) g.transform.rotation = Quaternion.LookRotation(toRing);
            var cc = g.AddComponent<CharacterController>();
            cc.height = 1.8f * scale; cc.radius = 0.7f * scale; cc.center = new Vector3(0, cc.height / 2, 0);
            cc.stepOffset = 0.55f;
            var h = g.AddComponent<Health>(); h.ResetHealth(hp);
            var b = g.AddComponent<Boss>();
            b.Title = title; b.Room = room;
            b.speed = 3.2f / Mathf.Sqrt(scale) + 0.6f; b.slamDmg = 14f + 6f * scale; b.slamR = 2.6f * scale;
            b.BuildVisual(scale, color);
            Geo.SetLayer(g, Geo.CamIgnoreLayer);
            h.Died += b.Defeated;
            return b;
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t) { var r = FindDeep(c, n); if (r) return r; }
            return null;
        }

        bool BuildModel(float s, Color color)
        {
            bool king = Title.Contains("King");
            string name = king ? "Boss_RatKing" : (modelIdx++ % 2 == 0 ? "Boss_Blob" : "Boss_Gator");
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * s * 0.9f;
            var model = PropLib.SpawnMob(name, visual);
            if (!model) { Object.Destroy(visual.gameObject); return false; }
            if (!king)   // tint skin to the boss colour
                foreach (var r in model.GetComponentsInChildren<Renderer>())
                {
                    var mats = r.materials;
                    foreach (var m in mats)
                    {
                        string n = m.name;
                        Color c = n.StartsWith("SkinLight") ? Color.Lerp(color, Color.white, 0.55f) : n.StartsWith("SkinDark") ? color * 0.45f : n.StartsWith("Skin") ? color : default;
                        if (c != default) { c.a = 1; m.SetColor("_BaseColor", c); }
                    }
                    r.materials = mats;
                }
            body = FindDeep(model.transform, "Body"); armL = FindDeep(model.transform, "ArmL"); armR = FindDeep(model.transform, "ArmR");
            if (body) bodyScale0 = body.localScale;
            if (armL) armL0 = armL.localRotation;
            if (armR) armR0 = armR.localRotation;
            return true;
        }

        void Arms(float swing)
        {
            if (armL) armL.localRotation = Quaternion.AngleAxis(swing, armL.parent.InverseTransformDirection(transform.right)) * armL0;
            if (armR) armR.localRotation = Quaternion.AngleAxis(swing, armR.parent.InverseTransformDirection(transform.right)) * armR0;
        }

        void BuildVisual(float s, Color color)
        {
            if (BuildModel(s, color)) return;
            var skin = Mat.Get(color, 0.35f);
            var dark = Mat.Get(color * 0.5f, 0.3f);
            var eye = Mat.Get(Color.white, 0.6f, new Color(1f, 0.2f, 0.1f) * 1.5f);
            var horn = Mat.Get(Mat.Hex("#E8DCC0"), 0.5f);
            visual = new GameObject("Visual").transform;
            visual.SetParent(transform, false);
            visual.localScale = Vector3.one * s;
            body = Geo.Prim(PrimitiveType.Sphere, visual, "Body", new Vector3(0, 0.85f, 0), new Vector3(1.5f, 1.6f, 1.3f), skin).transform;
            Geo.Prim(PrimitiveType.Sphere, body, "Belly", new Vector3(0, -0.12f, 0.3f), new Vector3(0.7f, 0.6f, 0.5f), Mat.Get(Color.Lerp(color, Color.white, 0.45f), 0.3f));
            Geo.Prim(PrimitiveType.Sphere, body, "EyeL", new Vector3(-0.2f, 0.22f, 0.42f), new Vector3(0.18f, 0.2f, 0.12f), eye);
            Geo.Prim(PrimitiveType.Sphere, body, "EyeR", new Vector3(0.2f, 0.22f, 0.42f), new Vector3(0.18f, 0.2f, 0.12f), eye);
            Geo.Prim(PrimitiveType.Cube, body, "Mouth", new Vector3(0, 0.0f, 0.47f), new Vector3(0.45f, 0.06f, 0.05f), Mat.Get(Color.black));
            for (int k = -1; k <= 1; k += 2)
            {
                var hrn = Geo.Prim(PrimitiveType.Capsule, body, "Horn", new Vector3(0.28f * k, 0.5f, 0.05f), new Vector3(0.12f, 0.2f, 0.12f), horn);
                hrn.transform.localRotation = Quaternion.Euler(0, 0, -35f * k);
                var arm = Geo.Prim(PrimitiveType.Capsule, visual, "Arm", new Vector3(0.85f * k, 0.8f, 0.1f), new Vector3(0.35f, 0.5f, 0.35f), dark);
                arm.transform.localRotation = Quaternion.Euler(0, 0, 20f * k);
                Geo.Prim(PrimitiveType.Sphere, visual, "Foot", new Vector3(0.4f * k, 0.12f, 0.15f), new Vector3(0.45f, 0.25f, 0.6f), dark);
            }
        }

        bool PlayerInRoom(Player p)
        {
            Vector3 d = p.transform.position - Room.Center;
            if (Mathf.Abs(d.y) > 3f) return false;
            d.y = 0;
            return d.magnitude < Room.Size * 0.62f;
        }

        void Update()
        {
            if (Hp.Dead) return;
            var t = Target;
            bool inRoom = t && PlayerInRoom(t);
            if (inRoom && !Awake_)
            {
                Awake_ = true;
                Hud.Message($"{Title} woke up!");
                SewerGame.Instance.ActiveBoss = this;
            }
            Vector3 horiz = Vector3.zero;
            if (Awake_ && !Stunned && !attacking && t && inRoom)
            {
                Vector3 to = t.transform.position - transform.position; to.y = 0;
                if (to.magnitude > slamR * 0.75f) horiz = to.normalized * speed;
                else transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), 6f * Time.deltaTime);
                if (to.magnitude < slamR && Time.time > nextAttack) StartCoroutine(SlamAttack());
            }
            Step(horiz);
            // idle breathing
            if (body && !attacking) body.localScale = Vector3.Scale(bodyScale0, new Vector3(1f + Mathf.Sin(Time.time * 3f + 1f) * 0.015f, 1f + Mathf.Sin(Time.time * 3f) * 0.03f, 1f + Mathf.Sin(Time.time * 3f + 1f) * 0.015f));
            if (!attacking) Arms(Mathf.Sin(Time.time * (horiz.sqrMagnitude > 0.1f ? 7f : 2f)) * (horiz.sqrMagnitude > 0.1f ? 25f : 8f));
        }

        IEnumerator SlamAttack()
        {
            attacking = true;
            nextAttack = Time.time + 1.9f;
            warn = Fx.Telegraph(transform.position + Vector3.up * 0.06f, slamR);
            // wind-up: rise and grow (time to dodge!)
            for (float k = 0; k < 1; k += Time.deltaTime / 0.75f)
            {
                if (Stunned) break;
                visual.localPosition = Vector3.up * Mathf.Sin(k * Mathf.PI * 0.5f) * 0.6f;
                if (body) body.localScale = bodyScale0 * (1 + 0.15f * k);
                Arms(-110f * k);
                yield return null;
            }
            visual.localPosition = Vector3.zero;
            Arms(20f);
            if (body) body.localScale = bodyScale0;
            Destroy(warn);
            if (!Stunned && !Hp.Dead)
            {
                StartCoroutine(Fx.Shockwave(transform.position + Vector3.up * 0.1f, slamR, new Color(1f, 0.3f, 0.2f)));
                SewerGame.Instance.Shake(0.25f);
                var p = SewerGame.Instance.Player;
                if (p && !p.Hp.Dead)
                {
                    Vector3 d = p.transform.position - transform.position;
                    if (Mathf.Abs(d.y) < 1.5f && new Vector3(d.x, 0, d.z).magnitude < slamR)
                    {
                        p.Hp.Hit(slamDmg);
                        p.Knock(new Vector3(d.x, 0, d.z).normalized * 11f);
                    }
                }
            }
            yield return new WaitForSeconds(0.35f);
            attacking = false;
        }


        /// <summary>Wobble, spin and explode into goo + confetti.</summary>
        IEnumerator BossDeath()
        {
            enabled = false; cc.enabled = false; attacking = true;
            var b = VisualBase;
            Color col = Color.magenta;
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && m.name.StartsWith("Skin") && m.HasProperty("_BaseColor")) { col = m.GetColor("_BaseColor"); break; }
            float h = cc.height;
            float nextPuff = 0;
            for (float t = 0; t < 1.4f; t += Time.deltaTime)
            {
                if (!visual) break;
                float k = t / 1.4f;
                visual.localPosition = Random.insideUnitSphere * 0.12f * (1 + k) + Vector3.up * k * 0.4f;
                visual.localRotation = Quaternion.Euler(Mathf.Sin(t * 30f) * 6f, 360f * k * k * 2f, Mathf.Cos(t * 27f) * 6f);
                visual.localScale = b * (1 + 0.12f * Mathf.Sin(t * 25f) * k + 0.15f * k);
                Arms(Mathf.Sin(t * 22f) * 60f);
                if (t > nextPuff)
                {
                    nextPuff = t + 0.18f;
                    Fx.Burst(transform.position + Vector3.up * h * Random.Range(0.3f, 0.9f) + Random.insideUnitSphere * h * 0.3f, Color.white, 6, 5f, 0.4f, 0.4f, 0f, "fx_star", true, col);
                }
                yield return null;
            }
            Vector3 c = transform.position + Vector3.up * h * 0.5f;
            Fx.Burst(c, col, 45, 11f, 0.7f, 1.1f, 1.4f, "fx_blob", false, Color.Lerp(col, Color.white, 0.5f), h * 0.3f);
            Fx.Burst(c, Mat.Hex("#FF4FD8"), 60, 13f, 0.35f, 2.2f, 0.6f, "fx_confetti", false, Mat.Hex("#3EF4FF"), h * 0.2f);
            Fx.Burst(c, Mat.Hex("#FFE45C"), 40, 10f, 0.3f, 2.0f, 0.5f, "fx_confetti", false, Mat.Hex("#B6FF3C"), h * 0.2f);
            Fx.Burst(c, Color.white, 20, 14f, 0.8f, 0.5f, 0f, "fx_star", true, Mat.Hex("#FFE45C"));
            SewerGame.Instance.StartCoroutine(Fx.Flash(c, Color.Lerp(col, Color.white, 0.6f), 25f, 0.4f));
            Fx.Pop(c + Vector3.up * h * 0.4f, "hit_ko", 3.2f);
            Fx.Splat(transform.position, 6f, "slime_a");
            SewerGame.Instance.Shake(0.6f);
            SewerGame.Instance.HitStop(0.12f);
            Destroy(gameObject);
        }

        void Defeated()
        {
            Hud.Message($"{Title} defeated!");
            if (SewerGame.Instance.ActiveBoss == this) SewerGame.Instance.ActiveBoss = null;
            OnDefeated?.Invoke();
            StopAllCoroutines();
            if (warn) Destroy(warn);
            visual.localPosition = Vector3.zero;
            StartCoroutine(BossDeath());
        }
    }
}
