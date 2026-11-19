using System.Collections.Generic;
using UnityEngine;

namespace Sewer
{
    /// <summary>
    /// Cheap wins for a level made of thousands of small objects:
    /// distance-culls the prop/decal layer, keeps only the nearest point lights on, caps the far plane,
    /// and shows an FPS counter (F1).
    /// </summary>
    public class Perf : MonoBehaviour
    {
        public static Perf Instance;
        public float PropCullDistance = 60f, FarClip = 140f, LightRadius = 42f;
        public int MaxLights = 20;
        public bool ShowFps;

        readonly List<Light> lights = new();
        Camera cam;
        float nextLights, fpsTimer, fps;
        int frames;
        GUIStyle st;

        public static Perf Setup(GameObject host, Transform levelRoot)
        {
            var p = host.GetComponent<Perf>();
            if (p == null) p = host.AddComponent<Perf>();
            Instance = p;
            foreach (var l in levelRoot.GetComponentsInChildren<Light>(true))
                if (l.type == LightType.Point || l.type == LightType.Spot) p.lights.Add(l);
            p.ApplyCamera();
            p.ShowFps = PlayerPrefs.GetInt("sewer_fps", 0) == 1;
            return p;
        }

        public void ApplyCamera()
        {
            cam = Camera.main;
            if (!cam) return;
            cam.farClipPlane = FarClip;
            var d = new float[32];
            d[Geo.CamIgnoreLayer] = PropCullDistance;   // props, decals, mobs (player is always close)
            cam.layerCullDistances = d;
            cam.useOcclusionCulling = false;
        }

        void Update()
        {
            if (!cam) ApplyCamera();
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame) { ShowFps = !ShowFps; PlayerPrefs.SetInt("sewer_fps", ShowFps ? 1 : 0); }

            frames++; fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer > 0.5f) { fps = frames / fpsTimer; frames = 0; fpsTimer = 0; }

            if (cam && Time.unscaledTime > nextLights)
            {
                nextLights = Time.unscaledTime + 0.25f;
                Vector3 c = cam.transform.position;
                lights.RemoveAll(l => l == null);
                lights.Sort((a, b) => (a.transform.position - c).sqrMagnitude.CompareTo((b.transform.position - c).sqrMagnitude));
                float r2 = LightRadius * LightRadius;
                for (int i = 0; i < lights.Count; i++)
                {
                    bool on = i < MaxLights && (lights[i].transform.position - c).sqrMagnitude < r2;
                    if (lights[i].enabled != on) lights[i].enabled = on;
                }
            }
        }

        void OnGUI()
        {
            if (!ShowFps) return;
            st ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            st.normal.textColor = fps > 50 ? new Color(0.5f, 1f, 0.4f) : fps > 30 ? Color.yellow : new Color(1f, 0.4f, 0.4f);
            GUI.Label(new Rect(Screen.width - 150, 8, 140, 30), $"{fps:0} FPS", st);
        }
    }
}
