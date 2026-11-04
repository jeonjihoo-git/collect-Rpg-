using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Lumina.Runtime.UI
{
    /// <summary>
    /// 가상 조이스틱(좌하단). 누른 지점 기준 상대 이동, 반경 정규화 벡터 출력.
    /// 멀티터치: 조이스틱과 액션 버튼을 동시에 누를 수 있다.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform knob;
        public float radius = 110f;
        public Vector2 Value { get; private set; }
        private RectTransform _rt;
        private int _pointer = int.MinValue;

        private void Awake() => _rt = (RectTransform)transform;

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            OnDrag(e);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_rt, e.position, e.pressEventCamera, out var local);
            local -= _rt.rect.center;
            var v = Vector2.ClampMagnitude(local, radius);
            if (knob != null) knob.anchoredPosition = v;
            var n = v / radius;
            Value = n.magnitude < 0.15f ? Vector2.zero : n;
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            Value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }

        private void OnDisable()
        {
            _pointer = int.MinValue;
            Value = Vector2.zero;
            if (knob != null) knob.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>누르는 순간 반응하는 전투 버튼(click 보다 지연이 적음). 쿨타임 오버레이/남은 초 표시.</summary>
    public sealed class ActionButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        public Image background;
        public Image cooldownFill;
        public Text label;
        public Text timer;
        public Text keyHint;
        public bool Held { get; private set; }
        public event Action Pressed;
        private int _pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e)
        {
            if (_pointer != int.MinValue) return;
            _pointer = e.pointerId;
            Held = true;
            Pressed?.Invoke();
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId != _pointer) return;
            _pointer = int.MinValue;
            Held = false;
        }

        private void OnDisable()
        {
            _pointer = int.MinValue;
            Held = false;
        }

        /// <summary>remaining/total 쿨타임 표시. ready=false 면 흐리게.</summary>
        public void SetCooldown(float remaining, float total, bool ready, Color readyColor)
        {
            float f = total > 0f ? Mathf.Clamp01(remaining / total) : 0f;
            var rt = cooldownFill.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            timer.text = remaining > 0.05f ? remaining.ToString(remaining < 1f ? "0.0" : "0") : "";
            background.color = ready ? readyColor : Color.Lerp(readyColor, Theme.Locked, 0.6f);
        }

        public static ActionButton Create(Transform parent, string name, string text, string key, float size, Color color)
        {
            var rt = Ui.Rect(parent, name);
            rt.sizeDelta = new Vector2(size, size);
            var bg = rt.gameObject.AddComponent<Image>();
            bg.color = color;
            var b = rt.gameObject.AddComponent<ActionButton>();
            b.background = bg;
            var cd = Ui.Panel(rt, "Cooldown", new Color(0f, 0f, 0f, 0.55f), false);
            b.cooldownFill = cd;
            b.label = Ui.Label(rt, text, (int)(size * 0.2f), Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            b.timer = Ui.Label(rt, "", (int)(size * 0.32f), Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            b.keyHint = Ui.Label(rt, key, (int)(size * 0.14f), Theme.Dim, TextAnchor.LowerRight);
            Ui.Place(b.keyHint.rectTransform, 0, 0, 1, 1, 0, 6, 10, 0);
            Ui.Outline(bg, new Color(0f, 0f, 0f, 0.6f), 3f);
            return b;
        }
    }

    /// <summary>탭 한 번에 콜백(파티 초상 태그 등).</summary>
    public sealed class TapArea : MonoBehaviour, IPointerDownHandler
    {
        public event Action Tapped;
        public void OnPointerDown(PointerEventData e) => Tapped?.Invoke();
    }
}
