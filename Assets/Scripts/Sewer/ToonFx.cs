using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Sewer
{
    /// <summary>
    /// Cartoon look: swaps every opaque URP/Lit material for Sewer/Toon (cel light, rim, outline)
    /// and adds a post-process volume (bloom, vignette, colour pop, slight Y2K chromatic aberration).
    /// F6 toggles the toon look on/off for comparison.
    /// </summary>
    public class ToonFx : MonoBehaviour
    {
        public static ToonFx Instance;
        public bool ToonOn = true;
        [Header("Outline widths (metres)")]
        public float PropOutline = 0.022f, MobOutline = 0.03f, PlayerOutline = 0.025f;

        static Shader toon;
        readonly Dictionary<(Material, float), Material> cache = new();
        readonly List<(Renderer r, Material[] orig, Material[] toon)> swapped = new();
        Volume volume;

        public static ToonFx Setup(GameObject host)
        {
            var fx = host.GetComponent<ToonFx>();
            if (fx == null) fx = host.AddComponent<ToonFx>();
            Instance = fx;
            fx.BuildVolume();
            fx.ApplyAll();
            return fx;
        }

        /// <summary>Convert everything currently in the scene. Safe to call again (already-converted renderers are skipped).</summary>
        public void ApplyAll()
        {
            if (toon == null) toon = Shader.Find("Sewer/Toon");
            if (toon == null) { Debug.LogWarning("Sewer/Toon shader not found"); return; }
            var done = new HashSet<Renderer>();
            foreach (var s in swapped) done.Add(s.r);
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (done.Contains(r) || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                float w = OutlineFor(r);
                var orig = r.sharedMaterials;
                var mats = new Material[orig.Length];
                bool any = false;
                for (int i = 0; i < orig.Length; i++)
                {
                    mats[i] = Convert(orig[i], w);
                    any |= mats[i] != orig[i];
                }
                if (!any) continue;
                swapped.Add((r, orig, mats));
                if (ToonOn) r.sharedMaterials = mats;
            }
        }

        /// <summary>Convert one freshly spawned object (cheap, no scene scan).</summary>
        public static void Apply(GameObject go)
        {
            var fx = Instance;
            if (fx == null || go == null || toon == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                float w = fx.OutlineFor(r);
                var orig = r.sharedMaterials; var mats = new Material[orig.Length]; bool any = false;
                for (int i = 0; i < orig.Length; i++) { mats[i] = fx.Convert(orig[i], w); any |= mats[i] != orig[i]; }
                if (!any) continue;
                fx.swapped.Add((r, orig, mats));
                if (fx.ToonOn) r.sharedMaterials = mats;
            }
        }

        float OutlineFor(Renderer r)
        {
            var t = r.transform;
            if (t.GetComponentInParent<Enemy>()) return MobOutline;
            if (t.GetComponentInParent<Player>()) return PlayerOutline;
            // dressing props & pickups get an outline; the tunnel/room shell does not
            for (var p = t; p != null; p = p.parent)
                if (p.name == "Dressing" || p.name == "Crown" || p.name.StartsWith("Chest") || p.name == "Props") return PropOutline;
            if (r.name.StartsWith("Decal") || r.name == "SlimeStream") return 0f;
            return 0f;
        }

        Material Convert(Material src, float outline)
        {
            if (src == null || src.shader == null) return src;
            string sn = src.shader.name;
            if (!sn.StartsWith("Universal Render Pipeline/Lit") && !sn.StartsWith("Universal Render Pipeline/Simple Lit")) return src;
            if (src.HasProperty("_Surface") && src.GetFloat("_Surface") > 0.5f) return src;   // keep transparent (decals, glass, water sheen)
            if (cache.TryGetValue((src, outline), out var m) && m) return m;

            m = new Material(toon) { name = src.name + "_toon" };
            var tex = src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : null;
            if (tex) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap")); m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap")); }
            m.SetColor("_BaseColor", src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white);
            var bump = src.HasProperty("_BumpMap") ? src.GetTexture("_BumpMap") : null;
            if (bump) { m.SetTexture("_BumpMap", bump); m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f); }
            var height = src.HasProperty("_ParallaxMap") && src.IsKeywordEnabled("_PARALLAXMAP") ? src.GetTexture("_ParallaxMap") : null;
            if (height) { m.SetTexture("_ParallaxMap", height); m.SetFloat("_Parallax", src.GetFloat("_Parallax")); }
            if (src.IsKeywordEnabled("_EMISSION") && src.HasProperty("_EmissionColor"))
            {
                m.SetColor("_EmissionColor", src.GetColor("_EmissionColor"));
                var em = src.HasProperty("_EmissionMap") ? src.GetTexture("_EmissionMap") : null;
                if (em) m.SetTexture("_EmissionMap", em);
            }
            else m.SetColor("_EmissionColor", Color.black);

            float metal = src.HasProperty("_Metallic") ? src.GetFloat("_Metallic") : 0f;
            float smooth = src.HasProperty("_Smoothness") ? src.GetFloat("_Smoothness") : 0.3f;
            m.SetFloat("_SpecSize", Mathf.Lerp(0.02f, 0.12f, smooth));
            m.SetFloat("_SpecStrength", Mathf.Lerp(0.15f, 0.9f, Mathf.Max(smooth, metal)));
            m.SetFloat("_RimStrength", outline > 0 ? 0.45f : 0.12f);
            m.SetFloat("_OutlineWidth", outline);
            m.SetFloat("_Steps", outline > 0 ? 3 : 4);
            cache[(src, outline)] = m;
            return m;
        }

        void BuildVolume()
        {
            var cam = Camera.main;
            if (cam)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                data.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            }
            if (volume) return;
            var go = new GameObject("ToonPostFX");
            go.transform.SetParent(transform, false);
            volume = go.AddComponent<Volume>();
            volume.isGlobal = true; volume.priority = 10;
            var p = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = p.Add<Bloom>(true);
            bloom.threshold.Override(0.95f); bloom.intensity.Override(0.9f); bloom.scatter.Override(0.65f);
            bloom.highQualityFiltering.Override(false); bloom.downscale.Override(BloomDownscaleMode.Quarter); bloom.maxIterations.Override(5);
            var vig = p.Add<Vignette>(true);
            vig.intensity.Override(0.32f); vig.smoothness.Override(0.45f); vig.color.Override(new Color(0.08f, 0.02f, 0.12f));
            var ca = p.Add<ColorAdjustments>(true);
            ca.saturation.Override(18f); ca.contrast.Override(12f); ca.postExposure.Override(0.15f);
            var chrom = p.Add<ChromaticAberration>(true);
            chrom.intensity.Override(0.12f);
            var tone = p.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);

            volume.sharedProfile = p;
        }

        public void SetToon(bool on, bool announce = true)
        {
            if (on == ToonOn && !announce) return;
            ToonOn = on;
            foreach (var (r, orig, mats) in swapped) if (r) r.sharedMaterials = on ? mats : orig;
            if (announce) Hud.Message(on ? "Мультяшные шейдеры: ВКЛ" : "Мультяшные шейдеры: ВЫКЛ");
        }

        public void SetPost(bool on) { if (volume) volume.enabled = on; }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame) SetToon(!ToonOn);

        }
    }
}
