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
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.J)) _attackP = true;
            if (Input.GetKeyDown(KeyCode.K)) _skillP = true;
            if (Input.GetKeyDown(KeyCode.L)) _ultP = true;
            if (Input.GetKeyDown(KeyCode.Space)) _dodgeP = true;
            if (Input.GetKeyDown(KeyCode.Alpha1)) _tag = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2)) _tag = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3)) _tag = 2;
            if (Input.GetKeyDown(KeyCode.Alpha4)) _tag = 3;
            if (Input.GetKeyDown(KeyCode.Escape)) PausePressed = true;
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.jKey.wasPressedThisFrame) _attackP = true;
                if (kb.kKey.wasPressedThisFrame) _skillP = true;
                if (kb.lKey.wasPressedThisFrame) _ultP = true;
                if (kb.spaceKey.wasPressedThisFrame) _dodgeP = true;
                if (kb.digit1Key.wasPressedThisFrame) _tag = 0;
                if (kb.digit2Key.wasPressedThisFrame) _tag = 1;
                if (kb.digit3Key.wasPressedThisFrame) _tag = 2;
                if (kb.digit4Key.wasPressedThisFrame) _tag = 3;
                if (kb.escapeKey.wasPressedThisFrame) PausePressed = true;
            }
#endif
        }

        private static Vector2 KeyboardMove(out bool attackHeld)
        {
            Vector2 v = Vector2.zero;
            attackHeld = false;
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v.y += 1f;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v.y -= 1f;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) v.x += 1f;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) v.x -= 1f;
            attackHeld = Input.GetKey(KeyCode.J);
#elif ENABLE_INPUT_SYSTEM
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
                attackHeld = kb.jKey.isPressed;
            }
#endif
            return v.sqrMagnitude > 1f ? v.normalized : v;
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
}
