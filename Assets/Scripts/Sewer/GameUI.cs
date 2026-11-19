using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Sewer
{
    public enum UiState { Menu, Intro, Playing, Paused, Won }

    /// <summary>Main menu (camera flies through the sewer), character roulette, pause, settings, controls and win screen.</summary>
    public class GameUI : MonoBehaviour
    {
        public static GameUI Instance;
        public static bool SkipMenuOnce;          // set before reloading the scene for "restart"
        public UiState State { get; private set; } = UiState.Menu;
        public Hud Hud { get; private set; }

        Canvas canvas;
        RectTransform menuRoot, pauseRoot, settingsRoot, controlsRoot, introRoot, winRoot, overlayRoot;
        Text introName, introAbility, introDesc, winTime, winTitle;
        Image introIcon;
        bool settingsFromPause;
        float menuAngle;

        // ------------------------------------------------------------ settings (PlayerPrefs)
        public static float Sensitivity { get => PlayerPrefs.GetFloat("sewer_sens", 0.12f); set => PlayerPrefs.SetFloat("sewer_sens", value); }
        public static float Volume { get => PlayerPrefs.GetFloat("sewer_vol", 0.8f); set => PlayerPrefs.SetFloat("sewer_vol", value); }
        public static bool ToonOn { get => PlayerPrefs.GetInt("sewer_toon", 1) == 1; set => PlayerPrefs.SetInt("sewer_toon", value ? 1 : 0); }
        public static bool PostOn { get => PlayerPrefs.GetInt("sewer_post", 1) == 1; set => PlayerPrefs.SetInt("sewer_post", value ? 1 : 0); }
        public static float DrawDistance { get => PlayerPrefs.GetFloat("sewer_draw", 60f); set => PlayerPrefs.SetFloat("sewer_draw", value); }
        public static bool VSync { get => PlayerPrefs.GetInt("sewer_vsync", 1) == 1; set => PlayerPrefs.SetInt("sewer_vsync", value ? 1 : 0); }

        public static void ApplySettings()
        {
            AudioListener.volume = Volume;
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = VSync ? -1 : 144;
            var cam = Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;
            if (cam) cam.Sensitivity = Sensitivity;
            if (ToonFx.Instance) { ToonFx.Instance.SetToon(ToonOn, false); ToonFx.Instance.SetPost(PostOn); }
            if (Perf.Instance) { Perf.Instance.PropCullDistance = DrawDistance; Perf.Instance.ApplyCamera(); }
        }

        public static GameUI Setup(GameObject host)
        {
            var ui = host.AddComponent<GameUI>();
            Instance = ui;
            ui.Hud = host.AddComponent<Hud>();
            return ui;
        }

        void Start()
        {
            Time.timeScale = 1;
            ApplySettings();
            Build();
            if (SkipMenuOnce) { SkipMenuOnce = false; StartIntro(); }
            else ShowMenu();
        }

        // ------------------------------------------------------------ build
        void Build()
        {
            canvas = UiKit.MakeCanvas("GameUI", 20, transform);
            var root = canvas.transform;

            // shared full-screen overlay (vignette + grid + sparkles)
            overlayRoot = UiKit.Stretch(root, "Overlay");
            var vig = UiKit.Img(overlayRoot, "vignette", new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            vig.preserveAspect = false; var vr = vig.rectTransform; vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.sizeDelta = Vector2.zero;
            var dim = UiKit.Img(overlayRoot, null, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 0, new Color(0.06f, 0.02f, 0.14f, 0.35f));
            var dmr = dim.rectTransform; dmr.anchorMin = Vector2.zero; dmr.anchorMax = Vector2.one; dmr.sizeDelta = Vector2.zero;
            dim.raycastTarget = true;   // blocks clicks to the game while a menu is up
            var grid = UiKit.Img(overlayRoot, "grid", new Vector2(0.5f, 0), Vector2.zero, new Vector2(2600, 330), 0, new Color(1, 1, 1, 0.28f), new Vector2(0.5f, 0));
            grid.preserveAspect = false;
            for (int i = 0; i < 14; i++)
            {
                var s = UiKit.Img(overlayRoot, "sparkle", new Vector2(Random.value, Random.Range(0.35f, 1f)), Vector2.zero, Vector2.one * Random.Range(26, 60));
                s.gameObject.AddComponent<UiTwinkle>();
            }

            // ---- main menu
            menuRoot = UiKit.Stretch(root, "Menu");
            var logo = UiKit.Img(menuRoot, "logo", new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1000, 438), 0, null, new Vector2(0.5f, 1));
            logo.gameObject.AddComponent<UiBob>().Spin = 2f;
            UiKit.Txt(menuRoot, "get to the bottom — grab the crown", 34, UiKit.Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -470), new Vector2(1200, 50), false);
            float y = -525;
            UiKit.Btn(menuRoot, "PLAY", "play", new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(560, 120), StartIntro, "btn_primary");
            UiKit.Btn(menuRoot, "SETTINGS", "gear", new Vector2(0.5f, 1), new Vector2(0, y - 128), new Vector2(560, 104), () => OpenSettings(false));
            UiKit.Btn(menuRoot, "CONTROLS", "mouse", new Vector2(0.5f, 1), new Vector2(0, y - 246), new Vector2(560, 104), () => OpenControls());
            UiKit.Btn(menuRoot, "QUIT", "exit", new Vector2(0.5f, 1), new Vector2(0, y - 364), new Vector2(560, 104), Quit);
            UiKit.Txt(menuRoot, "UKF • Y2K SEWER EDITION • 2026", 22, new Color(1, 1, 1, 0.55f), TextAnchor.LowerCenter, new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(900, 40), true, true, new Vector2(0.5f, 0));

            // ---- pause
            pauseRoot = UiKit.Stretch(root, "Pause");
            var pp = UiKit.Img(pauseRoot, "panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(720, 840), 72);
            Header(pp.transform, "icon_pause", "PAUSED", 640);
            float py = -200;
            UiKit.Btn(pp.transform, "RESUME", "play", new Vector2(0.5f, 1), new Vector2(0, py), new Vector2(560, 104), Resume, "btn_primary");
            UiKit.Btn(pp.transform, "SETTINGS", "gear", new Vector2(0.5f, 1), new Vector2(0, py - 120), new Vector2(560, 100), () => OpenSettings(true));
            UiKit.Btn(pp.transform, "RESTART", "restart", new Vector2(0.5f, 1), new Vector2(0, py - 236), new Vector2(560, 100), Restart);
            UiKit.Btn(pp.transform, "MAIN MENU", "home", new Vector2(0.5f, 1), new Vector2(0, py - 352), new Vector2(560, 100), ToMenu);
            UiKit.Btn(pp.transform, "QUIT", "exit", new Vector2(0.5f, 1), new Vector2(0, py - 468), new Vector2(560, 100), Quit);

            // ---- settings
            settingsRoot = UiKit.Stretch(root, "Settings");
            var sp = UiKit.Img(settingsRoot, "panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 1000), 72);
            Header(sp.transform, "icon_gear", "SETTINGS", 900);
            float sy = -185, sw = 800;
            UiKit.Sld(sp.transform, "Mouse sensitivity", new Vector2(0.5f, 1), new Vector2(0, sy), sw, 0.03f, 0.35f, Sensitivity, v => { Sensitivity = v; ApplySettings(); }, v => (v / 0.12f).ToString("0.0") + "x");
            UiKit.Sld(sp.transform, "Volume", new Vector2(0.5f, 1), new Vector2(0, sy - 120), sw, 0, 1, Volume, v => { Volume = v; ApplySettings(); }, v => Mathf.RoundToInt(v * 100) + "%");
            UiKit.Sld(sp.transform, "Draw distance (FPS)", new Vector2(0.5f, 1), new Vector2(0, sy - 240), sw, 30, 110, DrawDistance, v => { DrawDistance = v; ApplySettings(); }, v => Mathf.RoundToInt(v) + " m");
            UiKit.Tgl(sp.transform, "Cartoon shaders", new Vector2(0.5f, 1), new Vector2(0, sy - 360), sw, ToonOn, v => { ToonOn = v; ApplySettings(); });
            UiKit.Tgl(sp.transform, "Glow & post effects", new Vector2(0.5f, 1), new Vector2(0, sy - 435), sw, PostOn, v => { PostOn = v; ApplySettings(); });
            UiKit.Tgl(sp.transform, "V-Sync", new Vector2(0.5f, 1), new Vector2(0, sy - 510), sw, VSync, v => { VSync = v; ApplySettings(); });
            UiKit.Tgl(sp.transform, "Show FPS (F1)", new Vector2(0.5f, 1), new Vector2(0, sy - 585), sw, Perf.Instance && Perf.Instance.ShowFps, v => { if (Perf.Instance) { Perf.Instance.ShowFps = v; PlayerPrefs.SetInt("sewer_fps", v ? 1 : 0); } });
            UiKit.Btn(sp.transform, "BACK", "home", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(460, 104), CloseSubmenu, "btn_normal");

            // ---- controls
            controlsRoot = UiKit.Stretch(root, "Controls");
            var cp = UiKit.Img(controlsRoot, "panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 940), 72);
            Header(cp.transform, "icon_mouse", "CONTROLS", 900);
            string[,] keys =
            {
                { "W A S D", "move" }, { "SHIFT", "sprint" }, { "SPACE", "jump / climb" }, { "LMB", "bat attack (3 hits = combo)" },
                { "E / RMB", "character ability" }, { "MOUSE / ARROWS", "camera" }, { "WHEEL", "camera zoom" }, { "ESC", "pause" },
            };
            for (int i = 0; i < keys.GetLength(0); i++)
            {
                float ky = -190 - i * 76;
                var kb = UiKit.Img(cp.transform, "btn_normal", new Vector2(0.5f, 1), new Vector2(-200, ky), new Vector2(300, 64), 32);
                UiKit.Txt(kb.transform, keys[i, 0], 26, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(290, 60));
                UiKit.Txt(cp.transform, keys[i, 1], 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0.5f, 1), new Vector2(240, ky), new Vector2(500, 64), false);
            }
            UiKit.Btn(cp.transform, "BACK", "home", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(460, 104), CloseSubmenu, "btn_normal");

            // ---- character roulette intro
            introRoot = UiKit.Stretch(root, "Intro");
            var ip = UiKit.Img(introRoot, "panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 640), 72);
            UiKit.Txt(ip.transform, "YOU GOT...", 40, UiKit.Cyan, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -70), new Vector2(800, 60));
            introIcon = UiKit.Img(ip.transform, "icon_dash", new Vector2(0.5f, 0.5f), new Vector2(0, 75), new Vector2(180, 180));
            introIcon.gameObject.AddComponent<UiBob>().Amp = 6;
            introName = UiKit.Txt(ip.transform, "", 80, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -90), new Vector2(820, 100));
            introAbility = UiKit.Txt(ip.transform, "", 36, UiKit.Pink, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -165), new Vector2(820, 50));
            introDesc = UiKit.Txt(ip.transform, "", 28, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, -225), new Vector2(780, 70), false);

            // ---- win
            winRoot = UiKit.Stretch(root, "Win");
            var wp = UiKit.Img(winRoot, "panel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900, 820), 72);
            UiKit.Img(wp.transform, "icon_crown", new Vector2(0.5f, 1), new Vector2(0, -140), new Vector2(190, 190), 0, null, new Vector2(0.5f, 0.5f)).gameObject.AddComponent<UiBob>().Spin = 6f;
            winTitle = UiKit.Txt(wp.transform, "THE CROWN IS YOURS!", 70, UiKit.PinkLight, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -290), new Vector2(800, 90));
            winTime = UiKit.Txt(wp.transform, "", 32, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(0, -380), new Vector2(760, 90), false);
            UiKit.Btn(wp.transform, "PLAY AGAIN", "restart", new Vector2(0.5f, 0), new Vector2(0, 180), new Vector2(520, 104), Restart, "btn_primary");
            UiKit.Btn(wp.transform, "MAIN MENU", "home", new Vector2(0.5f, 0), new Vector2(0, 50), new Vector2(520, 104), ToMenu);

            HideAll();
        }

        /// <summary>Panel title row: icon + text, both inside the panel.</summary>
        static void Header(Transform panel, string icon, string title, float width)
        {
            var row = UiKit.Rect(panel, "Header", new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(width, 100), new Vector2(0.5f, 1));
            var t = UiKit.Txt(row, title, 62, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(46, 0), new Vector2(width - 140, 100));
            float tw = Mathf.Min(width - 140, t.preferredWidth);
            UiKit.Img(row, icon, new Vector2(0.5f, 0.5f), new Vector2(-tw / 2 - 20, 0), new Vector2(92, 92)).gameObject.AddComponent<UiBob>().Amp = 4;
        }

        void HideAll()
        {
            foreach (var r in new[] { menuRoot, pauseRoot, settingsRoot, controlsRoot, introRoot, winRoot, overlayRoot }) r.gameObject.SetActive(false);
        }

        void SetCursor(bool free)
        {
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }

        ThirdPersonCamera Cam => Camera.main ? Camera.main.GetComponent<ThirdPersonCamera>() : null;

        // ------------------------------------------------------------ states
        void ShowMenu()
        {
            State = UiState.Menu; Time.timeScale = 1;
            HideAll(); overlayRoot.gameObject.SetActive(true); menuRoot.gameObject.SetActive(true);
            Hud.Visible = false;
            if (Cam) Cam.enabled = false;
            SetCursor(true);
            menuAngle = Random.Range(0f, 360f);
        }

        void StartIntro()
        {
            State = UiState.Intro; Time.timeScale = 1;
            HideAll(); introRoot.gameObject.SetActive(true);
            Hud.Visible = false;
            if (Cam) Cam.enabled = true;
            SetCursor(false);
            StartCoroutine(Roulette());
        }

        IEnumerator Roulette()
        {
            var def = SewerGame.Instance.Player.Def;
            int idx = 0; float delay = 0.05f;
            for (int i = 0; i < 18; i++)
            {
                var d = Roster.All[idx++ % Roster.All.Length];
                ShowChar(d);
                yield return new WaitForSecondsRealtime(delay);
                delay *= 1.16f;
            }
            ShowChar(def);
            introRoot.localScale = Vector3.one * 1.08f;
            for (float t = 0; t < 1; t += Time.unscaledDeltaTime / 0.25f) { introRoot.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, t); yield return null; }
            yield return new WaitForSecondsRealtime(1.4f);
            introRoot.gameObject.SetActive(false);
            State = UiState.Playing;
            Hud.Visible = true;
            SewerGame.Instance.BeginPlay();
        }

        void ShowChar(CharacterDef d)
        {
            introName.text = d.Name.ToUpper();
            introName.color = Color.Lerp(d.Hair, Color.white, 0.3f);
            introAbility.text = "Ability: " + d.AbilityName;
            introDesc.text = d.Description;
            introIcon.sprite = UiKit.Spr(d.Ability switch { Ability.Dash => "icon_dash", Ability.Invisibility => "icon_ghost", Ability.Slam => "icon_slam", _ => "icon_flash" });
        }

        public void Pause()
        {
            if (State != UiState.Playing) return;
            State = UiState.Paused; Time.timeScale = 0;
            HideAll(); overlayRoot.gameObject.SetActive(true); pauseRoot.gameObject.SetActive(true);
            SetCursor(true);
        }

        void Resume()
        {
            if (State != UiState.Paused) return;
            State = UiState.Playing; Time.timeScale = 1;
            HideAll();
            SetCursor(false);
            if (Cam) Cam.SkipMouse();
        }

        void OpenSettings(bool fromPause)
        {
            settingsFromPause = fromPause;
            menuRoot.gameObject.SetActive(false); pauseRoot.gameObject.SetActive(false);
            settingsRoot.gameObject.SetActive(true);
        }

        void OpenControls()
        {
            settingsFromPause = State == UiState.Paused;
            menuRoot.gameObject.SetActive(false); pauseRoot.gameObject.SetActive(false);
            controlsRoot.gameObject.SetActive(true);
        }

        void CloseSubmenu()
        {
            settingsRoot.gameObject.SetActive(false); controlsRoot.gameObject.SetActive(false);
            if (State == UiState.Paused) pauseRoot.gameObject.SetActive(true);
            else menuRoot.gameObject.SetActive(true);
        }

        public void ShowWin(float time, string who)
        {
            State = UiState.Won; Time.timeScale = 1;
            HideAll(); overlayRoot.gameObject.SetActive(true); winRoot.gameObject.SetActive(true);
            winTime.text = $"{who} reached the bottom in <color=#3EF4FF>{Mathf.FloorToInt(time / 60)}:{Mathf.FloorToInt(time % 60):00}</color>";
            SetCursor(true);
        }

        void Restart() { Time.timeScale = 1; SkipMenuOnce = true; SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
        void ToMenu() { Time.timeScale = 1; SkipMenuOnce = false; SceneManager.LoadScene(SceneManager.GetActiveScene().name); }
        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ------------------------------------------------------------ per frame
        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (State == UiState.Playing) Pause();
                else if (State == UiState.Paused)
                {
                    if (settingsRoot.gameObject.activeSelf || controlsRoot.gameObject.activeSelf) CloseSubmenu();
                    else Resume();
                }
                else if (State == UiState.Menu && (settingsRoot.gameObject.activeSelf || controlsRoot.gameObject.activeSelf)) CloseSubmenu();
            }
            // re-lock the cursor when clicking back into the game
            if (State == UiState.Playing && Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                SetCursor(false);

            if (State == UiState.Menu) MenuCamera();
        }

        /// <summary>Slow cinematic flight along the first ring while the menu is open.</summary>
        void MenuCamera()
        {
            var cam = Camera.main;
            if (!cam) return;
            var d = LevelBuilder.Rings[0];
            menuAngle += Time.unscaledDeltaTime * 2.2f;
            float a = menuAngle * Mathf.Deg2Rad;
            Vector3 pos = Geo.Polar(menuAngle, d.R - 1.2f + Mathf.Sin(Time.unscaledTime * 0.3f) * 1.5f, d.Y + 3.6f + Mathf.Sin(Time.unscaledTime * 0.5f) * 0.6f);
            Vector3 tangent = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
            Vector3 look = tangent + Vector3.up * (Mathf.Sin(Time.unscaledTime * 0.4f) * 0.08f) + Geo.Out(menuAngle) * Mathf.Sin(Time.unscaledTime * 0.23f) * 0.25f;
            cam.transform.SetPositionAndRotation(pos, Quaternion.Slerp(cam.transform.rotation, Quaternion.LookRotation(look), 1 - Mathf.Exp(-3f * Time.unscaledDeltaTime)));
        }
    }
}
