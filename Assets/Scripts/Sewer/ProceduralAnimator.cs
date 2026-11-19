using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>
    /// Full-body procedural animation for the rigged character (Hips, Spine, Head, UpperArm/LowerArm/Hand, UpperLeg/LowerLeg/Foot).
    /// Hands and feet use two-bone IK, so poses are written as "where the hands/feet/bat should be" in body space,
    /// and the hips, spine, head and legs all take part: weight shift, pelvis/chest counter-rotation, steps, squash & stretch.
    /// Combo: 1) forehand right→left, 2) backhand left→right, 3) jumping overhead smash.
    /// </summary>
    public class ProceduralAnimator : MonoBehaviour
    {
        public Transform Body;                 // player root (axes + ground)
        public float Speed, VerticalSpeed;
        public bool Grounded = true, Sprinting;
        /// <summary>Climb progress 0..1 (-1 = not climbing), the ledge edge point and the wall normal.</summary>
        public float ClimbT = -1;
        public Vector3 ClimbEdge, ClimbNormal;
        Vector3 climbFootL, climbFootR;
        public Transform Bat;                  // bat holder, long axis = local +Y, grip at origin
        public TrailRenderer[] BatTrails;
        public ParticleSystem BatSparks;
        public Color Accent = Color.magenta;

        // ------------------------------------------------------------ rig
        readonly Dictionary<string, Transform> bones = new();
        readonly Dictionary<Transform, Quaternion> rest = new();
        static readonly string[] Names =
        {
            "Hips", "Spine", "Chest", "Neck", "Head",
            "UpperArm_L", "LowerArm_L", "Hand_L", "UpperArm_R", "LowerArm_R", "Hand_R",
            "UpperLeg_L", "LowerLeg_L", "Foot_L", "UpperLeg_R", "LowerLeg_R", "Foot_R",
        };
        Transform hips;
        Vector3 hipsRestLocal, footRestL, footRestR, baseScale;
        float k = 1;                           // rig scale vs. the 1.42 m shoulder height the poses are written for

        // ------------------------------------------------------------ pose description (body space, metres / degrees)
        struct Pose
        {
            public Vector3 grip, dir;          // right-hand grip point and bat direction
            public float hipsYaw, chestYaw, lean, roll, crouch, stance, headYaw, headPitch;
            public float twoHands;             // 1 = left hand also on the bat (IK), 0 = left arm free (FK)
            public float lPitch, lZ, lElbow;   // free left arm

            public static Pose Lerp(Pose a, Pose b, float t)
            {
                if (t <= 0) return a;
                if (t >= 1) return b;
                return new Pose
                {
                    grip = Vector3.Lerp(a.grip, b.grip, t), dir = Vector3.Slerp(a.dir, b.dir, t).normalized,
                    hipsYaw = Mathf.Lerp(a.hipsYaw, b.hipsYaw, t), chestYaw = Mathf.Lerp(a.chestYaw, b.chestYaw, t),
                    lean = Mathf.Lerp(a.lean, b.lean, t), roll = Mathf.Lerp(a.roll, b.roll, t), crouch = Mathf.Lerp(a.crouch, b.crouch, t),
                    stance = Mathf.Lerp(a.stance, b.stance, t), headYaw = Mathf.Lerp(a.headYaw, b.headYaw, t), headPitch = Mathf.Lerp(a.headPitch, b.headPitch, t),
                    twoHands = Mathf.Lerp(a.twoHands, b.twoHands, t),
                    lPitch = Mathf.Lerp(a.lPitch, b.lPitch, t), lZ = Mathf.Lerp(a.lZ, b.lZ, t), lElbow = Mathf.Lerp(a.lElbow, b.lElbow, t),
                };
            }
        }

        static Pose P(float gx, float gy, float gz, float dx, float dy, float dz, float hips, float chest, float lean, float crouch, float stance, float head = 0, float two = 1)
            => new Pose { grip = new Vector3(gx, gy, gz), dir = new Vector3(dx, dy, dz).normalized, hipsYaw = hips, chestYaw = chest, lean = lean, crouch = crouch, stance = stance, headYaw = head, twoHands = two, lPitch = -10, lElbow = -20, lZ = -6 };

        static readonly Pose Rest = P(0.46f, 1.0f, 0.16f, 0.42f, 0.886f, -0.19f, 0, 0, 4, 0, 0, 0, 0);

        struct Key { public float t; public Pose p; public Key(float t, Pose p) { this.t = t; this.p = p; } }
        static readonly Key[][] Combos =
        {
            // 1) forehand: wind up to the right, whole body uncoils to the left
            new[] { new Key(0, Rest),
                    new Key(0.28f, P(0.42f, 1.28f, -0.12f, 0.75f, 0.45f, -0.5f, 25, 35, -4, 0.06f, 0.6f, -20)),
                    new Key(0.46f, P(0.08f, 1.08f, 0.48f, 0.25f, 0.0f, 1f, -8, -15, 14, 0.14f, 1f, 0)),
                    new Key(0.64f, P(-0.38f, 1.18f, 0.12f, -0.95f, 0.15f, -0.25f, -35, -55, 10, 0.12f, 1f, 10)),
                    new Key(1, Rest) },
            // 2) backhand: wind up to the left, swing out to the right
            new[] { new Key(0, Rest),
                    new Key(0.28f, P(-0.36f, 1.32f, -0.05f, -0.8f, 0.45f, -0.4f, -28, -40, -2, 0.06f, -0.6f, 20)),
                    new Key(0.46f, P(-0.02f, 1.08f, 0.48f, -0.2f, 0f, 1f, 8, 15, 14, 0.14f, -1f, 0)),
                    new Key(0.64f, P(0.42f, 1.2f, 0.12f, 0.95f, 0.15f, -0.25f, 35, 55, 10, 0.12f, -1f, -10)),
                    new Key(1, Rest) },
            // 3) overhead smash: rise on the toes with the bat behind the head, slam down into a squat
            new[] { new Key(0, Rest),
                    new Key(0.3f, P(0.05f, 1.85f, -0.12f, 0f, 0.55f, -0.85f, 0, 5, -18, -0.1f, 0.4f, 0)),
                    new Key(0.5f, P(0.04f, 0.82f, 0.55f, 0f, -0.75f, 0.66f, 0, 0, 38, 0.38f, 1f, 0)),
                    new Key(0.74f, P(0.04f, 0.8f, 0.55f, 0f, -0.8f, 0.6f, 0, 0, 34, 0.34f, 1f, 0)),
                    new Key(1, Rest) },
        };

        // ------------------------------------------------------------ state
        float phase, cycle, amp, dead, runBlend, duty = 0.6f, swingT0 = -10, swingDur = 0.4f, dashUntil = -10, abilityT0 = -10, abilityDur, slamLandT = -10, hurtT = -10;
        float landT = -10, takeoffT = -10, airTime, landPower;
        int combo; bool isDead, wasGrounded = true;
        Ability abilityKind;
        TrailRenderer bodyTrail;
        float lastSinL, lastSinR, nextDust;

        public bool Swinging => Time.time - swingT0 < swingDur;
        public static string Debug;
        Vector3 lastBodyPos; float measured;
        readonly Dictionary<Transform, Vector3> fwdLocal = new(); readonly Dictionary<Transform, Quaternion> footBind = new();   // each leg bone's "forward" (knee/toe direction) in its own space

        void Start()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                foreach (var n in Names)
                    if ((t.name == n || t.name.EndsWith(":" + n) || t.name.EndsWith("_" + n)) && !bones.ContainsKey(n))
                    { bones[n] = t; rest[t] = t.localRotation; }
            baseScale = transform.localScale;
            if (Body)
                foreach (var n in new[] { "UpperLeg_L", "LowerLeg_L", "Foot_L", "UpperLeg_R", "LowerLeg_R", "Foot_R" })
                    if (bones.TryGetValue(n, out var b)) { fwdLocal[b] = Quaternion.Inverse(b.rotation) * Body.forward; if (n.StartsWith("Foot")) footBind[b] = Quaternion.Inverse(Body.rotation) * b.rotation; }
            if (bones.TryGetValue("Hips", out hips)) hipsRestLocal = hips.localPosition;
            if (Body)
            {
                if (bones.TryGetValue("Foot_L", out var fl)) footRestL = Body.InverseTransformPoint(fl.position);
                if (bones.TryGetValue("Foot_R", out var fr)) footRestR = Body.InverseTransformPoint(fr.position);
                if (bones.TryGetValue("UpperArm_R", out var sh)) k = Mathf.Clamp(Body.InverseTransformPoint(sh.position).y / 1.42f, 0.6f, 1.6f);
            }
            if (hips)
            {
                var g = new GameObject("BodyTrail"); g.transform.SetParent(hips, false);
                bodyTrail = g.AddComponent<TrailRenderer>();
                bodyTrail.time = 0.22f; bodyTrail.minVertexDistance = 0.05f; bodyTrail.emitting = false;
                bodyTrail.widthCurve = new AnimationCurve(new Keyframe(0, 0.6f * k), new Keyframe(1, 0));
                var gr = new Gradient();
                gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Accent, 0.4f), new GradientColorKey(Accent, 1) },
                           new[] { new GradientAlphaKey(0.45f, 0), new GradientAlphaKey(0.25f, 0.5f), new GradientAlphaKey(0, 1) });
                bodyTrail.colorGradient = gr;
                bodyTrail.sharedMaterial = Fx.FxMat("fx_streak", true);
                bodyTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        // ------------------------------------------------------------ API used by Player
        public void Punch() => Swing(0, 0.42f);
        public void Swing(int c, float dur) { combo = Mathf.Clamp(c, 0, 2); swingT0 = Time.time; swingDur = dur; }
        public void DashPose(float dur) => dashUntil = Time.time + dur;
        public void AbilityPose(Ability a, float dur) { abilityKind = a; abilityT0 = Time.time; abilityDur = dur; }
        public void SlamLand() { slamLandT = Time.time; abilityT0 = -10; }
        public void Hurt() => hurtT = Time.time;
        public void SetDead(bool d) => isDead = d;

        static float Ease(float x) => x * x * (3 - 2 * x);

        // one leg over a full run cycle: 0 contact, .12 down, .25 passing (under body), .38 push-off,
        // .5 heel kicks up behind, .62 foot tucked under, .75 knee high in front, .88 reaching forward, 1 contact
        static readonly Vector2[] FootZ = { new(0, 0.5f), new(0.12f, 0.26f), new(0.25f, 0f), new(0.38f, -0.34f), new(0.5f, -0.55f), new(0.62f, -0.4f), new(0.75f, -0.02f), new(0.88f, 0.44f), new(1, 0.5f) };
        static readonly Vector2[] FootY = { new(0, 0.03f), new(0.12f, 0f), new(0.25f, 0f), new(0.38f, 0.07f), new(0.5f, 0.34f), new(0.62f, 0.4f), new(0.75f, 0.3f), new(0.88f, 0.13f), new(1, 0.03f) };
        // toes: up on heel-strike, down/back on push-off (positive = toes down)
        static readonly Vector2[] FootPitch = { new(0, -18f), new(0.12f, 0f), new(0.25f, 0f), new(0.38f, 22f), new(0.5f, 48f), new(0.62f, 35f), new(0.75f, 8f), new(0.88f, -16f), new(1, -18f) };
        // body: low on "down", high on "up"
        static readonly Vector2[] BobKeys = { new(0, 0), new(0.12f, -0.06f), new(0.25f, 0), new(0.38f, 0.045f), new(0.5f, 0), new(0.62f, -0.06f), new(0.75f, 0), new(0.88f, 0.045f), new(1, 0) };

        static float Sample(Vector2[] k, float t)
        {
            for (int i = 0; i < k.Length - 1; i++)
                if (t <= k[i + 1].x)
                {
                    float u = Mathf.InverseLerp(k[i].x, k[i + 1].x, t);
                    return Mathf.Lerp(k[i].y, k[i + 1].y, u * u * (3 - 2 * u));
                }
            return k[k.Length - 1].y;
        }

        static Pose Sample(Key[] ks, float t)
        {
            for (int i = 0; i < ks.Length - 1; i++)
                if (t <= ks[i + 1].t) return Pose.Lerp(ks[i].p, ks[i + 1].p, Ease(Mathf.InverseLerp(ks[i].t, ks[i + 1].t, t)));
            return ks[ks.Length - 1].p;
        }

        // ------------------------------------------------------------ main
        void LateUpdate()
        {
            if (bones.Count == 0 || Body == null) return;
            foreach (var kv in rest) kv.Key.localRotation = kv.Value;
            if (hips) hips.localPosition = hipsRestLocal;
            float now = Time.time, dt = Time.deltaTime;
            // measure the real ground speed from the body's motion (cc.velocity can read 0)
            Vector3 moved = Body.position - lastBodyPos; moved.y = 0; lastBodyPos = Body.position;
            if (dt > 1e-5f) measured = Mathf.Lerp(measured, Mathf.Min(moved.magnitude / dt, 30f), 1 - Mathf.Exp(-12f * dt));
            Speed = Mathf.Max(Speed, measured);

            // ---- ground events: take-off / landing (squash, stretch, dust)
            if (wasGrounded && !Grounded && VerticalSpeed > 1f) { takeoffT = now; Dust(1.0f, 10); }
            if (!Grounded) airTime += dt;
            if (!wasGrounded && Grounded)
            {
                if (airTime > 0.18f) { landT = now; landPower = Mathf.Clamp01(airTime / 0.9f); Dust(0.8f + landPower, 10 + (int)(landPower * 12)); }
                airTime = 0;
            }
            wasGrounded = Grounded;

            // ---- gait: cycle length & stride come from the real speed, so feet don't skate
            float target = Mathf.Clamp01(Speed / 7f);
            amp = Mathf.MoveTowards(amp, Grounded ? target : 0, 5f * dt);
            float run = Mathf.InverseLerp(2f, 6f, Speed);
            duty = Mathf.Lerp(0.62f, 0.36f, run);                       // walk: long ground contact, run: flight phase
            float period = Mathf.Lerp(1.0f, 0.6f, Mathf.Clamp01(Speed / 8f));
            runBlend = Mathf.MoveTowards(runBlend, run, 3f * dt);
            if (Speed > 0.25f && Grounded) cycle += dt / period;
            cycle -= Mathf.Floor(cycle);
            phase = cycle * Mathf.PI * 2f;
            dead = Mathf.MoveTowards(dead, isDead ? 1 : 0, 3f * dt);

            // ================================================== locomotion pose
            // left foot is forward at cycle 0 (start of its ground contact), right foot at 0.5
            float c = Mathf.Cos(phase), s = Mathf.Sin(phase);
            float breathe = Mathf.Sin(now * 2.1f);
            float runK = Mathf.InverseLerp(2f, 6f, Speed) * amp;
            var pose = Rest;
            pose.hipsYaw = 7f * amp * c;                 // pelvis turns with the forward leg
            pose.chestYaw = -24f * amp * c;              // shoulders counter-rotate
            pose.roll = 3f * amp * s;                    // pelvis drops slightly on the swing side
            pose.lean = 4f + 8f * amp + 12f * runK + (Sprinting ? 6f * runK : 0) + breathe * 1f * (1 - amp) + Sample(BobKeys, cycle) * -90f * amp;
            pose.headYaw = 0;
            pose.headPitch = -pose.lean * 0.55f + Sample(BobKeys, cycle) * 60f * amp;   // head nods with each step
            pose.headYaw = 6f * amp * c;
            pose.grip += new Vector3(0, 0.02f * Mathf.Cos(phase * 2) * amp + 0.006f * breathe, 0.07f * c * amp);  // bat hand swings with the left leg
            pose.lPitch = -12f + 55f * amp * c * (0.6f + 0.4f * runK);   // left arm swings opposite the left leg
            pose.lElbow = -20f - 75f * runK - 15f * amp;
            pose.lZ = -7f;
            // body is lowest at mid-stance of each foot, highest in the flight phase
            float bounce = Sample(BobKeys, cycle) * (0.6f + 0.8f * runBlend) * amp + 0.004f * breathe - 0.03f * runK;
            float sway = -0.022f * amp * s;              // weight shifts over the standing foot

            // air: tuck while rising, reach for the floor while falling
            float air = Grounded ? 0 : 1;
            float rising = Mathf.Clamp01(VerticalSpeed / 6f), falling = Mathf.Clamp01(-VerticalSpeed / 8f);
            if (air > 0)
            {
                pose.lean = Mathf.Lerp(pose.lean, -10f, rising) + 10f * falling;
                pose.lPitch = Mathf.Lerp(-60f, -35f, falling); pose.lZ = Mathf.Lerp(-45f, -70f, falling); pose.lElbow = -40f;
                pose.grip = Rest.grip + new Vector3(0.08f, 0.12f * rising, 0);
                pose.headPitch = -8f * rising + 6f * falling;
            }

            // landing absorb
            float landK = Mathf.Clamp01(1 - (now - landT) / 0.28f);
            pose.crouch += 0.28f * landK * (0.4f + landPower);
            pose.lean += 14f * landK;

            // ================================================== actions (layered on top)
            bool swing = now - swingT0 < swingDur;
            float swingT = swing ? (now - swingT0) / swingDur : 1;
            if (swing) pose = Pose.Lerp(pose, Sample(Combos[combo], swingT), 0.92f);

            float dashK = now < dashUntil ? 1 : Mathf.Clamp01(1 - (now - dashUntil) / 0.15f);
            if (dashK > 0)
            {
                var dp = P(0.35f, 0.95f, -0.4f, 0.3f, -0.2f, -1f, 0, 0, 38, 0.12f, 0, 0, 0);
                dp.lPitch = 70f; dp.lElbow = -30f; dp.lZ = -15f;
                pose = Pose.Lerp(pose, dp, dashK);
            }

            float abT = abilityDur > 0 ? (now - abilityT0) / abilityDur : 2, abK = 0;
            if (abT >= 0 && abT < 1) abK = Mathf.Min(1, Mathf.Min(abT * 7f, (1 - abT) * 7f));
            if (abK > 0)
            {
                Pose ap = pose;
                switch (abilityKind)
                {
                    case Ability.Slam: ap = P(0.03f, 1.95f, 0.05f, 0f, 1f, 0.15f, 0, 0, -12, -0.05f, 0); break;
                    case Ability.Flash: ap = P(0.35f, 1.95f, 0.1f, 0.2f, 1f, 0f, 0, 0, -8, 0, 0, 0, 0); ap.lPitch = -160f; ap.lZ = -30f; ap.lElbow = -10f; ap.headPitch = -15f; break;
                    case Ability.Invisibility: ap = P(0.35f, 0.75f, 0.05f, 0.3f, -0.3f, -0.9f, 0, 0, 25, 0.32f, 0.3f, 0, 0); ap.lPitch = -100f; ap.lElbow = -125f; ap.lZ = 10f; break;
                }
                pose = Pose.Lerp(pose, ap, abK);
            }
            float slamK = Mathf.Clamp01(1 - (now - slamLandT) / 0.5f);
            if (slamK > 0) pose = Pose.Lerp(pose, P(0.03f, 0.8f, 0.6f, 0f, -0.7f, 0.7f, 0, 0, 40, 0.42f, 1f), slamK);

            // ---- climbing: pull up (scramble on the wall), then mantle over the edge
            bool climb = ClimbT >= 0;
            if (climb)
            {
                float ct = ClimbT; bool pull = ct < 0.62f;
                pose = Rest;
                pose.lean = pull ? 18f : Mathf.Lerp(18f, 38f, (ct - 0.62f) / 0.38f);
                pose.headPitch = pull ? -22f : -5f;
                pose.crouch = pull ? 0.05f : 0.12f;
                pose.dir = new Vector3(0.3f, 0.9f, 0.3f).normalized;
                air = 0; rising = falling = 0; dashK = 0;
                float ph = ct * 3.2f * Mathf.PI * 2;
                if (pull)
                {
                    climbFootL = new Vector3(0, 0.12f + 0.32f * Mathf.Max(0, Mathf.Sin(ph)), 0.26f);
                    climbFootR = new Vector3(0, 0.12f + 0.32f * Mathf.Max(0, -Mathf.Sin(ph)), 0.26f);
                }
                else
                {
                    float m = (ct - 0.62f) / 0.38f;
                    climbFootL = new Vector3(0, Mathf.Lerp(0.42f, 0.05f, m * m), Mathf.Lerp(0.32f, 0.05f, m));   // knee up onto the ledge
                    climbFootR = new Vector3(0, Mathf.Lerp(0.05f, 0.2f, Mathf.Sin(m * Mathf.PI)), Mathf.Lerp(-0.15f, 0.0f, m));
                }
            }

            float hurtK = Mathf.Clamp01(1 - (now - hurtT) / 0.25f);
            pose.lean -= 18f * hurtK; pose.chestYaw += 12f * hurtK;

            // ================================================== apply
            if (dead > 0.01f)
            {
                transform.localRotation = Quaternion.Euler(-80f * dead, 0, 0);
                Rot("Spine", 25f * dead, 0, 0);
                PlaceBat(pose, false, 1, 0, 0);
                SetTrails(false, false);
                if (bodyTrail) bodyTrail.emitting = false;
                return;
            }
            transform.localRotation = Quaternion.identity;

            // squash & stretch on the whole model
            float stretch = Mathf.Clamp01(1 - (now - takeoffT) / 0.22f) * 0.09f;
            float squash = landK * (0.06f + 0.08f * landPower) + (slamK > 0.6f ? 0.08f * slamK : 0);
            transform.localScale = Vector3.Scale(baseScale, new Vector3(1 - stretch * 0.5f + squash, 1 + stretch - squash, 1 - stretch * 0.5f + squash));

            // hips: bob / crouch + pelvis yaw and side roll
            if (hips)
            {
                hips.position += Body.up * ((bounce - pose.crouch) * k) + Body.right * (sway * k);
                hips.rotation = Quaternion.AngleAxis(pose.hipsYaw, Body.up) * Quaternion.AngleAxis(pose.roll, Body.forward) * hips.rotation;
            }
            // torso & head (head stays steady-ish, looking ahead)
            Rot("Spine", pose.lean, pose.chestYaw, -pose.roll * 0.6f);
            Rot("Head", pose.headPitch, pose.headYaw - pose.chestYaw * 0.45f - pose.hipsYaw * 0.4f, 0);

            // arms: right hand always holds the bat grip; left hand joins it during swings
            Vector3 gripW = Body.TransformPoint(pose.grip * k);
            Vector3 dirW = Body.TransformDirection(pose.dir);
            ArmIK("UpperArm_R", "LowerArm_R", "Hand_R", gripW, 1f);
            if (pose.twoHands > 0.05f) ArmIK("UpperArm_L", "LowerArm_L", "Hand_L", gripW + dirW * 0.14f * k, -1f, pose.twoHands);
            if (pose.twoHands < 0.95f)
            {
                float f = 1 - pose.twoHands;
                Rot("UpperArm_L", pose.lPitch * f, 0, pose.lZ * f);
                Rot("LowerArm_L", pose.lElbow * f, 0, 0);
            }

            // climbing: both hands grab the ledge edge (bat stays in the right hand)
            if (climb)
            {
                float w = Mathf.SmoothStep(0, 1, ClimbT / 0.15f);
                Vector3 into = -ClimbNormal * 0.06f + Vector3.up * 0.04f;
                ArmIK("UpperArm_R", "LowerArm_R", "Hand_R", ClimbEdge + Body.right * 0.24f * k + into, 1f, w);
                ArmIK("UpperArm_L", "LowerArm_L", "Hand_L", ClimbEdge - Body.right * 0.24f * k + into, -1f, w);
            }

            // legs: planted / stepping feet via IK
            LegIK(true, pose, air, rising, falling, dashK);
            LegIK(false, pose, air, rising, falling, dashK);

            PlaceBat(pose, swing, swingT, dashK, slamK);

            // body trail while running fast / jumping, dust on footsteps
            bool fast = Grounded && Speed > 6.2f && (Sprinting || Speed > 7.5f);
            if (bodyTrail) bodyTrail.emitting = !Grounded || fast || dashK > 0.3f;
            float tl = cycle, tr = cycle + 0.5f - Mathf.Floor(cycle + 0.5f);
            if (Grounded && Speed > 4.5f)
            {
                if (lastSinL > 0.8f && tl < 0.2f) StepDust(true);
                if (lastSinR > 0.8f && tr < 0.2f) StepDust(false);
            }
            lastSinL = tl; lastSinR = tr;
        }

        // ------------------------------------------------------------ helpers
        void PlaceBat(Pose pose, bool swing, float swingT, float dashK, float slamK)
        {
            if (!Bat || !bones.TryGetValue("Hand_R", out var hand)) return;
            Vector3 dir = Body.TransformDirection(pose.dir);
            if (dead > 0) dir = Vector3.Slerp(dir, Body.right, dead);
            Vector3 side = Vector3.Cross(dir, Body.right);
            if (side.sqrMagnitude < 0.01f) side = Vector3.Cross(dir, Body.forward);
            Bat.position = hand.position - dir * 0.07f;
            Bat.rotation = Quaternion.LookRotation(side.normalized, dir);
            bool active = (swing && swingT > 0.2f && swingT < 0.72f) || slamK > 0.55f || dashK > 0.5f;
            SetTrails(active, (swing && swingT > 0.3f && swingT < 0.66f) || slamK > 0.8f);
        }

        void SetTrails(bool on, bool sparks)
        {
            if (BatTrails != null)
                foreach (var t in BatTrails)
                    if (t) { if (on && !t.emitting) t.Clear(); t.emitting = on; }
            if (BatSparks) { var em = BatSparks.emission; em.enabled = sparks; }
        }

        void ArmIK(string up, string low, string end, Vector3 target, float side, float weight = 1f)
        {
            if (!bones.TryGetValue(up, out var u) || !bones.TryGetValue(low, out var l) || !bones.TryGetValue(end, out var e)) return;
            if (weight < 0.999f) target = Vector3.Lerp(e.position, target, weight);
            Vector3 pole = u.position + Body.right * side * 0.8f - Body.up * 0.7f - Body.forward * 0.2f;
            TwoBone(u, l, e, target, pole);
        }

        void LegIK(bool left, Pose pose, float air, float rising, float falling, float dashK)
        {
            string sfx = left ? "_L" : "_R";
            if (!bones.TryGetValue("UpperLeg" + sfx, out var u) || !bones.TryGetValue("LowerLeg" + sfx, out var l) || !bones.TryGetValue("Foot" + sfx, out var f)) return;
            Vector3 p = left ? footRestL : footRestR;
            float t = cycle + (left ? 0 : 0.5f); t -= Mathf.Floor(t);
            float zs = k * amp * (0.7f + 0.5f * runBlend), ys = k * amp * (0.45f + 0.95f * runBlend);
            bool climbing = ClimbT >= 0;
            if (climbing) { p += (left ? climbFootL : climbFootR) * k; zs = ys = 0; }
            p.z += Sample(FootZ, t) * zs;
            p.y += Sample(FootY, t) * ys;
            // stance during swings: lead foot steps forward, the other braces back
            float st = pose.stance * (left ? 1 : -1);
            p.z += (st > 0 ? 0.24f * st : 0.16f * st) * k;
            p.x += (left ? -1 : 1) * 0.03f * Mathf.Abs(pose.stance) * k;
            // air: knees up while rising, legs reaching down while falling
            if (air > 0)
            {
                float lead = left ? 1 : 0.55f;
                p.y += (0.45f * rising * lead + 0.12f * falling) * k;
                p.z += (left ? 0.18f : -0.22f) * (rising + 0.4f * falling) * k;
            }
            if (dashK > 0) { p.z += (left ? 0.38f : -0.32f) * dashK * k; p.y += 0.12f * dashK * k; }
            Vector3 target = Body.TransformPoint(p);
            Vector3 pole = (u.position + target) * 0.5f + Body.forward * 1.5f;   // knee bends straight forward, never outward
            TwoBone(u, l, f, target, pole, -Body.right);   // hinge = -right → knee always forward, even when the foot kicks up behind
            // remove the twist that pelvis rotation / IK leave in the leg, so knees and toes always face forward
            Twist(u, l.position - u.position);
            l.rotation = Quaternion.FromToRotation(f.position - l.position, target - l.position) * l.rotation;
            Twist(l, f.position - l.position);
            // foot keeps its bind orientation relative to the body (toes always forward), then pitches with the gait
            if (footBind.TryGetValue(f, out var fb)) f.rotation = Body.rotation * fb;
            float toe = climbing ? 0 : air > 0 ? 25f * falling - 10f * rising : Sample(FootPitch, t) * amp;
            if (Mathf.Abs(toe) > 0.1f) f.rotation = Quaternion.AngleAxis(toe, Body.right) * f.rotation;
        }

        void Twist(Transform bone, Vector3 axis)
        {
            if (!fwdLocal.TryGetValue(bone, out var lf) || axis.sqrMagnitude < 1e-6f) return;
            axis.Normalize();
            // "front" of the bone = perpendicular to the bone inside the body's sagittal plane: stable even when the thigh points forward
            Vector3 cur = Vector3.ProjectOnPlane(bone.rotation * lf, axis), want = Vector3.Cross(axis, Body.right);
            if (cur.sqrMagnitude < 1e-4f || want.sqrMagnitude < 1e-4f) return;
            float ang = Vector3.SignedAngle(cur, want, axis);
            bone.rotation = Quaternion.AngleAxis(ang, axis) * bone.rotation;

        }


        /// <summary>Position-based two-bone IK: works regardless of bone axis conventions.</summary>
        static void TwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole, Vector3 hinge = default)
        {
            Vector3 A = a.position, B = b.position, C = c.position;
            float la = (B - A).magnitude, lb = (C - B).magnitude;
            if (la < 1e-4f || lb < 1e-4f) return;
            Vector3 toT = target - A;
            float d = Mathf.Clamp(toT.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
            Vector3 dirT = toT.normalized;
            float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2 * la * d), -1, 1);
            // legs pass a fixed hinge axis (knee only bends in the body's sagittal plane); arms use the pole
            Vector3 n = hinge != default ? Vector3.ProjectOnPlane(hinge, dirT) : Vector3.Cross(dirT, pole - A);
            if (n.sqrMagnitude < 1e-6f) n = Vector3.Cross(dirT, Vector3.up);
            n.Normalize();
            Vector3 elbowDir = Quaternion.AngleAxis(Mathf.Acos(cosA) * Mathf.Rad2Deg, n) * dirT;
            a.rotation = Quaternion.FromToRotation(B - A, elbowDir) * a.rotation;
            B = b.position; C = c.position;
            Vector3 handTarget = A + dirT * d;
            b.rotation = Quaternion.FromToRotation(C - B, handTarget - B) * b.rotation;
        }

        void Rot(string bone, float x, float y, float z)
        {
            if (!bones.TryGetValue(bone, out var t)) return;
            if (Mathf.Abs(x) > 0.01f) t.rotation = Quaternion.AngleAxis(x, Body.right) * t.rotation;
            if (Mathf.Abs(y) > 0.01f) t.rotation = Quaternion.AngleAxis(y, Body.up) * t.rotation;
            if (Mathf.Abs(z) > 0.01f) t.rotation = Quaternion.AngleAxis(z, Body.forward) * t.rotation;
        }

        void Dust(float size, int count)
        {
            Fx.Burst(Body.position + Vector3.up * 0.1f, new Color(0.85f, 0.78f, 0.95f, 0.75f), count, 2.6f * size, 0.55f * size, 0.5f, -0.05f, "fx_soft", false, new Color(1f, 0.7f, 0.95f, 0.6f), 0.35f);
        }

        void StepDust(bool left)
        {
            if (Time.time < nextDust || (!Sprinting && Speed < 6.5f)) return;
            nextDust = Time.time + 0.12f;
            if (!bones.TryGetValue(left ? "Foot_L" : "Foot_R", out var f)) return;
            Fx.Burst(new Vector3(f.position.x, Body.position.y + 0.08f, f.position.z), new Color(0.85f, 0.8f, 0.95f, 0.55f), 5, 1.4f, 0.4f, 0.4f, -0.05f, "fx_soft", false, null, 0.1f);
        }
    }
}
