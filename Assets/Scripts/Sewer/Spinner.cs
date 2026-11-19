using UnityEngine;

namespace Sewer
{
    /// <summary>Spins an object (disco balls) and optionally orbits colored lights around it.</summary>
    public class Spinner : MonoBehaviour
    {
        public float Speed = 30f;
        public Light[] Orbit;
        public float OrbitR = 1.2f;

        void Update()
        {
            transform.Rotate(0, Speed * Time.deltaTime, 0, Space.World);
            if (Orbit == null) return;
            for (int i = 0; i < Orbit.Length; i++)
            {
                if (!Orbit[i]) continue;
                float a = Time.time * 1.3f + i * Mathf.PI * 2 / Orbit.Length;
                Orbit[i].transform.position = transform.position + new Vector3(Mathf.Cos(a), -0.6f, Mathf.Sin(a)) * OrbitR;
            }
        }
    }
}
