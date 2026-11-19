using UnityEngine;

namespace Sewer
{
    /// <summary>Trash floating down the water channel of a ring.</summary>
    public class FloatDrift : MonoBehaviour
    {
        public float RingR, Y, Angle, Speed = 0.5f, Offset;
        float phase, spin;

        void Start() { phase = Random.value * 10f; spin = Random.Range(-20f, 20f); }

        void Update()
        {
            Angle += Speed / Mathf.Max(1f, RingR) * Mathf.Rad2Deg * Time.deltaTime;
            Vector3 p = Geo.Polar(Angle, RingR + Offset + Mathf.Sin(Time.time * 0.3f + phase) * 0.4f, Y + Mathf.Sin(Time.time * 1.7f + phase) * 0.03f);
            transform.position = p;
            transform.Rotate(0, spin * Time.deltaTime, 0, Space.World);
        }
    }
}
