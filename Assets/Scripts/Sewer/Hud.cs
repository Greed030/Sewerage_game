using UnityEngine;
using UnityEngine.UI;

namespace Sewer
{
    /// <summary>Y2K in-game HUD (uGUI, built from code). Hud.Message() shows a toast.</summary>
    public class Hud : MonoBehaviour
    {
        static string msg;
        static float msgUntil;
        static int msgId;

        public static void Message(string text, float duration = 3f)
        {
            msg = text; msgUntil = Time.time + duration; msgId++;
        }

        Canvas canvas;
        CanvasGroup group;
        Image hpFill, abilityShade, abilityIcon, bossFill, compass, deathTint, msgBg, hpHeart;
        Text nameTxt, hpTxt, abilityTxt, ringTxt, timeTxt, goalTxt, bossName, msgTxt;
        RectTransform bossRoot, msgRoot, abilityRoot;
        int shownMsg = -1;
        float hpShown = 1, msgPop;
        bool visible = true;

        public bool Visible { get => visible; set { visible = value; if (group) group.alpha = value ? 1 : 0; } }

        static string AbilityIcon(Ability a) => a switch
        {
            Ability.Dash => "icon_dash", Ability.Invisibility => "icon_ghost", Ability.Slam => "icon_slam", _ => "icon_flash",
        };

        void Start()
        {
            canvas = UiKit.MakeCanvas("HUD", 10, transform);
            group = canvas.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            group.alpha = visible ? 1 : 0;
            var root = canvas.transform;
            var p = SewerGame.Instance.Player;

            deathTint = UiKit.Img(root, null, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, 0, new Color(0.6f, 0, 0.15f, 0));
            var dr = deathTint.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.sizeDelta = Vector2.zero;

            // ---- top-left: name + HP
            var tl = UiKit.Img(root, "panel", new Vector2(0, 1), new Vector2(24, -20), new Vector2(600, 176), 60, new Color(1, 1, 1, 0.96f), new Vector2(0, 1));
            hpHeart = UiKit.Img(tl.transform, "icon_heart", new Vector2(0, 0.5f), new Vector2(88, 0), new Vector2(86, 86), 0, null, new Vector2(0.5f, 0.5f));
            nameTxt = UiKit.Txt(tl.transform, p.Def.Name.ToUpper(), 34, UiKit.PinkLight, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(160, -30), new Vector2(400, 46), true, true, new Vector2(0, 1));
            var hpBg = UiKit.Img(tl.transform, "bar_bg", new Vector2(0, 0), new Vector2(146, 34), new Vector2(414, 50), 32, null, new Vector2(0, 0));
            hpFill = UiKit.Img(hpBg.transform, "bar_fill", new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(406, 42), 32, UiKit.Pink, new Vector2(0, 0.5f));
            UiKit.Img(hpBg.transform, "bar_frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(414, 50), 32);
            hpTxt = UiKit.Txt(hpBg.transform, "", 24, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 1), new Vector2(400, 44));

            // ---- bottom-left: ability ring + bat
            abilityRoot = UiKit.Rect(root, "Ability", new Vector2(0, 0), new Vector2(44, 64), new Vector2(190, 190), new Vector2(0, 0));
            UiKit.Img(abilityRoot, "ring_bg", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(190, 190));
            abilityIcon = UiKit.Img(abilityRoot, AbilityIcon(p.Def.Ability), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(128, 128));
            abilityShade = UiKit.Img(abilityRoot, "circle", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(176, 176), 0, new Color(0.05f, 0.02f, 0.1f, 0.72f));
            abilityShade.type = Image.Type.Filled; abilityShade.fillMethod = Image.FillMethod.Radial360; abilityShade.fillOrigin = 2; abilityShade.fillClockwise = false;
            UiKit.Img(abilityRoot, "ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 200));
            abilityTxt = UiKit.Txt(abilityRoot, "", 52, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(180, 80));
            var keyBadge = UiKit.Img(abilityRoot, "btn_primary", new Vector2(1, 1), new Vector2(-2, -2), new Vector2(70, 46), 64, null, new Vector2(1, 1));
            UiKit.Txt(keyBadge.transform, "E", 28, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(70, 46));
            UiKit.Txt(abilityRoot, p.Def.AbilityName.ToUpper(), 24, UiKit.PinkLight, TextAnchor.MiddleCenter, new Vector2(0.5f, 0), new Vector2(0, -32), new Vector2(320, 40));

            var batRoot = UiKit.Rect(root, "Bat", new Vector2(0, 0), new Vector2(250, 60), new Vector2(130, 130), new Vector2(0, 0));
            UiKit.Img(batRoot, "ring_bg", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130, 130));
            UiKit.Img(batRoot, "icon_bat", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(92, 92));
            UiKit.Img(batRoot, "ring", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(138, 138));
            var lmb = UiKit.Img(batRoot, "btn_normal", new Vector2(1, 1), new Vector2(4, -2), new Vector2(78, 40), 64, null, new Vector2(1, 1));
            UiKit.Txt(lmb.transform, "LMB", 20, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(80, 40));

            // ---- top-right: ring badge, timer, objective
            var badge = UiKit.Img(root, "btn_normal", new Vector2(1, 1), new Vector2(-24, -24), new Vector2(320, 80), 64, null, new Vector2(1, 1));
            ringTxt = UiKit.Txt(badge.transform, "", 30, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(290, 70));
            var tbadge = UiKit.Img(root, "panel", new Vector2(1, 1), new Vector2(-24, -114), new Vector2(220, 84), 40, new Color(1, 1, 1, 0.96f), new Vector2(1, 1));
            UiKit.Img(tbadge.transform, "icon_clock", new Vector2(0, 0.5f), new Vector2(20, 0), new Vector2(54, 54), 0, null, new Vector2(0, 0.5f));
            timeTxt = UiKit.Txt(tbadge.transform, "0:00", 30, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.5f), new Vector2(80, 2), new Vector2(124, 60), true, true, new Vector2(0, 0.5f));
            var goal = UiKit.Img(root, "panel", new Vector2(1, 1), new Vector2(-24, -208), new Vector2(560, 132), 60, new Color(1, 1, 1, 0.96f), new Vector2(1, 1));
            compass = UiKit.Img(goal.transform, "icon_arrow", new Vector2(0, 0.5f), new Vector2(86, 0), new Vector2(64, 64), 0, null, new Vector2(0.5f, 0.5f));
            goalTxt = UiKit.Txt(goal.transform, "", 24, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), new Vector2(136, 0), new Vector2(380, 84), false, true, new Vector2(0, 0.5f));

