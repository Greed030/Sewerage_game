using UnityEngine;

namespace Sewer
{
    /// <summary>Old neon / bulb flicker.</summary>
    public class Flicker : MonoBehaviour
    {
        public Light Target;
        public float Base = 3f;
        float next, dim;

        void Update()
        {
            if (!Target) return;
            if (Time.time > next) { next = Time.time + Random.Range(0.05f, 2.5f); dim = Random.value < 0.25f ? Random.Range(0.1f, 0.6f) : 1f; }
            Target.intensity = Mathf.Lerp(Target.intensity, Base * dim, 20f * Time.deltaTime);
        }
    }
}
