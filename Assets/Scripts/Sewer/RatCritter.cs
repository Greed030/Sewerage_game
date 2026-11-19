using UnityEngine;

namespace Sewer
{
    /// <summary>Harmless rat: sniffs around along the wall and scurries away from the player.</summary>
    public class RatCritter : MonoBehaviour
    {
        public float RingR, Y, Angle, U;
        float dir = 1, speed, timer, bob;

        void Start() { timer = Random.Range(0.5f, 3f); dir = Random.value > 0.5f ? 1 : -1; }

        void Update()
        {
            var p = SewerGame.Instance ? SewerGame.Instance.Player : null;
            float want = 0f;
            if (p && (p.transform.position - transform.position).sqrMagnitude < 36f)
            {
                // run away along the ring
                Vector3 tan = new Vector3(-Mathf.Sin(Angle * Mathf.Deg2Rad), 0, Mathf.Cos(Angle * Mathf.Deg2Rad));
                dir = Vector3.Dot(transform.position - p.transform.position, tan) >= 0 ? 1 : -1;
                want = 6.5f;
            }
            else
            {
                timer -= Time.deltaTime;
                if (timer <= 0) { timer = Random.Range(0.8f, 3.5f); dir = Random.value > 0.3f ? dir : -dir; speed = Random.value > 0.4f ? 1.6f : 0f; }
                want = speed;
            }
            float v = want;
            Angle += dir * v / Mathf.Max(1f, RingR) * Mathf.Rad2Deg * Time.deltaTime;
            bob += Time.deltaTime * (v > 0.1f ? 30f : 3f);
            transform.position = Geo.Polar(Angle, RingR + U, Y + Mathf.Abs(Mathf.Sin(bob)) * (v > 0.1f ? 0.025f : 0f));
            Vector3 fwd = new Vector3(-Mathf.Sin(Angle * Mathf.Deg2Rad), 0, Mathf.Cos(Angle * Mathf.Deg2Rad)) * dir;
            transform.rotation = Quaternion.LookRotation(fwd); // model front (Blender -Y) is Unity +Z
        }
    }
}
