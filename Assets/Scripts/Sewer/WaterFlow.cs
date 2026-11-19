using UnityEngine;

namespace Sewer
{
    /// <summary>Scrolls the water texture (per-renderer material instance, so baked assets aren't modified).</summary>
    public class WaterFlow : MonoBehaviour
    {
        Material m;
        void Start()
        {
            var r = GetComponent<Renderer>();
            if (r) m = r.material;
        }
        void Update()
        {
            if (m) m.SetTextureOffset("_BaseMap", new Vector2(Time.time * 0.035f, Time.time * 0.012f));
        }
    }
}
