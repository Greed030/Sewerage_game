using UnityEngine;

namespace Sewer
{
    public enum Ability { Dash, Invisibility, Slam, Flash }

    public class CharacterDef
    {
        public string Name, AbilityName, Description;
        public Ability Ability;
        public float Cooldown, MaxHp, Speed;
        public Color Hair, Accent;

        public CharacterDef(string name, string abilityName, string desc, Ability ability, float cd, float hp, float speed, string hair, string accent)
        {
            Name = name; AbilityName = abilityName; Description = desc; Ability = ability;
            Cooldown = cd; MaxHp = hp; Speed = speed;
            Hair = Mat.Hex(hair); Accent = Mat.Hex(accent);
        }
    }

    /// <summary>The 4 playable characters. One is assigned at random when the match starts.</summary>
    public static class Roster
    {
        public static readonly CharacterDef[] All =
        {
            new CharacterDef("Spike", "Dash", "A sharp dash forward that rams and hurts enemies",
                Ability.Dash, 4f, 100, 6.5f, "#6E8A2B", "#E8681C"),
            new CharacterDef("Shade", "Invisibility", "Monsters can't see you for 5 seconds",
                Ability.Invisibility, 14f, 90, 7.0f, "#6B45C9", "#3B3A4A"),
            new CharacterDef("Bulldozer", "Ground Slam", "Damages and knocks back everyone around",
                Ability.Slam, 8f, 135, 5.8f, "#C9372C", "#2F6FD6"),
            new CharacterDef("Spark", "Flash", "Stuns all nearby monsters for 3 seconds",
                Ability.Flash, 10f, 100, 6.2f, "#F2C230", "#1FA58A"),
        };

        public static CharacterDef Random() => All[UnityEngine.Random.Range(0, All.Length)];

        /// <summary>Spawns the character model (Resources/Characters/Spike) recolored for this character.</summary>
        public static Transform SpawnModel(CharacterDef def, Transform parent, float targetHeight = 1.85f)
        {
            var prefab = Resources.Load<GameObject>("Characters/Spike");
            GameObject inst;
            if (prefab != null) inst = Object.Instantiate(prefab);
            else
            {
                inst = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                Object.Destroy(inst.GetComponent<Collider>());
                inst.GetComponent<Renderer>().sharedMaterial = Mat.Get(def.Accent);
            }
            inst.name = "Model";
            var holder = new GameObject("ModelRoot").transform;
            holder.SetParent(parent, false);
            inst.transform.SetParent(holder, false);

            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                foreach (var m in mats)
                {
                    string n = m.name;
                    Color? c = null;
                    if (n.Contains("Hair")) c = def.Hair;
                    else if (n.Contains("Orange")) c = def.Accent;
                    if (c.HasValue)
                    {
                        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c.Value);
                        m.color = c.Value;
                    }
                }
                r.materials = mats;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            // normalise size and put feet on the ground
            var bounds = new Bounds(holder.position, Vector3.zero);
            bool first = true;
            foreach (var r in inst.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = r.bounds; first = false; }
                else bounds.Encapsulate(r.bounds);
            }
            if (bounds.size.y > 0.01f)
            {
                float k = targetHeight / bounds.size.y;
                inst.transform.localScale *= k;
                float feet = holder.position.y + (bounds.min.y - holder.position.y) * k;
                inst.transform.position += Vector3.up * (holder.position.y - feet);
            }
            return holder;
        }
    }
}