            // ---- top-center: boss bar
            bossRoot = UiKit.Rect(root, "Boss", new Vector2(0.5f, 1), new Vector2(0, -26), new Vector2(820, 120), new Vector2(0.5f, 1));
            bossName = UiKit.Txt(bossRoot, "", 36, UiKit.Lilac, TextAnchor.MiddleCenter, new Vector2(0.5f, 1), new Vector2(30, 0), new Vector2(760, 50), true, true, new Vector2(0.5f, 1));
            var bossBg = UiKit.Img(bossRoot, "bar_bg", new Vector2(0.5f, 0), new Vector2(40, 14), new Vector2(700, 50), 32, null, new Vector2(0.5f, 0));
            bossFill = UiKit.Img(bossBg.transform, "bar_fill", new Vector2(0, 0.5f), new Vector2(4, 0), new Vector2(692, 42), 32, UiKit.Violet, new Vector2(0, 0.5f));
            UiKit.Img(bossBg.transform, "bar_frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 50), 32);
            UiKit.Img(bossRoot, "icon_skull", new Vector2(0, 0), new Vector2(0, -4), new Vector2(96, 96), 0, null, new Vector2(0, 0));

            // ---- bottom-center: toast
            msgBg = UiKit.Img(root, "panel", new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(1000, 124), 60, new Color(1, 1, 1, 0.95f), new Vector2(0.5f, 0));
            msgRoot = msgBg.rectTransform;
            msgTxt = UiKit.Txt(msgBg.transform, "", 34, Color.white, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0, 2), new Vector2(920, 100), false);

