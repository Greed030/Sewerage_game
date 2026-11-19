using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Sewer
{
    [RequireComponent(typeof(CharacterController))]
    public class Player : MonoBehaviour
    {
        public CharacterDef Def;
        public Health Hp;
        public Transform Cam;
        public ProceduralAnimator Anim;

        public float AbilityReadyAt;
        public float AbilityCooldown => Def.Cooldown * cooldownMul;
        public bool Invisible => Time.time < invisUntil;
        public bool Controllable => !Hp.Dead && !SewerGame.Instance.Finished && SewerGame.Instance.Playing;

        CharacterController cc;
        float yVel, cooldownMul = 1f, speedMul = 1f;
        float attackReadyAt, invisUntil, dashUntil, lastAttack = -10, nextGhost;
        int combo;
        static bool autoRun;
        Vector3 dashDir, knock;
        readonly HashSet<Health> dashHits = new();
        Vector3 checkpoint;
        int checkpointLevel;
        Renderer[] renderers;
        Material[][] origMats;
        static Material ghostMat;
        bool ghosted;
        // climbing / mantling onto ledges
        bool climbing; float climbT, climbDur;
        Vector3 climbFrom, climbUp, climbTo, climbEdge, climbN;
        public bool Climbing => climbing;

        static TrailRenderer MakeTrail(Transform bat, float along, float time, float width, string tex, bool additive, bool main)
        {
            var t = new GameObject(main ? "Swoosh" : "Smear").transform; t.SetParent(bat, false); t.localPosition = new Vector3(0, along, 0);
            var tr = t.gameObject.AddComponent<TrailRenderer>();
            tr.time = time; tr.minVertexDistance = 0.03f; tr.emitting = false;
            tr.widthCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(0.18f, width), new Keyframe(0.5f, width * 0.7f), new Keyframe(1, 0));
            var grad = new Gradient();
            if (main)
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.85f, 0.3f), new GradientAlphaKey(0.4f, 0.7f), new GradientAlphaKey(0, 1) });
            else
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                             new[] { new GradientAlphaKey(0.35f, 0), new GradientAlphaKey(0, 1) });
            tr.colorGradient = grad;
            tr.textureMode = LineTextureMode.Stretch;
            tr.sharedMaterial = Fx.FxMat(tex, additive);
            tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tr.numCapVertices = 2;
            return tr;
        }

        static ParticleSystem MakeSparks(Transform bat, float along)
        {
            var g = new GameObject("Sparks"); g.transform.SetParent(bat, false); g.transform.localPosition = new Vector3(0, along, 0);
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.26f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
            main.gravityModifier = 0.2f; main.maxParticles = 120;
            var em = ps.emission; em.rateOverTime = 0; em.rateOverDistance = 22; em.enabled = false;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.08f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
            var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = Fx.FxMat("fx_star", true); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        public static Player Spawn(CharacterDef def, Vector3 pos, float yaw)
        {
            var g = new GameObject($"Player_{def.Name}");
            g.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            var cc = g.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.4f; cc.center = new Vector3(0, 0.92f, 0);
            cc.stepOffset = 0.55f; cc.slopeLimit = 50f; cc.skinWidth = 0.05f;
            var p = g.AddComponent<Player>();
            p.Def = def;
            var model = Roster.SpawnModel(def, g.transform);
            p.Anim = model.gameObject.AddComponent<ProceduralAnimator>();
            p.Anim.Body = g.transform;
            // Y2K baseball bat, driven by the animator (position = right hand)
            var batHolder = new GameObject("Bat").transform;
            batHolder.SetParent(g.transform, false);
            var bat = PropLib.SpawnMob("Weapon_Bat", batHolder);
            if (bat)
            {
                batHolder.localScale = Vector3.one * 1.55f;   // big cartoon bat
                p.Anim.Bat = batHolder;
                p.Anim.Accent = def.Accent;
                p.Anim.BatTrails = new[] { MakeTrail(batHolder, 1.0f, 0.14f, 0.3f, "fx_swoosh", true, true) };
            }
            p.Hp = g.AddComponent<Health>();
            p.Hp.ResetHealth(def.MaxHp);
            p.Hp.Died += p.OnDied;
            p.Hp.Damaged += _ => { p.Anim?.Hurt(); SewerGame.Instance?.Shake(0.12f); };
            p.checkpoint = pos;
            Geo.SetLayer(g, Geo.CamIgnoreLayer);
            // small personal light so the character is readable in the dark
            var l = Geo.PointLight(g.transform, pos + Vector3.up * 2.6f, new Color(1f, 0.92f, 0.8f), 7f, 1.6f);
            l.transform.SetParent(g.transform, true);
            return p;
        }

        void Awake() => cc = GetComponent<CharacterController>();

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (climbing) { ClimbStep(); UpdateClimbAnim(); UpdateInvisVisual(); return; }
            Vector3 move = Vector3.zero;
            bool sprint = false;

            if (Controllable && kb != null)
            {
                Vector2 m = new Vector2((kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0),
                                        (kb.wKey.isPressed ? 1 : 0) - (kb.sKey.isPressed ? 1 : 0));
                var gp = Gamepad.current;
                if (gp != null && gp.leftStick.ReadValue().sqrMagnitude > 0.04f) m = gp.leftStick.ReadValue();
                if (kb.f7Key.wasPressedThisFrame) { autoRun = !autoRun; Hud.Message(autoRun ? "Auto-run (F7)" : "Auto-run off"); }
                if (autoRun && m.sqrMagnitude < 0.01f) m = Vector2.up;   // debug: keep running to inspect the run cycle
                if (kb.f8Key.wasPressedThisFrame) DebugJump();
                if (kb.f9Key.wasPressedThisFrame) DebugDoor();
                Vector3 f = Cam ? Vector3.ProjectOnPlane(Cam.forward, Vector3.up).normalized : transform.forward;
                Vector3 r = Vector3.Cross(Vector3.up, f);
                move = Vector3.ClampMagnitude(f * m.y + r * m.x, 1f);
                sprint = kb.leftShiftKey.isPressed || (gp != null && gp.leftStickButton.isPressed);

                bool jumpPressed = kb.spaceKey.wasPressedThisFrame || (gp != null && gp.buttonSouth.wasPressedThisFrame);
                Vector3 want = move.sqrMagnitude > 0.01f ? move : transform.forward;
                if (jumpPressed && TryClimb(want, 0.9f, 3.0f)) { UpdateClimbAnim(); UpdateInvisVisual(); return; }
                if (cc.isGrounded && jumpPressed) yVel = 6.2f;
                // in the air: pushing into a ledge grabs it
                if (!cc.isGrounded && move.sqrMagnitude > 0.1f && yVel < 4f && TryClimb(move, 0.3f, 2.2f)) { UpdateClimbAnim(); UpdateInvisVisual(); return; }
                if ((mouse != null && mouse.leftButton.wasPressedThisFrame) || (gp != null && gp.buttonWest.wasPressedThisFrame))
                    Attack();
                if (kb.eKey.wasPressedThisFrame || (mouse != null && mouse.rightButton.wasPressedThisFrame) ||
                    (gp != null && gp.buttonNorth.wasPressedThisFrame))
                    UseAbility();
            }

            float speed = Def.Speed * speedMul * (sprint ? 1.5f : 1f);
            Vector3 horiz = move * speed;

            if (Time.time < dashUntil)
            {
                horiz = dashDir * 24f;
                if (Time.time > nextGhost) { nextGhost = Time.time + 0.035f; Fx.Ghost(transform, Def.Accent); }
                foreach (var e in Enemy.All.ToArray())
                {
                    if (e == null || e.Hp.Dead || dashHits.Contains(e.Hp)) continue;
                    if ((e.transform.position - transform.position).sqrMagnitude < 2.2f * 2.2f)
                    {
                        dashHits.Add(e.Hp);
                        e.Hp.Hit(30);
                        e.Knock(dashDir * 9f);
                        Fx.Hit(e.transform.position + Vector3.up * 0.8f, Def.Accent, true);
                    }
                }
            }

            if (cc.isGrounded && yVel < 0) yVel = -2f;
            yVel += -20f * Time.deltaTime;
            knock = Vector3.MoveTowards(knock, Vector3.zero, 25f * Time.deltaTime);
            cc.Move((horiz + knock + Vector3.up * yVel) * Time.deltaTime);

            // face movement (or aim direction while attacking)
            Vector3 face = Time.time < attackReadyAt - 0.2f && Cam ? Vector3.ProjectOnPlane(Cam.forward, Vector3.up) : horiz;
            if (face.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(face.normalized), 14f * Time.deltaTime);

            if (Anim)
            {
                Anim.Speed = new Vector3(cc.velocity.x, 0, cc.velocity.z).magnitude;
                Anim.Grounded = cc.isGrounded;
                Anim.VerticalSpeed = yVel;
                Anim.Sprinting = Keyboard.current != null && Keyboard.current.leftShiftKey.isPressed;
                Anim.ClimbT = -1;
            }

            // checkpoints: every time you reach a deeper level
            if (cc.isGrounded && !Hp.Dead)
            {
                int lvl = LevelBuilder.LevelOf(transform.position.y);
                if (lvl > checkpointLevel)
                {
                    checkpointLevel = lvl;
                    checkpoint = transform.position;
                    Hud.Message(lvl < 3 ? $"Ring {lvl + 1}. Checkpoint" : "Crown Hall!");
                }
            }
            if (transform.position.y < -110) Teleport(checkpoint);

            // debug: F3 — defeat the nearest boss on this level, F4 — jump to the nearest hatch room on this level
            if (kb != null && kb.f3Key.wasPressedThisFrame)
            {
                Boss nb = null; float nd = float.MaxValue;
                foreach (var e in Enemy.All)
                    if (e is Boss bb && !bb.Hp.Dead && Mathf.Abs(bb.transform.position.y - transform.position.y) < 6f)
                    {
                        float dd = (bb.transform.position - transform.position).sqrMagnitude;
                        if (dd < nd) { nd = dd; nb = bb; }
                    }
                if (nb) nb.Hp.Hit(99999);
            }
            if (kb != null && kb.f4Key.wasPressedThisFrame)
            {
                int lv = LevelBuilder.LevelOf(transform.position.y);
                RoomInfo best = null; float bd = float.MaxValue;
                foreach (var r in SewerGame.Instance.Level.Rooms)
                {
                    if (r.Level != lv || r.Kind != RoomKind.Gate) continue;
                    float dd = (r.Center - transform.position).sqrMagnitude;
                    if (dd < bd) { bd = dd; best = r; }
                }
                if (best != null)
                {
                    Vector3 o = Vector3.ProjectOnPlane(best.Center, Vector3.up).normalized;
                    bool inside = (best.Center - transform.position).magnitude < best.Size / 2;
                    if (inside && best.Gate && best.Gate.IsOpen) Teleport(best.Center + Vector3.up * 0.5f); // drop into the shaft
                    else
                    {
                        Teleport(best.Center + o * (best.Size / 2 - 1.5f));
                        transform.rotation = Quaternion.LookRotation(-o);
                    }
                }
            }

            // debug: F2 — teleport into the nearest boss room on this level (for testing)
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                int lv = LevelBuilder.LevelOf(transform.position.y);
                RoomInfo best = null; float bd = float.MaxValue;
                foreach (var r in SewerGame.Instance.Level.Rooms)
                {
                    if (r.Level != lv || r.Kind == RoomKind.Arena) continue;
                    float dd = (r.Center - transform.position).sqrMagnitude;
                    if (dd < bd) { bd = dd; best = r; }
                }
                if (best != null)
                {
                    Vector3 o = Vector3.ProjectOnPlane(best.Center, Vector3.up).normalized;
                    Teleport(best.Center + o * (best.Size / 2 - 2f));
                    transform.rotation = Quaternion.LookRotation(-o);
                }
            }

            UpdateInvisVisual();
        }

        void Attack()
        {
            if (Time.time < attackReadyAt) return;
            combo = Time.time - lastAttack < 0.8f ? (combo + 1) % 3 : 0;
            lastAttack = Time.time;
            float dur = combo == 2 ? 0.55f : 0.42f;
            attackReadyAt = Time.time + dur * 0.8f;
            Anim?.Swing(combo, dur);
            Vector3 fwd = Cam ? Vector3.ProjectOnPlane(Cam.forward, Vector3.up).normalized : transform.forward;
            transform.rotation = Quaternion.LookRotation(fwd);
            StartCoroutine(HitRoutine(combo, dur * (combo == 2 ? 0.5f : 0.46f), fwd));
        }

        IEnumerator HitRoutine(int c, float delay, Vector3 fwd)
        {
            yield return new WaitForSeconds(delay);
            if (Hp.Dead) yield break;
            fwd = transform.forward;
            float dmg = c == 2 ? 32f : 20f, knockF = c == 2 ? 11f : 6f, reach = c == 2 ? 2.3f : 2.0f;
            Vector3 c0 = transform.position + Vector3.up * 0.9f + fwd * 1.15f;
            bool any = false;
            foreach (var e in Enemy.All.ToArray())
            {
                if (e == null || e.Hp.Dead) continue;
                var col = e.GetComponent<Collider>();
                Vector3 closest = col ? col.ClosestPoint(c0) : e.transform.position;
                Vector3 to = closest - transform.position; to.y = 0;
                if ((closest - c0).sqrMagnitude < reach * reach && Vector3.Dot(to.normalized, fwd) > -0.2f)
                {
                    e.Hp.Hit(dmg);
                    Vector3 side = c == 0 ? -transform.right : c == 1 ? transform.right : Vector3.zero;
                    e.Knock(fwd * knockF + side * 3f);
                    Fx.Hit(Vector3.Lerp(closest, c0, 0.3f), Def.Accent, c == 2);
                    any = true;
                }
            }
            if (c == 2)
            {
                Fx.Burst(transform.position + fwd * 1.6f + Vector3.up * 0.1f, new Color(0.55f, 0.5f, 0.45f, 0.8f), 10, 3f, 0.6f, 0.5f, 0.1f, "fx_soft");
                SewerGame.Instance.Shake(any ? 0.3f : 0.12f);
            }
            else if (any) SewerGame.Instance.Shake(0.15f);
        }

        void UseAbility()
        {
            if (Time.time < AbilityReadyAt) return;
            AbilityReadyAt = Time.time + AbilityCooldown;
            Vector3 fwd = transform.forward;
            switch (Def.Ability)
            {
                case Ability.Dash:
                    var d = Cam ? Vector3.ProjectOnPlane(Cam.forward, Vector3.up).normalized : fwd;
                    dashDir = d; dashUntil = Time.time + 0.28f; dashHits.Clear();
                    transform.rotation = Quaternion.LookRotation(d);
                    Anim?.DashPose(0.28f);
                    Fx.Burst(transform.position + Vector3.up * 0.2f, new Color(0.8f, 0.75f, 0.7f, 0.8f), 12, 3f, 0.7f, 0.45f, 0.1f, "fx_soft");
                    break;
                case Ability.Invisibility:
                    invisUntil = Time.time + 5f;
                    Hud.Message("You are invisible!");
                    Anim?.AbilityPose(Ability.Invisibility, 0.7f);
                    Fx.Burst(transform.position + Vector3.up * 1f, Mat.Hex("#B46BFF"), 26, 2.5f, 1.1f, 0.9f, -0.2f, "fx_soft", false, Mat.Hex("#3B3A4A"), 0.6f);
                    break;
                case Ability.Slam:
                    yVel = 7f;
                    Anim?.AbilityPose(Ability.Slam, 3f);
                    StartCoroutine(SlamRoutine());
                    break;
                case Ability.Flash:
                    StartCoroutine(Fx.Flash(transform.position + Vector3.up * 1.5f, new Color(1f, 0.95f, 0.6f), 30f, 0.35f));
                    Anim?.AbilityPose(Ability.Flash, 0.7f);
                    Fx.Burst(transform.position + Vector3.up * 1.8f, Mat.Hex("#FFE45C"), 30, 12f, 0.5f, 0.6f, 0f, "fx_star", true, Color.white, 0.3f);
                    foreach (var e in Enemy.All.ToArray())
                        if (e && !e.Hp.Dead && (e.transform.position - transform.position).sqrMagnitude < 9f * 9f)
                            e.Stun(3f);
                    break;
            }
        }

        IEnumerator SlamRoutine()
        {
            yield return new WaitForSeconds(0.3f);
            while (!cc.isGrounded) { yVel = Mathf.Min(yVel, -14f); yield return null; }
            Anim?.SlamLand();
            StartCoroutine(Fx.Shockwave(transform.position + Vector3.up * 0.1f, 5.5f, new Color(1f, 0.5f, 0.3f)));
            Fx.Burst(transform.position + Vector3.up * 0.3f, new Color(0.6f, 0.55f, 0.5f, 0.85f), 28, 7f, 1.0f, 0.7f, 0.2f, "fx_soft", false, null, 1.2f);
            Fx.Burst(transform.position + Vector3.up * 0.3f, Mat.Hex("#FFD23F"), 16, 9f, 0.35f, 0.5f, 1.2f, "fx_star", true);
            Fx.Pop(transform.position + transform.forward * 1.2f + Vector3.up * 1.5f, "hit_wham", 2.2f);
            foreach (var e in Enemy.All.ToArray())
            {
                if (!e || e.Hp.Dead) continue;
                Vector3 to = e.transform.position - transform.position;
                if (to.sqrMagnitude < 5.5f * 5.5f)
                {
                    e.Hp.Hit(40);
                    e.Knock(to.normalized * 12f + Vector3.up * 2f);
                }
            }
            SewerGame.Instance.Shake(0.35f);
        }

        public void Knock(Vector3 v) => knock += Vector3.ProjectOnPlane(v, Vector3.up);

        public void ApplyReward(RewardType r)
        {
            switch (r)
            {
                case RewardType.MaxHealth:
                    Hp.ResetHealth(Hp.Max + 30);
                    Hud.Message("Reward: +30 max health and full heal");
                    break;
                case RewardType.AbilityBoost:
                    cooldownMul *= 0.6f; AbilityReadyAt = 0;
                    Hud.Message($"Reward: \"{Def.AbilityName}\" recharges faster");
                    break;
                case RewardType.SpeedBoost:
                    speedMul *= 1.15f; Hp.Heal(40);
                    Hud.Message("Reward: +15% speed and a heal");
                    break;
            }
        }

        void OnDied()
        {
            Hud.Message("You got eaten... respawning");
            Anim?.SetDead(true);
            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            yield return new WaitForSeconds(2.5f);
            Teleport(checkpoint);
            Hp.ResetHealth(Hp.Max);
            Anim?.SetDead(false);
        }

        static int debugFeature = -1;
        /// <summary>Debug (F8): jump in front of the next tunnel feature (fan, grate, split path).</summary>
        void DebugJump()
        {
            var fs = LevelBuilder.Features;
            var br = SewerGame.Instance.Level.Breaches;
            debugFeature = (debugFeature + 1) % (fs.Length + br.Count);
            var cam0 = FindFirstObjectByType<ThirdPersonCamera>();
            if (debugFeature >= fs.Length)
            {
                var b = br[debugFeature - fs.Length];
                var bd = LevelBuilder.Rings[b.Ring];
                Teleport(Geo.Polar(b.Angle, bd.R + b.Side * 2.6f, bd.Y));
                transform.rotation = Quaternion.LookRotation(Geo.Out(b.Angle) * b.Side);
                if (cam0) cam0.Snap(transform.eulerAngles.y);
                Hud.Message($"F8: wall breach (ring {b.Ring + 1})");
                return;
            }
            var f = fs[debugFeature];
            var d = LevelBuilder.Rings[f.ring];
            float back = (f.kind == FeatureKind.Divider ? 22f : 10f) / d.R * Mathf.Rad2Deg;
            float a = f.angle - back;
            Vector3 p = Geo.Polar(a, d.R + 2.6f, d.Y);
            Vector3 tan = new Vector3(-Mathf.Sin(a * Mathf.Deg2Rad), 0, Mathf.Cos(a * Mathf.Deg2Rad));
            Teleport(p);
            transform.rotation = Quaternion.LookRotation(tan);
            var cam = FindFirstObjectByType<ThirdPersonCamera>();
            if (cam) cam.Snap(transform.eulerAngles.y);
            Hud.Message($"F8: {f.kind} (ring {f.ring + 1})");
        }

        // ------------------------------------------------------------ climbing
        const int ClimbMask = Physics.DefaultRaycastLayers & ~(1 << Geo.CamIgnoreLayer);

        bool TryClimb(Vector3 dir, float minH, float maxH)
        {
            if (climbing || Time.time < dashUntil) return false;
            dir = Vector3.ProjectOnPlane(dir, Vector3.up);
            if (dir.sqrMagnitude < 1e-4f) return false;
            dir.Normalize();
            Vector3 feet = transform.position;
            float r = cc.radius;
            if (!Physics.Raycast(feet + Vector3.up * 0.6f, dir, out var wall, r + 0.7f, ClimbMask, QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(wall.normal.y) > 0.35f) return false;
            Vector3 n = Vector3.ProjectOnPlane(wall.normal, Vector3.up).normalized;
            if (Vector3.Dot(n, -dir) < 0.5f) return false;                       // must face the wall
            Vector3 probe = wall.point - n * (r + 0.3f);
            probe.y = feet.y + maxH + 0.4f;
            if (!Physics.Raycast(probe, Vector3.down, out var top, maxH + 0.4f - minH + 0.05f, ClimbMask, QueryTriggerInteraction.Ignore)) return false;
            if (top.normal.y < 0.7f) return false;
            float h = top.point.y - feet.y;
            if (h < minH || h > maxH) return false;
            Vector3 dest = top.point + Vector3.up * 0.06f;
            if (Physics.CheckCapsule(dest + Vector3.up * (r + 0.1f), dest + Vector3.up * (cc.height - r), r * 0.85f, ClimbMask, QueryTriggerInteraction.Ignore)) return false;

            climbing = true; climbT = 0;
            climbDur = 0.32f + 0.2f * h;
            climbFrom = feet;
            climbN = n;
            climbEdge = new Vector3(wall.point.x, top.point.y, wall.point.z);
            climbUp = new Vector3(feet.x, dest.y, feet.z) + n * 0.05f;
            climbTo = dest;
            cc.enabled = false;
            yVel = 0; knock = Vector3.zero;
            transform.rotation = Quaternion.LookRotation(-n);
            return true;
        }

        void ClimbStep()
        {
            climbT += Time.deltaTime / climbDur;
            const float split = 0.62f;
            Vector3 p;
            if (climbT < split) p = Vector3.Lerp(climbFrom, climbUp, Mathf.SmoothStep(0, 1, climbT / split));
            else p = Vector3.Lerp(climbUp, climbTo, Mathf.SmoothStep(0, 1, (climbT - split) / (1 - split)));
            transform.position = p;
            if (climbT >= 1f)
            {
                climbing = false;
                transform.position = climbTo;
                cc.enabled = true;
                yVel = -2f;
            }
        }

        void UpdateClimbAnim()
        {
            if (!Anim) return;
            Anim.ClimbT = climbing ? Mathf.Clamp01(climbT) : -1;
            Anim.ClimbEdge = climbEdge; Anim.ClimbNormal = climbN;
            Anim.Grounded = true; Anim.VerticalSpeed = 0;
        }

        static int debugDoor = -1;
        /// <summary>Debug (F9): stand in front of the next room doorway / arrival passage in the ring tunnels.</summary>
        void DebugDoor()
        {
            var list = new List<(int ring, float angle, float side)>();
            for (int r = 0; r < LevelBuilder.Rings.Length; r++)
            {
                foreach (var (ra, _) in LevelBuilder.Rings[r].Rooms) list.Add((r, ra, -1));
                foreach (float ar in LevelBuilder.Rings[r].Arrivals) list.Add((r, ar, 1));
            }
            debugDoor = (debugDoor + 1) % list.Count;
            var (ring, ang, side) = list[debugDoor];
            var d = LevelBuilder.Rings[ring];
            float off = 6f / d.R * Mathf.Rad2Deg;
            Teleport(Geo.Polar(ang - off, d.R + side * 2.0f, d.Y));
            Vector3 look = Geo.Polar(ang, d.R + side * 7f, d.Y) - transform.position; look.y = 0;
            transform.rotation = Quaternion.LookRotation(look);
            var cam = FindFirstObjectByType<ThirdPersonCamera>();
            if (cam) cam.Snap(transform.eulerAngles.y);
            Hud.Message($"F9: {(side < 0 ? "room door" : "passage from above")} (ring {ring + 1})");
        }

        void Teleport(Vector3 p)
        {
            cc.enabled = false;
            transform.position = p + Vector3.up * 0.2f;
            cc.enabled = true;
            yVel = 0; knock = Vector3.zero;
        }

        /// <summary>Invisible = see-through ghost (you still see your own swings), enemies can't see you.</summary>
        void UpdateInvisVisual()
        {
            if (renderers == null)
            {
                var list = new List<Renderer>();
                foreach (var r in GetComponentsInChildren<Renderer>(true))
                    if (r is MeshRenderer || r is SkinnedMeshRenderer) list.Add(r);
                renderers = list.ToArray();
                origMats = new Material[renderers.Length][];
            }
            bool inv = Invisible;
            bool flicker = inv && invisUntil - Time.time < 1.2f && Mathf.Repeat(Time.time * 8f, 1f) > 0.5f;
            bool ghost = inv && !flicker;
            if (ghost)
            {
                float pulse = 0.22f + 0.08f * Mathf.Sin(Time.time * 6f);
                GhostMat().SetColor("_BaseColor", new Color(0.78f, 0.62f, 1f, pulse));
            }
            if (ghost == ghosted) return;
            ghosted = ghost;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                if (ghost)
                {
                    origMats[i] = renderers[i].sharedMaterials;   // whatever is current (toon or not)
                    var m = new Material[origMats[i].Length];
                    for (int k = 0; k < m.Length; k++) m[k] = GhostMat();
                    renderers[i].sharedMaterials = m;
                }
                else renderers[i].sharedMaterials = origMats[i];
            }
        }

        static Material GhostMat()
        {
            if (ghostMat) return ghostMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            ghostMat = new Material(sh) { name = "PlayerGhost" };
            ghostMat.SetFloat("_Surface", 1); ghostMat.SetFloat("_Blend", 0);
            ghostMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ghostMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ghostMat.SetFloat("_ZWrite", 0);
            ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMat.renderQueue = 3000;
            ghostMat.SetColor("_BaseColor", new Color(0.78f, 0.62f, 1f, 0.25f));
            return ghostMat;
        }
    }
}
