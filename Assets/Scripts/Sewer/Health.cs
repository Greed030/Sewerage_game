using System;
using UnityEngine;

namespace Sewer
{
    public class Health : MonoBehaviour
    {
        public float Max = 100;
        public float Current;
        public bool Dead => Current <= 0;
        public event Action<float> Damaged;
        public event Action Died;

        float flashUntil;
        bool flashing;
        Renderer[] renderers;
        MaterialPropertyBlock mpb;

        void Awake()
        {
            Current = Max;
            mpb = new MaterialPropertyBlock();
        }

        public void ResetHealth(float max)
        {
            Max = max;
            Current = max;
        }

        public void Hit(float dmg)
        {
            if (Dead || dmg <= 0) return;
            Current = Mathf.Max(0, Current - dmg);
            flashUntil = Time.time + 0.12f;
            Damaged?.Invoke(dmg);
            if (Current <= 0) Died?.Invoke();
        }

        public void Heal(float v) => Current = Mathf.Min(Max, Current + v);

        void LateUpdate()
        {
            bool flash = Time.time < flashUntil;
            if (flash == flashing) return;
            flashing = flash;
            if (renderers == null || renderers.Length == 0) renderers = GetComponentsInChildren<Renderer>();
            foreach (var r in renderers)
            {
                if (r == null) continue;
                if (flash)
                {
                    mpb.Clear();
                    mpb.SetColor("_BaseColor", new Color(1f, 0.35f, 0.3f));
                    r.SetPropertyBlock(mpb);
                }
                else r.SetPropertyBlock(null);
            }
        }
    }
}
