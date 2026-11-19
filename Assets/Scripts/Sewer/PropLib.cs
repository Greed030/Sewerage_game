using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Sewer
{
    /// <summary>
    /// Loads the Y2K prop models from Resources/Props and prepares their materials:
    /// Glass_* → translucent, Screen_* → glowing texture, Glow_* / Neon_* → emissive.
    /// </summary>
    public static class PropLib
    {
        static Dictionary<string, GameObject> models;
        static readonly Dictionary<Material, Material> Fixed = new();
        static readonly Dictionary<string, Material> DecalMats = new();

        /// <summary>Props that get a solid collider (everything else is walk-through decoration).</summary>
        static readonly string[] Solid =
        {
            "Furn_Couch", "Furn_Mattress", "Furn_Armchair", "Furn_BeanBag", "Tech_Arcade", "Street_Cart", "Trash_TireStack",
            "Trash_Tire", "Street_Barrier", "Furn_Pallet", "Trash_Cardboard", "Tech_CRT", "Trash_Bag_Black", "Trash_Bag_Blue", "Tech_Boombox",
            "Furn_Fridge", "Furn_WashingMachine", "Furn_Bathtub", "Furn_Toilet", "Tech_Vending", "Street_Hydrant", "Street_QuarterPipe",
            "Trash_Drum", "Furn_TV_Stand", "Trash_Suitcase", "Trash_Pile",
        };

        static void Load()
        {
            if (models != null) return;
            models = new Dictionary<string, GameObject>();
            foreach (var g in Resources.LoadAll<GameObject>("Props")) models[g.name] = g;
        }

        public static bool Has(string name) { Load(); return models.ContainsKey(name); }

        public static GameObject Spawn(string name, Transform parent, Vector3 pos, Quaternion rot, float scale = 1f)
        {
            Load();
            if (!models.TryGetValue(name, out var prefab)) return null;
            // holder keeps the FBX's own axis-conversion rotation on the child
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, rot);
            g.transform.localScale = Vector3.one * scale;
            Object.Instantiate(prefab, g.transform, false).name = "Model";
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = FixMat(mats[i]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (System.Array.IndexOf(Solid, name) >= 0)
            {
                var b = Bounds(g);
                var bc = g.AddComponent<BoxCollider>();
                bc.center = g.transform.InverseTransformPoint(b.center);
                Vector3 ls = g.transform.lossyScale;
                bc.size = new Vector3(b.size.x / ls.x, b.size.y / ls.y, b.size.z / ls.z);
                // bounds are world-axis aligned: for rotated props shrink a bit so they don't block corridors
                bc.size *= 0.85f;
            }
            Geo.SetLayer(g, Geo.CamIgnoreLayer);
            return g;
        }

        /// <summary>Instantiate a mob model from Resources/Mobs (materials fixed, shadows on).</summary>
        public static GameObject SpawnMob(string name, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Mobs/" + name);
            if (prefab == null) return null;
            var g = Object.Instantiate(prefab, parent, false);
            g.name = "Model";
            foreach (var r in g.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = FixMat(mats[i]);
                r.sharedMaterials = mats;
            }
            return g;
        }

        static Bounds Bounds(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.one * 0.2f);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Material FixMat(Material src)
        {
            if (src == null) return null;
            if (Fixed.TryGetValue(src, out var m) && m != null) return m;
            m = new Material(src) { name = src.name + "_rt" };
            string n = src.name;
            Color baseC = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            if (n.StartsWith("Glass_"))
            {
                MakeTransparent(m, 0.55f);
                m.SetFloat("_Smoothness", 0.9f);
            }
            else if (n.StartsWith("Screen_"))
            {
                var tex = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", (tex ? Color.white : baseC) * 1.6f);
                if (tex) m.SetTexture("_EmissionMap", tex);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else if (n.StartsWith("Glow_") || n.StartsWith("Neon_"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", baseC * 3.5f);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            else if (n.StartsWith("Mirror") || n.StartsWith("Chrome"))
            {
                m.SetFloat("_Smoothness", 0.85f);
                m.SetFloat("_Metallic", 0.6f);
            }
            Fixed[src] = m;
            return m;
        }

        public static void MakeTransparent(Material m, float alpha)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.renderQueue = (int)RenderQueue.Transparent;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            c.a = alpha;
            m.SetColor("_BaseColor", c); m.color = c;
        }

        /// <summary>Transparent quad material for Resources/SewerDecals/{name}. Neon decals glow.</summary>
        public static Material Decal(string name, float glow = 0f)
        {
            string key = name + glow;
            if (DecalMats.TryGetValue(key, out var m) && m != null) return m;
            var tex = Resources.Load<Texture2D>("SewerDecals/" + name);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh) { name = "Decal_" + name };
            m.SetTexture("_BaseMap", tex); m.mainTexture = tex;
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", name.StartsWith("puddle") ? 0.95f : 0.15f);
            MakeTransparent(m, 1f);
            if (glow > 0)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", tex);
                m.SetColor("_EmissionColor", Color.white * glow);
            }
            m.renderQueue = (int)RenderQueue.Transparent + (name.StartsWith("puddle") || name.StartsWith("slime") || name.StartsWith("chalk") ? -10 : 0);
            DecalMats[key] = m;
            return m;
        }

        /// <summary>Flat decal quad. forward = direction INTO the surface.</summary>
        public static GameObject DecalQuad(Transform parent, string name, Vector3 pos, Vector3 intoSurface, Vector3 up, Vector2 size, float glow = 0f)
        {
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(q.GetComponent<Collider>());
            q.name = "Decal_" + name;
            q.transform.SetParent(parent, false);
            q.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(intoSurface, up));
            q.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = q.GetComponent<MeshRenderer>();
            r.sharedMaterial = Decal(name, glow);
            r.shadowCastingMode = ShadowCastingMode.Off;
            q.layer = Geo.CamIgnoreLayer;
            return q;
        }
    }
}
