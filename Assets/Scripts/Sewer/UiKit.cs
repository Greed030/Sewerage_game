using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Sewer
{
    /// <summary>Tiny code-only uGUI toolkit with the Y2K skin (sprites in Resources/SewerUI).</summary>
    public static class UiKit
    {
        static readonly Dictionary<string, Sprite> sprites = new();
        static Font title, body;

        public static readonly Color Ink = new Color(0.07f, 0.04f, 0.12f);
        // one palette: ink-indigo glass, pearl, hot pink, violet, cyan
        public static readonly Color Pink = Mat.Hex("#FF4FCB"), PinkLight = Mat.Hex("#FFB8EE"), Cyan = Mat.Hex("#6FF0FF"), Violet = Mat.Hex("#8A63FF"), Lilac = Mat.Hex("#D9C6FF");
        public static readonly Color Gold = PinkLight, Lime = Cyan;   // legacy names

        public static Font TitleFont { get { if (!title) title = Resources.Load<Font>("SewerUI/SewerTitle"); if (!title) title = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); return title; } }
        public static Font BodyFont { get { if (!body) body = Resources.Load<Font>("SewerUI/SewerBody"); if (!body) body = TitleFont; return body; } }

        public static Sprite Spr(string name, float border = 0)
        {
            string key = name + "|" + border;
            if (sprites.TryGetValue(key, out var s) && s) return s;
            var tex = Resources.Load<Texture2D>("SewerUI/" + name);
            if (!tex) return null;
            tex.wrapMode = TextureWrapMode.Clamp;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect, Vector4.one * border);
            s.name = name;
            sprites[key] = s;
            return s;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>()) return;
            var go = new GameObject("EventSystem", typeof(EventSystem));
            var m = go.AddComponent<InputSystemUIInputModule>();
            m.AssignDefaultActions();
        }

        public static Canvas MakeCanvas(string name, int order, Transform parent = null)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            if (parent) go.transform.SetParent(parent, false);
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = order;
            var sc = go.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>RectTransform helper. anchor = (x,y) in 0..1 for both min/max (point anchor) unless stretch.</summary>
        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(Transform parent, string name, float inset = 0)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.one * inset; rt.offsetMax = -Vector2.one * inset;
            return rt;
        }

        public static Image Img(Transform parent, string sprite, Vector2 anchor, Vector2 pos, Vector2 size, float border = 0, Color? color = null, Vector2? pivot = null)
        {
            if (sprite != null && border > 0)   // each skin sprite has one correct 9-slice border
                border = sprite.StartsWith("btn") ? 64 : sprite.StartsWith("bar") ? 32 : sprite == "panel" ? 72 : border;
            var rt = Rect(parent, sprite ?? "Image", anchor, pos, size, pivot);
            var im = rt.gameObject.AddComponent<Image>();
            if (sprite != null) im.sprite = Spr(sprite, border);
            im.type = border > 0 ? Image.Type.Sliced : Image.Type.Simple;
            if (border > 0 && im.sprite)
            {
                float minSide = Mathf.Max(1f, Mathf.Min(size.x, size.y));
                bool pillLike = sprite.StartsWith("btn") || sprite.StartsWith("bar");
                im.pixelsPerUnitMultiplier = pillLike ? im.sprite.rect.height / Mathf.Max(1f, size.y) : Mathf.Max(1f, border * 2.05f / minSide);
            }
            im.preserveAspect = border <= 0 && sprite != null;
            im.color = color ?? Color.white;
            im.raycastTarget = false;
            return im;
        }

        public static Text Txt(Transform parent, string text, int size, Color color, TextAnchor align, Vector2 anchor, Vector2 pos, Vector2 box, bool titleFont = true, bool outline = true, Vector2? pivot = null)
        {
            var rt = Rect(parent, "Text", anchor, pos, box, pivot);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = titleFont ? TitleFont : BodyFont;
            t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false; t.supportRichText = true;
            if (outline)
            {
                var o = rt.gameObject.AddComponent<Outline>(); o.effectColor = Ink; o.effectDistance = new Vector2(3, -3);
                var sh = rt.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0.16f, 0.04f, 0.3f, 0.9f); sh.effectDistance = new Vector2(4, -6);
            }
            return t;
        }

        public static Button Btn(Transform parent, string label, string icon, Vector2 anchor, Vector2 pos, Vector2 size, Action onClick, string style = "btn_normal", int fontSize = 0)
        {
            var im = Img(parent, style, anchor, pos, size, 64);
            im.raycastTarget = true; im.name = "Btn_" + label;
            var b = im.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.SpriteSwap;
            b.spriteState = new SpriteState { highlightedSprite = Spr("btn_hover", 64), pressedSprite = Spr("btn_press", 64), selectedSprite = Spr(style, 64), disabledSprite = Spr("btn_press", 64) };
            var nav = b.navigation; nav.mode = Navigation.Mode.None; b.navigation = nav;
            b.onClick.AddListener(() => onClick?.Invoke());
            float h = size.y, left = h * 0.32f;
            if (icon != null)
            {
                float isz = h * 0.66f;
                Img(im.transform, "icon_" + icon, new Vector2(0, 0.5f), new Vector2(h * 0.24f, 0), Vector2.one * isz, 0, null, new Vector2(0, 0.5f));
                left = h * 0.24f + isz + h * 0.08f;
            }
            float right = h * 0.36f;
            if (fontSize <= 0) fontSize = Mathf.RoundToInt(h * 0.36f);
            var t = Txt(im.transform, label, fontSize, Color.white, TextAnchor.MiddleCenter, new Vector2(0, 0.5f), new Vector2(left, 2), new Vector2(size.x - left - right, h * 0.9f), true, true, new Vector2(0, 0.5f));
            t.resizeTextForBestFit = true; t.resizeTextMinSize = 14; t.resizeTextMaxSize = fontSize;
            im.gameObject.AddComponent<UiHover>();
            return b;
        }

        public static Slider Sld(Transform parent, string label, Vector2 anchor, Vector2 pos, float width, float min, float max, float value, Action<float> onChange, Func<float, string> fmt)
        {
            var root = Rect(parent, "Slider_" + label, anchor, pos, new Vector2(width, 90));
            var lbl = Txt(root, label, 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 0), new Vector2(width, 40), false, true, new Vector2(0, 1));
            var val = Txt(root, "", 30, Cyan, TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(0, 0), new Vector2(240, 40), true, true, new Vector2(1, 1));
            var bg = Img(root, "bar_bg", new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(width, 40), 24, null, new Vector2(0.5f, 0));
            bg.raycastTarget = true;
            var fillArea = Stretch(bg.transform, "FillArea", 6);
            var fill = Img(fillArea, "bar_fill", new Vector2(0, 0.5f), Vector2.zero, new Vector2(10, 0), 24, Pink);
            var frt = fill.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = new Vector2(0, 1); frt.sizeDelta = Vector2.zero;
            Img(bg.transform, "bar_frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, 40), 24);
            var handleArea = Stretch(bg.transform, "HandleArea", 0);
            handleArea.offsetMin = new Vector2(20, 0); handleArea.offsetMax = new Vector2(-20, 0);
            var handle = Img(handleArea, "knob", new Vector2(0, 0.5f), Vector2.zero, new Vector2(62, 62));
            handle.raycastTarget = true;
            var s = bg.gameObject.AddComponent<Slider>();
            s.fillRect = frt; s.handleRect = handle.rectTransform; s.targetGraphic = handle;
            s.direction = Slider.Direction.LeftToRight; s.minValue = min; s.maxValue = max;
            var nav = s.navigation; nav.mode = Navigation.Mode.None; s.navigation = nav;
            s.value = value; val.text = fmt(value);
            s.onValueChanged.AddListener(v => { val.text = fmt(v); onChange?.Invoke(v); });
            return s;
        }

        public static Toggle Tgl(Transform parent, string label, Vector2 anchor, Vector2 pos, float width, bool value, Action<bool> onChange)
        {
            var root = Rect(parent, "Toggle_" + label, anchor, pos, new Vector2(width, 64));
            Txt(root, label, 30, Color.white, TextAnchor.MiddleLeft, new Vector2(0, 0.5f), Vector2.zero, new Vector2(width - 150, 60), false, true, new Vector2(0, 0.5f));
            var bg = Img(root, "bar_bg", new Vector2(1, 0.5f), Vector2.zero, new Vector2(120, 56), 32, null, new Vector2(1, 0.5f));
            bg.raycastTarget = true;
            var fill = Img(bg.transform, "bar_fill", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(112, 48), 32, Pink);
            Img(bg.transform, "bar_frame", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(120, 56), 32);
            var knob = Img(bg.transform, "knob", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(60, 60));
            var t = bg.gameObject.AddComponent<Toggle>();
            t.targetGraphic = bg; t.graphic = fill; t.isOn = value;
            var nav = t.navigation; nav.mode = Navigation.Mode.None; t.navigation = nav;
            void Place(bool v) => knob.rectTransform.anchoredPosition = new Vector2(v ? 32 : -32, 0);
            Place(value);
            t.onValueChanged.AddListener(v => { Place(v); onChange?.Invoke(v); });
            bg.gameObject.AddComponent<UiHover>();
            return t;
        }
    }

    /// <summary>Bouncy hover scale (works while the game is paused).</summary>
    public class UiHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler
    {
        float target = 1f, cur = 1f;
        public void OnPointerEnter(PointerEventData e) => target = 1.07f;
        public void OnPointerExit(PointerEventData e) => target = 1f;
        public void OnPointerDown(PointerEventData e) { cur = 0.92f; }
        void OnDisable() { cur = target = 1f; transform.localScale = Vector3.one; }
        void Update()
        {
            cur = Mathf.Lerp(cur, target, 1 - Mathf.Exp(-14f * Time.unscaledDeltaTime));
            transform.localScale = Vector3.one * cur;
        }
    }

    /// <summary>Gentle float / pulse for logos and badges.</summary>
    public class UiBob : MonoBehaviour
    {
        public float Amp = 10f, Speed = 1.6f, Pulse = 0.03f, Spin;
        Vector2 basePos; RectTransform rt;
        void Start() { rt = (RectTransform)transform; basePos = rt.anchoredPosition; }
        void Update()
        {
            float t = Time.unscaledTime * Speed;
            rt.anchoredPosition = basePos + Vector2.up * Mathf.Sin(t) * Amp;
            rt.localScale = Vector3.one * (1 + Mathf.Sin(t * 1.3f) * Pulse);
            if (Spin != 0) rt.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 0.7f) * Spin);
        }
    }

    /// <summary>Twinkling sparkle.</summary>
    public class UiTwinkle : MonoBehaviour
    {
        float phase, speed;
        Image im;
        void Start() { im = GetComponent<Image>(); phase = UnityEngine.Random.value * 10f; speed = UnityEngine.Random.Range(1.5f, 3.5f); }
        void Update()
        {
            float k = Mathf.Max(0, Mathf.Sin(Time.unscaledTime * speed + phase));
            transform.localScale = Vector3.one * (0.3f + k);
            transform.localRotation = Quaternion.Euler(0, 0, Time.unscaledTime * 40f + phase * 30f);
            if (im) im.color = new Color(1, 1, 1, k);
        }
    }
}
