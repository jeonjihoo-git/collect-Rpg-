using Lumina.Core.Combat;
using Lumina.Core.Util;
using Lumina.Runtime.UI;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>
    /// 키보드(WASD/방향키, J 일반, K 스킬, L 궁극기, Space 회피, 1~4 태그, Esc 일시정지)와 터치 HUD 를 하나의 PlayerInput 으로 합친다.
    /// 눌림(pressed)은 다음 시뮬레이션 틱 1회에만 전달된다. 카메라가 고정 3/4 뷰이므로 화면 위 = 월드 +Z.
    /// </summary>
    public sealed class BattleInput
    {
        public VirtualJoystick joystick;
        public ActionButton attack, skill, ultimate, dodge;
        private bool _attackP, _skillP, _ultP, _dodgeP;
        private int _tag = -1;
        public bool PausePressed { get; private set; }

        public void Bind(VirtualJoystick j, ActionButton a, ActionButton s, ActionButton u, ActionButton d)
        {
            joystick = j;
            attack = a;
            skill = s;
            ultimate = u;
            dodge = d;
            a.Pressed += () => _attackP = true;
            s.Pressed += () => _skillP = true;
            u.Pressed += () => _ultP = true;
            d.Pressed += () => _dodgeP = true;
        }

        public void RequestTag(int i) => _tag = i;

        /// <summary>매 프레임 키보드 엣지 입력 수집.</summary>
        public void Poll()
        {
            PausePressed = false;
            if (Down(KeyCode.J, "jKey")) _attackP = true;
            if (Down(KeyCode.K, "kKey")) _skillP = true;
            if (Down(KeyCode.L, "lKey")) _ultP = true;
            if (Down(KeyCode.Space, "spaceKey")) _dodgeP = true;
            if (Down(KeyCode.Alpha1, "digit1Key")) _tag = 0;
            if (Down(KeyCode.Alpha2, "digit2Key")) _tag = 1;
            if (Down(KeyCode.Alpha3, "digit3Key")) _tag = 2;
            if (Down(KeyCode.Alpha4, "digit4Key")) _tag = 3;
            if (Down(KeyCode.Escape, "escapeKey")) PausePressed = true;
        }

        private static Vector2 KeyboardMove(out bool attackHeld)
        {
            Vector2 v = Vector2.zero;
            if (Held(KeyCode.W, "wKey") || Held(KeyCode.UpArrow, "upArrowKey")) v.y += 1f;
            if (Held(KeyCode.S, "sKey") || Held(KeyCode.DownArrow, "downArrowKey")) v.y -= 1f;
            if (Held(KeyCode.D, "dKey") || Held(KeyCode.RightArrow, "rightArrowKey")) v.x += 1f;
            if (Held(KeyCode.A, "aKey") || Held(KeyCode.LeftArrow, "leftArrowKey")) v.x -= 1f;
            attackHeld = Held(KeyCode.J, "jKey");
            return v.sqrMagnitude > 1f ? v.normalized : v;
        }

        // 구 Input Manager 가 켜져 있으면(Old/Both) 그대로 사용, 꺼져 있으면 Input System 을 리플렉션으로 읽는다.
        // Input System 패키지를 직접 참조하지 않으므로 패키지 유무·입력 설정과 관계없이 컴파일된다.
        private static bool Down(KeyCode legacy, string newKey)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(legacy);
#else
            return NewInputKeyboard.Get(newKey, true);
#endif
        }

        private static bool Held(KeyCode legacy, string newKey)
        {
#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKey(legacy);
#else
            return NewInputKeyboard.Get(newKey, false);
#endif
        }

        /// <summary>시뮬레이션 1틱분 입력을 만들고 눌림 플래그를 소비한다.</summary>
        public PlayerInput ConsumeTick()
        {
            var kbMove = KeyboardMove(out bool kbHeld);
            Vector2 mv = joystick != null && joystick.Value.sqrMagnitude > 0.0001f ? joystick.Value : kbMove;
            var input = new PlayerInput
            {
                move = new Vec2(mv.x, mv.y),
                attackPressed = _attackP,
                attackHeld = kbHeld || (attack != null && attack.Held),
                skillPressed = _skillP,
                ultimatePressed = _ultP,
                dodgePressed = _dodgeP,
                tagTo = _tag
            };
            _attackP = _skillP = _ultP = _dodgeP = false;
            _tag = -1;
            return input;
        }

        public void Clear()
        {
            _attackP = _skillP = _ultP = _dodgeP = false;
            _tag = -1;
        }
    }

    /// <summary>
    /// Input System(com.unity.inputsystem) 키보드를 리플렉션으로 읽는다. 패키지가 없으면 항상 false.
    /// 구 Input Manager 가 꺼진 프로젝트에서만 사용된다.
    /// </summary>
    internal static class NewInputKeyboard
    {
        private static readonly System.Type KeyboardType = System.Type.GetType("UnityEngine.InputSystem.Keyboard, Unity.InputSystem");
        private static readonly System.Reflection.PropertyInfo Current = KeyboardType?.GetProperty("current", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.PropertyInfo> KeyProps = new System.Collections.Generic.Dictionary<string, System.Reflection.PropertyInfo>();
        private static System.Reflection.PropertyInfo _isPressed;
        private static System.Reflection.PropertyInfo _wasPressed;

        public static bool Get(string keyProperty, bool pressedThisFrame)
        {
            object kb = Current?.GetValue(null);
            if (kb == null) return false;
            if (!KeyProps.TryGetValue(keyProperty, out var prop))
            {
                prop = KeyboardType.GetProperty(keyProperty);
                KeyProps[keyProperty] = prop;
            }
            object control = prop?.GetValue(kb);
            if (control == null) return false;
            var state = pressedThisFrame
                ? (_wasPressed ?? (_wasPressed = control.GetType().GetProperty("wasPressedThisFrame")))
                : (_isPressed ?? (_isPressed = control.GetType().GetProperty("isPressed")));
            return state != null && (bool)state.GetValue(control);
        }
    }
}
