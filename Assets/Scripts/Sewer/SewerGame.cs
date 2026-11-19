using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Sewer
{
    /// <summary>
    /// Entry point. Put this on one empty GameObject (menu Sewer → Create Game Scene does it)
    /// and press Play: it builds the sewer, spawns the player with a random character, monsters and bosses.
    /// </summary>
    public class SewerGame : MonoBehaviour
    {
        public static SewerGame Instance { get; private set; }

        public Player Player { get; private set; }
        public Boss ActiveBoss;
        public bool Finished { get; private set; }
        public float StartTime { get; private set; }
        public float FinishTime { get; private set; }
        public LevelBuilder Level { get; private set; }
        /// <summary>True while the player is actually playing (not in the main menu / intro / pause).</summary>
        public bool Playing => GameUI.Instance == null || GameUI.Instance.State == UiState.Playing;

        [Header("Your environment models")]
        [Tooltip("Drag free models/prefabs here — they are placed on prop slots along the tunnels and in chambers.")]
        public GameObject[] EnvironmentPrefabs;
        [Range(0, 1)] public float PropSlotChance = 0.7f;
        public Vector2 PropScale = new Vector2(0.9f, 1.2f);

        ThirdPersonCamera cam;

        static readonly string[] BossNames = { "Fat Slug", "Rat Baron", "Pipe Worm", "Mold Mother", "Rusty Crab", "Canal Troll" };
        static readonly string[] BossColors = { "#7FA33A", "#8A6A5A", "#B5566E", "#5E8C7A", "#B8642F", "#5B6FA8" };

        void Awake()
        {
            Instance = this;
            Enemy.All.Clear();

            // dark sewer atmosphere
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.31f, 0.40f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = new Color(0.07f, 0.06f, 0.10f);
            RenderSettings.fogDensity = 0.013f;
            RenderSettings.skybox = null;
            RenderSettings.reflectionIntensity = 0.12f;
            RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) l.intensity = 0.05f;

            Physics.queriesHitBackfaces = true; // camera collides with walls from both sides
            Level = new LevelBuilder();
            Transform levelRoot = transform.Find("Level");
            if (levelRoot != null)
            {
                // level was baked into the scene (menu Sewer → Bake Level Into Scene)
                Level.BuildData();
                foreach (var r in Level.Rooms)
                    if (r.Kind == RoomKind.Gate)
                    {
                        var t = FindDeep(levelRoot, r.HatchName);
                        if (t) r.Gate = t.GetComponent<Gate>();
                    }
            }
            else levelRoot = Level.Build(transform);
            if (levelRoot.Find("Dressing") == null) PropDresser.Dress(Level, levelRoot);
            SpawnEnvironment(levelRoot);

            // player — random character at a random start point
            var def = Roster.Random();
            int spawn = Random.Range(0, Level.Spawns.Count);
            Vector3 sp = Level.Spawns[spawn];
            float yaw = Quaternion.LookRotation(Vector3.Cross(Vector3.up, sp.normalized)).eulerAngles.y;
            Player = Player.Spawn(def, sp, yaw);

            // camera
            var camGo = Camera.main ? Camera.main.gameObject : new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            var camera = camGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = RenderSettings.fogColor;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 260f;
            cam = camGo.GetComponent<ThirdPersonCamera>();
            if (cam == null) cam = camGo.AddComponent<ThirdPersonCamera>();
            cam.Target = Player.transform;
            Player.Cam = camGo.transform;

            // monsters
            var mobs = new GameObject("Monsters").transform;
            mobs.SetParent(transform, false);
            foreach (var (lvl, pos, ang, u) in Level.CrawlerSpots)
                Crawler.Spawn(pos, LevelBuilder.Rings[lvl].R + u, ang, mobs);

            int bi = 0;
            var rewards = new[] { RewardType.MaxHealth, RewardType.AbilityBoost, RewardType.SpeedBoost };
            int ri = Random.Range(0, rewards.Length);
            foreach (var room in Level.Rooms)
            {
                if (room.Kind == RoomKind.Arena) continue;
                float hp = 160 + room.Level * 70, scale = 1.5f + room.Level * 0.25f;
                if (room.Kind == RoomKind.Optional) { hp *= 0.8f; scale *= 0.9f; }
                var boss = Boss.Spawn(room, BossNames[bi % BossNames.Length], hp, scale,
                    Mat.Hex(BossColors[bi % BossColors.Length]), mobs);
                bi++;
                if (room.Kind == RoomKind.Gate)
                {
                    var gate = room.Gate;
                    boss.OnDefeated = () =>
                    {
                        if (gate) gate.Open();
                        Hud.Message("Hatch open — jump down!");
                    };
                }
                else
                {
                    var chest = Chest.Create(levelRoot, room.ChestPos, Quaternion.LookRotation(Vector3.ProjectOnPlane(room.Center, Vector3.up).normalized), rewards[ri++ % rewards.Length]);
                    boss.OnDefeated = () =>
                    {
                        chest.Locked = false;
                        Hud.Message("Chest unlocked — grab your reward");
                    };
                }
            }
            // final boss guards the crown
            var king = Boss.Spawn(Level.Arena, "Sewer King", 700, 2.8f, Mat.Hex("#6A3FA0"), mobs);
            king.OnDefeated = () => Hud.Message("The crown is free!");
            Crown.Create(Level.CrownPos);

            ToonFx.Setup(gameObject);
            Perf.Setup(gameObject, levelRoot);
            GameUI.Setup(gameObject);
            StartTime = Time.time;
        }

        public void Shake(float s) { if (cam) cam.Shake(s); }

        /// <summary>Called by the UI when the intro roulette finishes.</summary>
        public bool Started { get; private set; }
        public void BeginPlay()
        {
            StartTime = Time.time; Started = true;
            Hud.Message($"You are {Player.Def.Name}. Ability: {Player.Def.AbilityName}. Get to the bottom and grab the crown!", 5f);
        }

        Coroutine hitStop;
        /// <summary>Tiny freeze-frame on heavy hits (game feel).</summary>
        public void HitStop(float t)
        {
            if (!Playing || Time.timeScale < 0.99f || hitStop != null) return;
            hitStop = StartCoroutine(HitStopRoutine(t));
        }
        System.Collections.IEnumerator HitStopRoutine(float t)
        {
            Time.timeScale = 0.05f;
            yield return new WaitForSecondsRealtime(t);
            if (Playing) Time.timeScale = 1f;
            hitStop = null;
        }

        void SpawnEnvironment(Transform levelRoot)
        {
            if (EnvironmentPrefabs == null || EnvironmentPrefabs.Length == 0) return;
            var holder = new GameObject("Environment").transform;
            holder.SetParent(levelRoot, false);
            var rnd = new System.Random(7);
            foreach (var (pos, rot) in Level.PropSlots)
            {
                if (rnd.NextDouble() > PropSlotChance) continue;
                var prefab = EnvironmentPrefabs[rnd.Next(EnvironmentPrefabs.Length)];
                if (prefab == null) continue;
                var g = Instantiate(prefab, pos, rot * Quaternion.Euler(0, (float)rnd.NextDouble() * 40 - 20, 0), holder);
                g.transform.localScale *= Mathf.Lerp(PropScale.x, PropScale.y, (float)rnd.NextDouble());
            }
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, name);
                if (r) return r;
            }
            return null;
        }

        public void Win(Player p)
        {
            Finished = true;
            FinishTime = Time.time - StartTime;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (GameUI.Instance) GameUI.Instance.ShowWin(FinishTime, p.Def.Name);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && ((Finished && kb.rKey.wasPressedThisFrame) || kb.f5Key.wasPressedThisFrame))
            {
                Time.timeScale = 1; GameUI.SkipMenuOnce = true;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
        }
    }
}
