using System;
using System.Collections.Generic;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Runtime.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace Lumina.Runtime.UI
{
    /// <summary>
    /// SCREEN 13 실시간 전투 HUD. 자동전투는 표시만 한다(구버전 '자동 일반공격' 토글을 두지 않음 — 편성 화면에서 설정).
    /// </summary>
    public sealed class BattleHud
    {
        public readonly RectTransform root;
        public readonly BattleInput input = new BattleInput();
        public event Action PauseRequested;
        public event Action ResumeRequested;
        public event Action RetreatRequested;
        public event Action DebugFillResonance;

        private readonly Text _timer, _wave, _stage, _autoBadge, _manualNote, _resText, _hpText;
        private readonly Text _enemyName, _enemyAura, _enemyState;
        private readonly Image _enemyHp, _enemyBreak, _resFill, _hpFill, _shieldFill;
        private readonly RectTransform _enemyPanel;
        private readonly PartySlot[] _slots = new PartySlot[4];
        private readonly ActionButton _attack, _skill, _ult, _dodge;
        private readonly RectTransform _floatLayer;
        private readonly RectTransform _pausePanel;
        private readonly Image _hurtFlash;
        private readonly List<FloatText> _floats = new List<FloatText>();
        private readonly Queue<Text> _pool = new Queue<Text>();
        private float _hurt;

        private sealed class PartySlot
        {
            public RectTransform rt;
            public Image bg, frame, hp, strip;
            public Text name, info, key;
        }

        private sealed class FloatText
        {
            public Text text;
            public Vector3 world;
            public float life, age;
            public Vector2 drift;
        }

        public BattleHud(Transform parent, string stageName, bool auto, int partySize, bool devTools)
        {
            root = Ui.Rect(parent, "BattleHud");
            Ui.Place(root, 0, 0, 1, 1);
            root.gameObject.AddComponent<SafeAreaFitter>();

            _hurtFlash = Ui.Panel(root, "HurtFlash", new Color(1f, 0.1f, 0.1f, 0f), false);
            _floatLayer = Ui.Rect(root, "Floats");
            Ui.Place(_floatLayer, 0, 0, 1, 1);

            // 상단: 스테이지/타이머/웨이브
            var top = Ui.Rect(root, "Top");
            Ui.Box(top, 0.5f, 1f, 0f, -12f, 520f, 120f);
            _stage = Ui.Label(top, stageName, 24, Theme.Dim, TextAnchor.UpperCenter);
            _timer = Ui.Label(top, "3:00", 52, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Place(_timer.rectTransform, 0, 0, 1, 1, 0, 0, 0, 22);
            _wave = Ui.Label(top, "WAVE 1/1", 22, Theme.Gold, TextAnchor.LowerCenter, FontStyle.Bold);

            // 적 정보
            _enemyPanel = Ui.Rect(root, "Enemy");
            Ui.Box(_enemyPanel, 0.5f, 1f, 0f, -135f, 760f, 96f);
            Ui.Panel(_enemyPanel, "Bg", new Color(0f, 0f, 0f, 0.45f), false);
            _enemyName = Ui.Label(_enemyPanel, "", 24, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Place(_enemyName.rectTransform, 0, 0, 1, 1, 14, 0, 14, 6);
            _enemyAura = Ui.Label(_enemyPanel, "", 22, Theme.Text, TextAnchor.UpperRight, FontStyle.Bold);
            Ui.Place(_enemyAura.rectTransform, 0, 0, 1, 1, 14, 0, 14, 6);
            var hpBack = Ui.Bar(_enemyPanel, "Hp", new Color(0.15f, 0.05f, 0.05f, 0.9f), new Color(0.88f, 0.25f, 0.3f), out _enemyHp);
            Ui.Place(hpBack.rectTransform, 0, 0, 1, 0, 14, 34, 14, -56);
            var brBack = Ui.Bar(_enemyPanel, "Break", new Color(0.05f, 0.1f, 0.15f, 0.9f), new Color(0.5f, 0.85f, 1f), out _enemyBreak);
            Ui.Place(brBack.rectTransform, 0, 0, 1, 0, 14, 12, 14, -26);
            _enemyState = Ui.Label(_enemyPanel, "", 22, new Color(0.6f, 0.95f, 1f), TextAnchor.LowerRight, FontStyle.Bold);
            Ui.Place(_enemyState.rectTransform, 0, 0, 1, 1, 14, 30, 14, 0);

            // 우상단: 자동/일시정지
            var pause = Ui.Button(root, "II", () => PauseRequested?.Invoke(), new Color(0f, 0f, 0f, 0.5f), 34);
            Ui.Box((RectTransform)pause.transform, 1f, 1f, -20f, -20f, 96f, 96f);
            var autoRt = Ui.Rect(root, "Auto");
            Ui.Box(autoRt, 1f, 1f, -130f, -30f, 210f, 76f);
            var autoBg = Ui.Panel(autoRt, "Bg", auto ? Theme.WithAlpha(Theme.Teal, 0.85f) : new Color(0f, 0f, 0f, 0.35f), false);
            _autoBadge = Ui.Label(autoRt, auto ? "AUTO ON" : "AUTO OFF", 26, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            _manualNote = Ui.Label(root, "", 22, Theme.Gold, TextAnchor.UpperRight);
            Ui.Box(_manualNote.rectTransform, 1f, 1f, -20f, -112f, 600f, 40f);
            autoBg.raycastTarget = false;

            // 좌측: 파티 4인
            for (int i = 0; i < 4; i++)
            {
                var s = new PartySlot();
                s.rt = Ui.Rect(root, "Slot" + i);
                Ui.Box(s.rt, 0f, 1f, 20f, -20f - i * 104f, 300f, 94f);
                s.frame = Ui.Panel(s.rt, "Frame", Theme.Gold, true);
                s.bg = Ui.Panel(s.rt, "Bg", new Color(0.08f, 0.1f, 0.16f, 0.88f), false);
                Ui.Place(s.bg.rectTransform, 0, 0, 1, 1, 3, 3, 3, 3);
                s.strip = Ui.Panel(s.rt, "Strip", Color.white, false);
                Ui.Place(s.strip.rectTransform, 0, 0, 0, 1, 3, 3, -12, 3);
                s.key = Ui.Label(s.rt, (i + 1).ToString(), 26, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                Ui.Place(s.key.rectTransform, 0, 0, 1, 1, 22, 0, 0, 0);
                s.name = Ui.Label(s.rt, "", 24, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
                Ui.Place(s.name.rectTransform, 0, 0, 1, 1, 52, 0, 8, 8);
                s.info = Ui.Label(s.rt, "", 19, Theme.Dim, TextAnchor.MiddleLeft);
                Ui.Place(s.info.rectTransform, 0, 0, 1, 1, 52, 22, 8, 30);
                var hb = Ui.Bar(s.rt, "Hp", new Color(0.1f, 0.12f, 0.14f), Theme.Ok, out s.hp);
                Ui.Place(hb.rectTransform, 0, 0, 1, 0, 52, 12, 12, -24);
                int idx = i;
                s.frame.gameObject.AddComponent<TapArea>().Tapped += () => input.RequestTag(idx);
                s.rt.gameObject.SetActive(i < partySize);
                _slots[i] = s;
            }

            // 하단 중앙: 활성 HP + 공명
            var bottom = Ui.Rect(root, "Bottom");
            Ui.Box(bottom, 0.5f, 0f, 0f, 24f, 640f, 100f);
            var hpb = Ui.Bar(bottom, "Hp", new Color(0.08f, 0.1f, 0.12f, 0.9f), Theme.Ok, out _hpFill);
            Ui.Place(hpb.rectTransform, 0, 1, 1, 1, 0, -34, 0, 0);
            var sh = Ui.Panel(hpb.transform, "Shield", new Color(0.7f, 0.95f, 1f, 0.7f), false);
            _shieldFill = sh;
            Ui.SetFill(_shieldFill, 0f);
            _hpText = Ui.Label(hpb.transform, "", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            var rb = Ui.Bar(bottom, "Res", new Color(0.08f, 0.1f, 0.12f, 0.9f), Theme.Gold, out _resFill);
            Ui.Place(rb.rectTransform, 0, 0, 1, 0, 0, 22, 0, -50);
            _resText = Ui.Label(bottom, "공명 0/100", 20, Theme.Gold, TextAnchor.LowerCenter, FontStyle.Bold);
            Ui.Place(_resText.rectTransform, 0, 0, 1, 0, 0, 0, 0, -22);

            // 좌하단 조이스틱
            var joyRt = Ui.Rect(root, "Joystick");
            Ui.Box(joyRt, 0f, 0f, 90f, 70f, 300f, 300f);
            var joyBg = joyRt.gameObject.AddComponent<Image>();
            joyBg.color = new Color(1f, 1f, 1f, 0.08f);
            var knob = Ui.Rect(joyRt, "Knob");
            Ui.Box(knob, 0.5f, 0.5f, 0f, 0f, 120f, 120f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.3f);
            knob.GetComponent<Image>().raycastTarget = false;
            var joy = joyRt.gameObject.AddComponent<VirtualJoystick>();
            joy.knob = knob;
            joy.radius = 110f;

            // 우하단 액션 버튼
            _attack = ActionButton.Create(root, "Attack", "공격", "J", 220f, Theme.TealDark);
            Ui.Box((RectTransform)_attack.transform, 1f, 0f, -80f, 70f, 220f, 220f);
            _dodge = ActionButton.Create(root, "Dodge", "회피", "Space", 140f, new Color(0.25f, 0.3f, 0.42f));
            Ui.Box((RectTransform)_dodge.transform, 1f, 0f, -330f, 50f, 140f, 140f);
            _skill = ActionButton.Create(root, "Skill", "스킬", "K", 150f, new Color(0.2f, 0.45f, 0.6f));
            Ui.Box((RectTransform)_skill.transform, 1f, 0f, -330f, 230f, 150f, 150f);
            _ult = ActionButton.Create(root, "Ultimate", "궁극기", "L", 160f, new Color(0.62f, 0.48f, 0.2f));
            Ui.Box((RectTransform)_ult.transform, 1f, 0f, -110f, 320f, 160f, 160f);
            input.Bind(joy, _attack, _skill, _ult, _dodge);

            // 일시정지 패널
            _pausePanel = Ui.Rect(root, "Pause");
            Ui.Place(_pausePanel, 0, 0, 1, 1);
            Ui.Panel(_pausePanel, "Dim", new Color(0f, 0f, 0f, 0.6f), true);
            var box = Ui.Rect(_pausePanel, "Box");
            Ui.Box(box, 0.5f, 0.5f, 0f, 0f, 640f, devTools ? 520f : 420f);
            box.pivot = new Vector2(0.5f, 0.5f);
            Ui.Panel(box, "Bg", Theme.Panel, true);
            var title = Ui.Label(box, "일시정지", 40, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Place(title.rectTransform, 0, 0, 1, 1, 0, 0, 0, 24);
            var resume = Ui.Button(box, "계속하기", () => ResumeRequested?.Invoke(), Theme.TealDark, 30);
            Ui.Box((RectTransform)resume.transform, 0.5f, 1f, 0f, -110f, 420f, 90f);
            var retreat = Ui.Button(box, "전투 포기 (보상 없음)", () => RetreatRequested?.Invoke(), new Color(0.45f, 0.2f, 0.22f), 28);
            Ui.Box((RectTransform)retreat.transform, 0.5f, 1f, 0f, -220f, 420f, 90f);
            var help = Ui.Label(box, "이동 WASD · 공격 J · 스킬 K · 궁극기 L · 회피 Space · 태그 1~4", 20, Theme.Dim, TextAnchor.LowerCenter);
            Ui.Place(help.rectTransform, 0, 0, 1, 1, 12, devTools ? 120 : 20, 12, 0);
            if (devTools)
            {
                var dev = Ui.Button(box, "[개발] 공명 100 채우기", () => DebugFillResonance?.Invoke(), new Color(0.35f, 0.3f, 0.15f), 24);
                Ui.Box((RectTransform)dev.transform, 0.5f, 0f, 0f, 30f, 420f, 70f);
            }
            _pausePanel.gameObject.SetActive(false);
        }

        public void SetPaused(bool paused) => _pausePanel.gameObject.SetActive(paused);

        public void Hurt() => _hurt = 0.35f;

        public void Refresh(BattleSim sim, bool autoOn, bool manualOverride, float dt)
        {
            var P = sim.Prototype;
            float rem = sim.TimeRemaining;
            _timer.text = $"{(int)(rem / 60f)}:{(int)(rem % 60f):00}";
            _timer.color = rem < 30f ? Theme.Danger : Theme.Text;
            _wave.text = $"WAVE {Mathf.Max(1, sim.WaveIndex + 1)}/{sim.WaveCount}";
            _manualNote.text = autoOn && manualOverride ? "수동 입력 우선 중 — 잠시 후 자동 재개" : "";

            // 적 포커스: 락온 대상 > 보스 > 가장 가까운 적
            EnemyState focus = sim.Body.lockTarget >= 0 ? sim.FindEnemy(sim.Body.lockTarget) : null;
            if (focus == null || !focus.Alive)
            {
                focus = null;
                foreach (var e in sim.Enemies)
                {
                    if (e.Alive && e.def.isBoss) focus = e;
                }
                if (focus == null)
                {
                    int n = sim.FindNearestEnemy(sim.Body.pos, 99f);
                    focus = n >= 0 ? sim.FindEnemy(n) : null;
                }
            }
            _enemyPanel.gameObject.SetActive(focus != null);
            if (focus != null)
            {
                _enemyName.text = $"{focus.def.displayName}  Lv.{focus.def.level}{(focus.def.isTemp ? "  <size=18><color=#8A93A8>(검증용)</color></size>" : "")}";
                Ui.SetFill(_enemyHp, focus.hp / focus.maxHp);
                Ui.SetFill(_enemyBreak, focus.breakGauge / focus.breakMax);
                _enemyAura.text = focus.aura != Element.None ? Ui.ColorTag($"{ElementInfo.StatusKo(focus.aura)}({ElementInfo.Ko(focus.aura)}) {focus.auraTimer:0.0}s", Theme.Element(focus.aura)) : "";
                _enemyState.text = focus.Staggered ? $"BREAK {focus.staggerTimer:0.0}s  피해×{P.breakDamageMultiplier}" : "";
            }

            for (int i = 0; i < sim.Party.Length && i < 4; i++)
            {
                var m = sim.Party[i];
                var s = _slots[i];
                bool active = i == sim.ActiveIndex;
                s.frame.color = active ? Theme.Gold : !m.Alive ? Theme.Locked : new Color(0.3f, 0.36f, 0.5f);
                s.strip.color = Theme.Element(m.def.element);
                s.name.text = m.def.displayName + (m.Alive ? "" : "  <color=#E05A5A>KO</color>");
                s.info.text = $"{WeaponInfo.Ko(m.def.weapon)} · {RoleInfo.Ko(m.def.role)} · 스킬 {(m.skillCd > 0f ? m.skillCd.ToString("0.0") + "s" : "준비")}";
                Ui.SetFill(s.hp, m.hp / m.maxHp);
                s.hp.color = m.hp / m.maxHp < 0.3f ? Theme.Danger : Theme.Ok;
                s.rt.localScale = Vector3.one * (active ? 1.04f : 1f);
            }

            var a = sim.Active;
            Ui.SetFill(_hpFill, a.hp / a.maxHp);
            Ui.SetFill(_shieldFill, Mathf.Clamp01(sim.Shield / a.maxHp));
            _hpText.text = $"{a.def.displayName}  {a.hp:0}/{a.maxHp:0}" + (sim.Shield > 0f ? $"  보호막 {sim.Shield:0}" : "") + (sim.BuffAtkPct > 0f ? $"  공격+{sim.BuffAtkPct * 100f:0}%" : "");
            Ui.SetFill(_resFill, sim.Resonance / sim.ResonanceMax);
            _resText.text = $"공명 {sim.Resonance:0}/{sim.ResonanceMax:0}";

            _skill.SetCooldown(a.skillCd, P.skillCooldown, a.skillCd <= 0f, new Color(0.2f, 0.45f, 0.6f));
            _dodge.SetCooldown(sim.DodgeCd, P.dodgeCooldown, sim.DodgeCd <= 0f, new Color(0.25f, 0.3f, 0.42f));
            float need = Mathf.Max(0f, sim.UltimateCost - sim.Resonance);
            _ult.SetCooldown(need, sim.UltimateCost, need <= 0f, need <= 0f ? Color.Lerp(Theme.Gold, Color.white, Mathf.PingPong(Time.time * 2f, 0.4f)) : new Color(0.62f, 0.48f, 0.2f));
            _ult.timer.text = need > 0f ? $"{sim.Resonance:0}" : "";
            _attack.SetCooldown(0f, 1f, true, Theme.TealDark);

            _hurt = Mathf.MoveTowards(_hurt, 0f, dt);
            _hurtFlash.color = new Color(1f, 0.1f, 0.1f, _hurt * 0.5f);
            UpdateFloats(dt);
        }

        // ───── 떠오르는 텍스트(피해 숫자/반응/브레이크) ─────

        public void Float(Vector3 world, string text, Color color, int size, float life = 0.8f)
        {
            Text t = _pool.Count > 0 ? _pool.Dequeue() : Ui.Label(_floatLayer, "", size, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.gameObject.SetActive(true);
            t.text = text;
            t.color = color;
            t.fontSize = size;
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(500f, 80f);
            if (t.GetComponent<Outline>() == null) Ui.Outline(t, new Color(0f, 0f, 0f, 0.85f), 2f);
            _floats.Add(new FloatText { text = t, world = world, life = life, drift = new Vector2(UnityEngine.Random.Range(-30f, 30f), 90f) });
            if (_floats.Count > 40)
            {
                Recycle(_floats[0]);
                _floats.RemoveAt(0);
            }
        }

        private void Recycle(FloatText f)
        {
            f.text.gameObject.SetActive(false);
            _pool.Enqueue(f.text);
        }

        private void UpdateFloats(float dt)
        {
            var cam = Camera.main;
            for (int i = _floats.Count - 1; i >= 0; i--)
            {
                var f = _floats[i];
                f.age += dt;
                if (f.age >= f.life || cam == null)
                {
                    Recycle(f);
                    _floats.RemoveAt(i);
                    continue;
                }
                Vector3 sp = cam.WorldToScreenPoint(f.world);
                if (sp.z < 0f)
                {
                    f.text.gameObject.SetActive(false);
                    continue;
                }
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_floatLayer, sp, null, out var local);
                float u = f.age / f.life;
                f.text.rectTransform.anchoredPosition = local + f.drift * u;
                var c = f.text.color;
                c.a = u < 0.7f ? 1f : 1f - (u - 0.7f) / 0.3f;
                f.text.color = c;
                f.text.rectTransform.localScale = Vector3.one * (u < 0.1f ? Mathf.Lerp(1.5f, 1f, u / 0.1f) : 1f);
            }
        }
    }
}