            UiKit.Txt(root, "Esc — pause", 22, new Color(1, 1, 1, 0.6f), TextAnchor.LowerRight, new Vector2(1, 0), new Vector2(-24, 18), new Vector2(400, 40), false, true, new Vector2(1, 0));
        }

        void Update()
        {
            var game = SewerGame.Instance;
            if (game == null || game.Player == null || nameTxt == null) return;
            var p = game.Player;
            float now = Time.time;

            // HP (smooth) + heart beat when low
            float hp = p.Hp.Current / p.Hp.Max;
            hpShown = Mathf.MoveTowards(hpShown, hp, Time.deltaTime * 1.5f);
            hpFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(42, 406 * hpShown), 42);
            hpFill.enabled = hpShown > 0.01f;
            hpTxt.text = $"{Mathf.CeilToInt(p.Hp.Current)} / {p.Hp.Max:0}";
            float beat = hp < 0.35f ? 1 + Mathf.Abs(Mathf.Sin(now * 7f)) * 0.18f : 1 + Mathf.Abs(Mathf.Sin(now * 2.2f)) * 0.04f;
            hpHeart.rectTransform.localScale = Vector3.one * beat;

            // ability cooldown
            float left = Mathf.Max(0, p.AbilityReadyAt - now);
            float frac = left / Mathf.Max(0.01f, p.AbilityCooldown);
            abilityShade.fillAmount = frac;
            abilityTxt.text = left > 0 ? left.ToString(left < 10 ? "0.0" : "0") : "";
            abilityRoot.localScale = Vector3.one * (left <= 0 ? 1 + Mathf.Sin(now * 4f) * 0.035f : 1);
            abilityIcon.color = left > 0 ? new Color(0.7f, 0.7f, 0.8f) : Color.white;

            // ring / time / objective
            int lvl = LevelBuilder.LevelOf(p.transform.position.y);
            ringTxt.text = lvl < 3 ? $"RING {lvl + 1}/3" : "CROWN HALL";
            float t = game.Finished ? game.FinishTime : (game.Started ? now - game.StartTime : 0);
            timeTxt.text = $"{Mathf.FloorToInt(t / 60)}:{Mathf.FloorToInt(t % 60):00}";
            string goal = Objective(p, lvl, out Vector3 goalPos);
            if (goal != null && p.Cam)
            {
                Vector3 to = goalPos - p.transform.position; to.y = 0;
                Vector3 camF = Vector3.ProjectOnPlane(p.Cam.forward, Vector3.up).normalized;
                float ang = Vector3.SignedAngle(camF, to, Vector3.up);
                compass.rectTransform.localRotation = Quaternion.Euler(0, 0, -ang);
                goalTxt.text = $"{goal}\n<color=#3EF4FF>{to.magnitude:0} m</color>";
            }

            // boss
            var b = game.ActiveBoss;
            bool showBoss = b != null && !b.Hp.Dead;
            if (bossRoot.gameObject.activeSelf != showBoss) bossRoot.gameObject.SetActive(showBoss);
            if (showBoss)
            {
                bossName.text = b.Title.ToUpper();
                bossFill.rectTransform.sizeDelta = new Vector2(Mathf.Max(42, 692 * b.Hp.Current / b.Hp.Max), 42);
            }

            // toast
            if (msgId != shownMsg) { shownMsg = msgId; msgTxt.text = msg; msgPop = 0; }
            bool showMsg = now < msgUntil && !string.IsNullOrEmpty(msg);
            if (msgBg.gameObject.activeSelf != showMsg) msgBg.gameObject.SetActive(showMsg);
            if (showMsg)
            {
                msgPop = Mathf.MoveTowards(msgPop, 1, Time.unscaledDeltaTime * 6f);
                float pop = msgPop < 1 ? 1 + Mathf.Sin(msgPop * Mathf.PI) * 0.12f : 1;
                float a = Mathf.Clamp01((msgUntil - now) * 2f);
                msgRoot.localScale = Vector3.one * pop * Mathf.Lerp(0.6f, 1f, Mathf.Min(1, msgPop * 3));
                msgBg.color = new Color(1, 1, 1, 0.95f * a);
                msgTxt.color = new Color(1, 1, 1, a);
            }

            // death tint
            float want = p.Hp.Dead ? 0.42f : 0;
            var dc = deathTint.color; dc.a = Mathf.MoveTowards(dc.a, want, Time.deltaTime * 2f); deathTint.color = dc;
        }

        static string Objective(Player p, int lvl, out Vector3 pos)
        {
            pos = Vector3.zero;
            var game = SewerGame.Instance;
            if (lvl >= 3) { pos = game.Level.CrownPos; return "Crown"; }
            float best = float.MaxValue; string res = null;
            foreach (var r in game.Level.Rooms)
            {
                if (r.Level != lvl || r.Kind != RoomKind.Gate) continue;
                float d = (r.Center - p.transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d; pos = r.Center;
                    res = r.Gate != null && r.Gate.IsOpen ? "Open hatch — jump in!" : "Guard room (hatch down)";
                }
            }
            return res;
        }
    }
}
