using UnityEngine;
using UnityEngine.InputSystem;

namespace Sewer
{
    /// <summary>Mouse-orbit third person camera that pulls in when walls are behind it.</summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 7f, MinDist = 2.5f, MaxDist = 14f;
        public float Sensitivity = 0.12f;
        float yaw, pitch = 18f, curDist, shake;
        public void Snap(float y) { yaw = y; pitch = 12f; }
        int skip = 5; // ignore the mouse jump right after the cursor gets locked

        void Start()
        {
            curDist = Distance;
            if (Target) yaw = Target.eulerAngles.y;
        }

        public void Shake(float s) => shake = Mathf.Max(shake, s);
        public void SkipMouse() => skip = 5;

        void LateUpdate()
        {
            if (!Target) return;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool input = SewerGame.Instance == null || SewerGame.Instance.Playing;
            if (Cursor.lockState != CursorLockMode.Locked) skip = 5;
            if (mouse != null && input)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 raw = mouse.delta.ReadValue();
                    if (skip > 0) skip--;
                    else if (raw.sqrMagnitude < 250f * 250f)
                    {
                        Vector2 d = raw * Sensitivity;
                        yaw += d.x; pitch -= d.y;
                    }
                }
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) Distance = Mathf.Clamp(Distance - Mathf.Sign(sc) * 0.6f, MinDist, MaxDist);
            }
            if (kb != null && input)
            {
                if (kb.leftArrowKey.isPressed) yaw -= 120f * Time.deltaTime;
                if (kb.rightArrowKey.isPressed) yaw += 120f * Time.deltaTime;
                if (kb.upArrowKey.isPressed) pitch -= 60f * Time.deltaTime;
                if (kb.downArrowKey.isPressed) pitch += 60f * Time.deltaTime;
            }
            var gp = Gamepad.current;
            if (gp != null && input)
            {
                Vector2 rs = gp.rightStick.ReadValue();
                yaw += rs.x * 160f * Time.deltaTime; pitch -= rs.y * 110f * Time.deltaTime;
            }
            pitch = Mathf.Clamp(pitch, -25f, 65f);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            Vector3 pivot = Target.position + Vector3.up * 1.6f;
            float want = Distance;
            if (Physics.SphereCast(pivot, 0.3f, rot * Vector3.back, out var hit, Distance, ~(1 << Geo.CamIgnoreLayer), QueryTriggerInteraction.Ignore))
                want = Mathf.Max(1.4f, hit.distance - 0.2f);
            curDist = want < curDist ? want : Mathf.Lerp(curDist, want, 6f * Time.deltaTime);

            Vector3 pos = pivot + rot * Vector3.back * curDist;
            if (shake > 0)
            {
                pos += Random.insideUnitSphere * shake * 0.3f;
                shake = Mathf.MoveTowards(shake, 0, Time.deltaTime);
            }
            transform.SetPositionAndRotation(pos, rot);
        }
    }
}
