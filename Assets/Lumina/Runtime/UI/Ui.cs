using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lumina.Runtime.UI
{
    /// <summary>코드로 uGUI 를 구성하는 최소 헬퍼(프리팹 없이 클린 체크아웃에서 바로 동작).</summary>
    public static class Ui
    {
        public const float RefW = 1920f;
        public const float RefH = 1080f;

        public static Canvas CreateCanvas(string name, int sortingOrder)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(RefW, RefH);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.6f;
            go.AddComponent<GraphicRaycaster>();
            return canvas;
        }

        public static void EnsureEventSystem()
        {
            if (UnityEngine.Object.FindFirstObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if ENABLE_LEGACY_INPUT_MANAGER
            go.AddComponent<StandaloneInputModule>();
#else
            // 구 Input Manager 가 꺼진 프로젝트: Input System 의 UI 모듈을 리플렉션으로 추가(패키지 직접 참조 없음)
            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (module != null) go.AddComponent(module);
            else Debug.LogError("[Lumina] 구 Input Manager 가 꺼져 있고 Input System 패키지도 없어 UI 입력이 동작하지 않습니다. " +
                                "Edit > Project Settings > Player > Other Settings > Active Input Handling 을 'Both' 로 바꾸고 Unity 를 재시작하세요.");
#endif
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static RectTransform Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>앵커(0~1)와 픽셀 오프셋으로 배치.</summary>
        public static RectTransform Place(RectTransform rt, float xMin, float yMin, float xMax, float yMax, float l = 0, float b = 0, float r = 0, float t = 0)
        {
            rt.anchorMin = new Vector2(xMin, yMin);
            rt.anchorMax = new Vector2(xMax, yMax);
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
            return rt;
        }

        /// <summary>고정 크기 박스를 앵커 지점 기준으로 배치(pivot 동일).</summary>
        public static RectTransform Box(RectTransform rt, float ax, float ay, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
            return rt;
        }

        public static Image Panel(Transform parent, string name, Color color, bool raycast = true)
        {
            var rt = Rect(parent, name);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            Place(rt, 0, 0, 1, 1);
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect(parent, "Text");
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Fonts.Main;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            Place(rt, 0, 0, 1, 1);
            return t;
        }

        public static Button Button(Transform parent, string label, Action onClick, Color bg, int fontSize = 30, Color? textColor = null)
        {
            var rt = Rect(parent, "Btn_" + label);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = bg;
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.1f, 1.1f, 1.1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            colors.disabledColor = new Color(0.45f, 0.45f, 0.5f, 0.8f);
            btn.colors = colors;
            if (onClick != null) btn.onClick.AddListener(() =>
            {
                GameApp.Instance?.PlayUiClick();
                onClick();
            });
            var t = Label(rt, label, fontSize, textColor ?? Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Place(t.rectTransform, 0, 0, 1, 1, 8, 4, 8, 4);
            return btn;
        }

        public static Text ButtonText(Button b) => b.GetComponentInChildren<Text>();

        public static Image Bar(Transform parent, string name, Color bg, Color fill, out Image fillImage)
        {
            var back = Panel(parent, name, bg, false);
            var f = Panel(back.transform, "Fill", fill, false);
            f.rectTransform.pivot = new Vector2(0f, 0.5f);
            fillImage = f;
            return back;
        }

        /// <summary>Bar 의 채움 비율(0~1) 설정 — 앵커 기반이라 스프라이트 없이 동작.</summary>
        public static void SetFill(Image fill, float v)
        {
            v = Mathf.Clamp01(v);
            var rt = fill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(v, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static void Outline(Graphic g, Color c, float d = 2f)
        {
            var o = g.gameObject.AddComponent<Outline>();
            o.effectColor = c;
            o.effectDistance = new Vector2(d, -d);
        }

        public static string Stars(int n, int max = 3)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++) sb.Append(i < n ? "★" : "☆");
            return sb.ToString();
        }

        public static string ColorTag(string text, Color c) => $"<color=#{ColorUtility.ToHtmlStringRGB(c)}>{text}</color>";
    }

    /// <summary>노치/안전영역 대응: 부모 전체를 Screen.safeArea 로 맞춘다.</summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        private Rect _last;
        private Vector2Int _lastScreen;

        private void Update()
        {
            var sa = Screen.safeArea;
            var sc = new Vector2Int(Screen.width, Screen.height);
            if (sa == _last && sc == _lastScreen) return;
            _last = sa;
            _lastScreen = sc;
            var rt = (RectTransform)transform;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            rt.anchorMin = new Vector2(sa.xMin / Screen.width, sa.yMin / Screen.height);
            rt.anchorMax = new Vector2(sa.xMax / Screen.width, sa.yMax / Screen.height);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
