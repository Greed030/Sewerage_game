using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>Runtime material cache (URP Lit).</summary>
    public static class Mat
    {
        static Shader _lit;
        static readonly Dictionary<string, Material> Cache = new();

        static Shader Lit
        {
            get
            {
                if (_lit == null) _lit = Shader.Find("Universal Render Pipeline/Lit");
                if (_lit == null) _lit = Shader.Find("Standard");
                return _lit;
            }
        }

        public static Material Get(Color c, float smooth = 0.25f, Color? emission = null)
        {
            string key = $"{c}{smooth}{emission}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit) { name = "Sewer_color_" + ColorUtility.ToHtmlStringRGB(c) };
            m.SetColor("_BaseColor", c);
            m.color = c;
            m.SetFloat("_Smoothness", smooth);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            Cache[key] = m;
            return m;
        }

        /// <summary>Textured URP Lit material from Resources/SewerTextures/{name}_a / {name}_n. tile = meters per texture repeat.</summary>
        public static Material Tex(string name, float tile, float smooth = 0.2f, Color? tint = null, float metallic = 0f, float bump = 1f)
        {
            string key = $"tex_{name}_{tile}_{smooth}_{tint}_{metallic}_{bump}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Lit) { name = $"Sewer_{name}_{tile:0.#}" };
            var a = Resources.Load<Texture2D>($"SewerTextures/{name}_a");
            var n = Resources.Load<Texture2D>($"SewerTextures/{name}_n");
            Color c = tint ?? Color.white;
            m.SetColor("_BaseColor", c); m.color = c;
            if (a != null) { m.SetTexture("_BaseMap", a); m.mainTexture = a; }
            if (n != null)
            {
                m.SetTexture("_BumpMap", n);
                m.SetFloat("_BumpScale", bump);
                m.EnableKeyword("_NORMALMAP");
            }
            var h = Resources.Load<Texture2D>($"SewerTextures/{name}_h");
            if (h != null)
            {
                m.SetTexture("_ParallaxMap", h);
                m.SetFloat("_Parallax", 0.025f);
                m.EnableKeyword("_PARALLAXMAP");
            }
            Vector2 sc = Vector2.one / Mathf.Max(0.01f, tile);
            m.SetTextureScale("_BaseMap", sc);
            m.mainTextureScale = sc;
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", metallic);
            Cache[key] = m;
            return m;
        }

        /// <summary>Textured material that also glows with its own texture (toxic water).</summary>
        public static Material TexGlow(string name, float tile, float smooth, float glow)
        {
            string key = $"texglow_{name}_{tile}_{smooth}_{glow}";
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(Tex(name, tile, smooth)) { name = $"Sewer_{name}_glow" };
            var a = Resources.Load<Texture2D>($"SewerTextures/{name}_a");
            m.EnableKeyword("_EMISSION");
            if (a != null) m.SetTexture("_EmissionMap", a);
            m.SetColor("_EmissionColor", Color.white * glow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            Cache[key] = m;
            return m;
        }

        public static Material Emissive(Color c, float intensity)
        {
            return Get(c, 0.5f, c * intensity);
        }

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }
    }

    public static class Geo
    {
        /// <summary>Layer the camera ignores (built-in "Ignore Raycast"): props, monsters, player.</summary>
        public const int CamIgnoreLayer = 2;

        public static void SetLayer(GameObject g, int layer)
        {
            foreach (var t in g.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
        }

        /// <summary>Point on a circle (degrees) at radius r and height y.</summary>
        public static Vector3 Polar(float deg, float r, float y)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        public static Vector3 Out(float deg)
        {
            float a = deg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
        }

        public static GameObject Box(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material m, bool collider = true)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!collider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        /// <summary>Box with meter-scaled UVs (textures don't stretch). Rotation applied, scale stays 1.</summary>
        public static GameObject TBox(Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 size, Material m, bool collider = true)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.AddComponent<MeshFilter>().sharedMesh = MeshKit.Box(size);
            var r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            if (collider) g.AddComponent<BoxCollider>();
            return g;
        }

        public static GameObject Prim(PrimitiveType t, Transform parent, string name, Vector3 localPos, Vector3 scale, Material m, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            g.transform.SetParent(parent, false);
            g.transform.localPosition = localPos;
            g.transform.localScale = scale;
            g.GetComponent<Renderer>().sharedMaterial = m;
            if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
            return g;
        }

        public static Light PointLight(Transform parent, Vector3 pos, Color c, float range, float intensity)
        {
            var g = new GameObject("Light");
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            return l;
        }
    }
}
