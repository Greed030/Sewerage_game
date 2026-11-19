using System.Collections;
using UnityEngine;

namespace Sewer
{
    /// <summary>Floor hatch that slides open when its room boss dies.</summary>
    public class Gate : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            StartCoroutine(Slide());
        }

        IEnumerator Slide()
        {
            foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
            Vector3 a = transform.position, b = a + transform.right * 9f + Vector3.down * 0.05f;
            for (float t = 0; t < 1; t += Time.deltaTime / 0.8f)
            {
                transform.position = Vector3.Lerp(a, b, Mathf.SmoothStep(0, 1, t));
                yield return null;
            }
            transform.position = b;
        }
    }

}
