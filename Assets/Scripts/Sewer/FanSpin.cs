using UnityEngine;

namespace Sewer
{
    /// <summary>Spins a fan around its local forward axis.</summary>
    public class FanSpin : MonoBehaviour
    {
        public float Speed = 70f;
        void Update() => transform.Rotate(0, 0, Speed * Time.deltaTime, Space.Self);
    }
}
