using System.Collections;
using UnityEngine;

namespace Sewer
{
    public enum RewardType { MaxHealth, AbilityBoost, SpeedBoost }

    /// <summary>Reward chest in optional rooms — unlocks after its boss is beaten.</summary>
    public class Chest : MonoBehaviour
    {
        public RewardType Reward;
        public bool Locked = true;
        bool taken;
        Transform lid;

        public static Chest Create(Transform parent, Vector3 pos, Quaternion rot, RewardType reward)
        {
            var g = new GameObject("Chest");
            g.transform.SetParent(parent, false);
            g.transform.SetPositionAndRotation(pos, rot);
            Geo.Prim(PrimitiveType.Cube, g.transform, "Body", new Vector3(0, 0.35f, 0), new Vector3(1.2f, 0.7f, 0.8f), Mat.Get(Mat.Hex("#7A4E2A"), 0.2f));
            var lidPivot = new GameObject("LidPivot").transform;
            lidPivot.SetParent(g.transform, false);
            lidPivot.localPosition = new Vector3(0, 0.7f, -0.4f);
            Geo.Prim(PrimitiveType.Cube, lidPivot, "Lid", new Vector3(0, 0.12f, 0.4f), new Vector3(1.25f, 0.24f, 0.85f), Mat.Get(Mat.Hex("#8E5C32"), 0.2f));
            Geo.Prim(PrimitiveType.Cube, g.transform, "Lock", new Vector3(0, 0.6f, 0.42f), new Vector3(0.2f, 0.25f, 0.06f), Mat.Get(Mat.Hex("#FFD23F"), 0.8f, Mat.Hex("#553F00")));
            var col = g.AddComponent<BoxCollider>();
            col.isTrigger = true;
            col.center = new Vector3(0, 0.5f, 0);
            col.size = new Vector3(2.2f, 1.5f, 2f);
            var c = g.AddComponent<Chest>();
            c.Reward = reward;
            c.lid = lidPivot;
            return c;
        }

        void OnTriggerEnter(Collider other)
        {
            if (taken) return;
            var p = other.GetComponentInParent<Player>();
            if (p == null) return;
            if (Locked)
            {
                Hud.Message("Сундук заперт — победи стража комнаты");
                return;
            }
            taken = true;
            p.ApplyReward(Reward);
            StartCoroutine(OpenLid());
        }

        IEnumerator OpenLid()
        {
            for (float t = 0; t < 1; t += Time.deltaTime / 0.4f)
            {
                lid.localRotation = Quaternion.Euler(-100 * t, 0, 0);
                yield return null;
            }
        }
    }

    /// <summary>The crown — touch it to win.</summary>
    public class Crown : MonoBehaviour
    {
        public static Crown Create(Vector3 pos)
        {
            var g = new GameObject("Crown");
            g.transform.position = pos;
            var gold = Mat.Get(Mat.Hex("#FFC93C"), 0.85f, Mat.Hex("#7A5200"));
            var gem = Mat.Get(Mat.Hex("#E0245E"), 0.9f, Mat.Hex("#5A0018"));
            Geo.Prim(PrimitiveType.Cylinder, g.transform, "Band", Vector3.zero, new Vector3(0.9f, 0.18f, 0.9f), gold);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f * Mathf.Deg2Rad;
                var p = Geo.Prim(PrimitiveType.Cube, g.transform, "Point", new Vector3(Mathf.Cos(a) * 0.38f, 0.3f, Mathf.Sin(a) * 0.38f), new Vector3(0.14f, 0.42f, 0.14f), gold);
                p.transform.localRotation = Quaternion.Euler(0, -i * 60f, 45f * 0) * Quaternion.Euler(0, 45, 0);
                Geo.Prim(PrimitiveType.Sphere, g.transform, "Tip", new Vector3(Mathf.Cos(a) * 0.38f, 0.56f, Mathf.Sin(a) * 0.38f), Vector3.one * 0.13f, gold);
            }
            Geo.Prim(PrimitiveType.Sphere, g.transform, "Gem", new Vector3(0, 0.05f, -0.45f), Vector3.one * 0.18f, gem);
            var l = Geo.PointLight(g.transform, pos + Vector3.up * 0.6f, Mat.Hex("#FFD36B"), 6f, 3f);
            l.transform.SetParent(g.transform, true);
            var col = g.AddComponent<SphereCollider>();
            col.isTrigger = true;
            col.radius = 1.7f;
            return g.AddComponent<Crown>();
        }

        void Update()
        {
            transform.Rotate(0, 60 * Time.deltaTime, 0, Space.World);
            transform.position += Vector3.up * Mathf.Sin(Time.time * 2f) * 0.002f;
        }

        void OnTriggerEnter(Collider other)
        {
            var p = other.GetComponentInParent<Player>();
            if (p == null || SewerGame.Instance.Finished) return;
            transform.SetParent(p.transform, true);
            transform.localPosition = new Vector3(0, 2.3f, 0);
            enabled = false;
            SewerGame.Instance.Win(p);
        }
    }
}
