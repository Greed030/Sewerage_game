using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sewer
{
    /// <summary>Tiny primitive-based effects.</summary>
    public static class Fx
    {
        public static IEnumerator Flash(Vector3 pos, Color c, float range, float time)
        {
            var g = new GameObject("FlashFx");
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = range;
            var orb = Geo.Prim(PrimitiveType.Sphere, g.transform, "Orb", Vector3.zero, Vector3.one, Mat.Get(c, 0.9f, c * 4f));
            for (float t = 0; t < 1; t += Time.deltaTime / time)
            {
                l.intensity = Mathf.Lerp(40f, 0f, t);
                orb.transform.localScale = Vector3.one * Mathf.Lerp(0.5f, range * 0.5f, t);
                yield return null;
            }
            Object.Destroy(g);
        }

        public static IEnumerator Shockwave(Vector3 pos, float radius, Color c)
        {
            var g = Geo.Prim(PrimitiveType.Cylinder, null, "ShockFx", pos, new Vector3(0.2f, 0.03f, 0.2f), Mat.Get(c, 0.5f, c * 0.6f));
            for (float t = 0; t < 1; t += Time.deltaTime / 0.3f)
            {
                float d = Mathf.Lerp(0.2f, radius * 2f, t);
                g.transform.localScale = new Vector3(d, 0.03f * (1 - t) + 0.005f, d);
                yield return null;
            }
            Object.Destroy(g);
        }

        public static GameObject Telegraph(Vector3 pos, float radius)
        {
            return Geo.Prim(PrimitiveType.Cylinder, null, "TelegraphFx", pos, new Vector3(radius * 2f, 0.01f, radius * 2f),
                Mat.Get(new Color(0.6f, 0.05f, 0.03f), 0.2f, new Color(0.9f, 0.1f, 0.05f)));
        }

        public static IEnumerator StunStars(Transform target, float time, float height)
        {
            var g = new GameObject("StunFx").transform;
            var mat = Mat.Get(Mat.Hex("#FFE14D"), 0.8f, Mat.Hex("#A08800"));
            for (int i = 0; i < 3; i++)
                Geo.Prim(PrimitiveType.Sphere, g, "Star", Quaternion.Euler(0, i * 120, 0) * Vector3.forward * 0.5f, Vector3.one * 0.18f, mat);
            for (float t = 0; t < time; t += Time.deltaTime)
            {
                if (target == null) break;
                g.position = target.position + Vector3.up * height;
                g.Rotate(0, 360 * Time.deltaTime, 0);
                yield return null;
            }
            Object.Destroy(g.gameObject);
        }

        // ================================================================ particles / pops / ghosts
        static readonly Dictionary<string, Material> fxMats = new();

        /// <summary>Unlit particle/billboard material for Resources/SewerFx/{tex}. additive = glowy sparks.</summary>
        public static Material FxMat(string tex, bool additive = false)
        {
            string key = tex + additive;
            if (fxMats.TryGetValue(key, out var m) && m) return m;
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            m = new Material(sh) { name = "Fx_" + tex };
            var t = Resources.Load<Texture2D>("SewerFx/" + tex);
            if (t) { m.SetTexture("_BaseMap", t); m.mainTexture = t; }
            m.SetFloat("_Surface", 1); m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent + 5;
            fxMats[key] = m;
            return m;
        }

        /// <summary>One-shot particle burst (auto-destroys).</summary>
        public static ParticleSystem Burst(Vector3 pos, Color c, int count, float speed, float size, float life = 0.6f,
                                           float gravity = 1f, string tex = "fx_blob", bool additive = false, Color? c2 = null, float spread = 0.2f)
        {
            var g = new GameObject("BurstFx");
            g.transform.position = pos;
            var ps = g.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f; main.loop = false; main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = c2.HasValue ? new ParticleSystem.MinMaxGradient(c, c2.Value) : new ParticleSystem.MinMaxGradient(c);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 4;
            main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, (short)count) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = spread;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.EaseInOut(0, 1, 1, 0));
            var col = ps.colorOverLifetime; col.enabled = true;
            var gr = new Gradient(); gr.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                                                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, 0.6f), new GradientAlphaKey(0, 1) });
            col.color = gr;
            var r = g.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = FxMat(tex, additive);
            r.shadowCastingMode = ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }

        static readonly string[] HitWords = { "hit_pow", "hit_bonk", "hit_bam", "hit_wham" };

        /// <summary>Comic-book word that pops up at a hit point and faces the camera.</summary>
        public static void Pop(Vector3 pos, string word = null, float size = 1.3f)
        {
            if (SewerGame.Instance == null) return;
            SewerGame.Instance.StartCoroutine(PopRoutine(pos, word ?? HitWords[Random.Range(0, HitWords.Length)], size));
        }

        static IEnumerator PopRoutine(Vector3 pos, string word, float size)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(q.GetComponent<Collider>());
            q.name = "PopFx"; q.layer = 2;
            var r = q.GetComponent<MeshRenderer>(); r.sharedMaterial = FxMat(word); r.shadowCastingMode = ShadowCastingMode.Off;
            float rot = Random.Range(-15f, 15f);
            Vector3 drift = new Vector3(Random.Range(-0.4f, 0.4f), 1.1f, 0);
            for (float t = 0; t < 1; t += Time.deltaTime / 0.55f)
            {
                var cam = Camera.main;
                if (cam) q.transform.rotation = Quaternion.LookRotation(q.transform.position - cam.transform.position) * Quaternion.Euler(0, 0, rot);
                float s = t < 0.18f ? Mathf.Lerp(0.2f, 1.25f, t / 0.18f) : t < 0.3f ? Mathf.Lerp(1.25f, 1f, (t - 0.18f) / 0.12f) : Mathf.Lerp(1f, 0.6f, (t - 0.3f) / 0.7f);
                q.transform.localScale = Vector3.one * size * s;
                q.transform.position = pos + drift * t * 0.6f;
                yield return null;
            }
            Object.Destroy(q);
        }

        /// <summary>Hit spark: stars + comic word + tiny hit-stop.</summary>
        public static void Hit(Vector3 pos, Color c, bool big = false)
        {
            Burst(pos, Color.white, big ? 14 : 8, big ? 9 : 6, big ? 0.45f : 0.3f, 0.35f, 0.3f, "fx_star", true, c);
            Burst(pos, c, big ? 10 : 6, 4, 0.25f, 0.5f, 1.5f);
            Pop(pos + Vector3.up * 0.4f, null, big ? 1.8f : 1.25f);
            if (SewerGame.Instance) SewerGame.Instance.HitStop(big ? 0.09f : 0.05f);
        }

        /// <summary>Flat slime/goo splat on the floor.</summary>
        public static void Splat(Vector3 pos, float size, string tex = "slime_a")
        {
            var q = PropLib.DecalQuad(null, tex, pos + Vector3.up * 0.03f, Vector3.down, Vector3.forward, Vector2.one * size);
            q.transform.Rotate(0, 0, Random.Range(0, 360), Space.Self);
            Object.Destroy(q, 12f);
        }

        /// <summary>Translucent afterimage of a character (for the dash).</summary>
        public static void Ghost(Transform root, Color c, float life = 0.28f)
        {
            if (SewerGame.Instance == null) return;
            var holder = new GameObject("GhostFx");
            var mat = new Material(FxMat("fx_soft", true)) { name = "Ghost" };
            mat.SetTexture("_BaseMap", Texture2D.whiteTexture);
            mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.45f));
            var meshes = new List<Mesh>();
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled) continue;
                var m = new Mesh(); smr.BakeMesh(m); meshes.Add(m);
                var g = new GameObject("g"); g.transform.SetParent(holder.transform, false);
                g.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                g.transform.localScale = smr.transform.lossyScale;
                g.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            SewerGame.Instance.StartCoroutine(GhostFade(holder, mat, meshes, c, life));
        }

        static IEnumerator GhostFade(GameObject holder, Material mat, List<Mesh> meshes, Color c, float life)
        {
            for (float t = 0; t < 1; t += Time.deltaTime / life)
            {
                mat.SetColor("_BaseColor", new Color(c.r, c.g, c.b, 0.45f * (1 - t)));
                yield return null;
            }
            Object.Destroy(holder);
            Object.Destroy(mat);
            foreach (var m in meshes) Object.Destroy(m);
        }
    }
}
