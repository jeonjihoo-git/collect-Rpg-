using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Core.Data;
using Lumina.Core.Progress;
using Lumina.Runtime.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace Lumina.Runtime.UI
{
    public abstract class ScreenBase
    {
        public RectTransform Root { get; private set; }
        protected GameApp App { get; private set; }

        protected void Create(GameApp app, Transform parent, string name)
        {
            App = app;
            Root = Ui.Rect(parent, name);
            Ui.Place(Root, 0, 0, 1, 1);
            Ui.Panel(Root, "Bg", Theme.Ink, true);
        }

        public virtual void Show()
        {
            Root.gameObject.SetActive(true);
            Root.SetAsLastSibling();
            Refresh();
        }

        public void Hide() => Root.gameObject.SetActive(false);
        public abstract void Refresh();

        protected RectTransform Safe(string name = "Safe")
        {
            var s = Ui.Rect(Root, name);
            Ui.Place(s, 0, 0, 1, 1);
            s.gameObject.AddComponent<SafeAreaFitter>();
            return s;
        }

        protected static Text Header(RectTransform parent, string title, Action back)
        {
            var bar = Ui.Rect(parent, "Header");
            Ui.Place(bar, 0, 1, 1, 1, 0, -110, 0, 0);
            Ui.Panel(bar, "Bg", Theme.Panel, false);
            if (back != null)
            {
                var b = Ui.Button(bar, "◀ 뒤로", back, Theme.PanelLight, 28);
                Ui.Box((RectTransform)b.transform, 0f, 0.5f, 20f, 0f, 180f, 76f);
                ((RectTransform)b.transform).pivot = new Vector2(0f, 0.5f);
            }
            var t = Ui.Label(bar, title, 40, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            Ui.Place(t.rectTransform, 0, 0, 1, 1, back != null ? 230 : 40, 0, 40, 0);
            return t;
        }
    }

    /// <summary>화면 하단 잠깐 표시되는 안내(재화 부족/미구현 등).</summary>
    public sealed class Toast
    {
        private readonly Text _text;
        private readonly Image _bg;
        private float _t;

        public Toast(Transform parent)
        {
            var rt = Ui.Rect(parent, "Toast");
            Ui.Box(rt, 0.5f, 0f, 0f, 150f, 1100f, 80f);
            _bg = rt.gameObject.AddComponent<Image>();
            _bg.color = new Color(0f, 0f, 0f, 0.8f);
            _bg.raycastTarget = false;
            _text = Ui.Label(rt, "", 28, Theme.Text, TextAnchor.MiddleCenter);
            rt.gameObject.SetActive(false);
        }

        public void Show(string msg, float seconds = 2.2f)
        {
            _text.text = msg;
            _t = seconds;
            _bg.transform.SetAsLastSibling();
            _bg.gameObject.SetActive(true);
        }

        public void Update(float dt)
        {
            if (_t <= 0f) return;
            _t -= dt;
            if (_t <= 0f) _bg.gameObject.SetActive(false);
        }
    }

    /// <summary>확인/취소 모달. 취소(닫기)는 아무것도 소비하지 않는다.</summary>
    public sealed class ConfirmDialog
    {
        private readonly RectTransform _root;
        private readonly Text _title, _body;
        private Action _onOk;

        public ConfirmDialog(Transform parent)
        {
            _root = Ui.Rect(parent, "Confirm");
            Ui.Place(_root, 0, 0, 1, 1);
            Ui.Panel(_root, "Dim", new Color(0f, 0f, 0f, 0.65f), true);
            var box = Ui.Rect(_root, "Box");
            Ui.Box(box, 0.5f, 0.5f, 0f, 0f, 820f, 420f);
            box.pivot = new Vector2(0.5f, 0.5f);
            Ui.Panel(box, "Bg", Theme.Panel, true);
            _title = Ui.Label(box, "", 36, Theme.Text, TextAnchor.UpperCenter, FontStyle.Bold);
            Ui.Place(_title.rectTransform, 0, 0, 1, 1, 30, 0, 30, 30);
            _body = Ui.Label(box, "", 26, Theme.Dim, TextAnchor.MiddleCenter);
            Ui.Place(_body.rectTransform, 0, 0, 1, 1, 40, 120, 40, 90);
            var ok = Ui.Button(box, "확인", () =>
            {
                Hide();
                _onOk?.Invoke();
            }, new Color(0.55f, 0.22f, 0.24f), 30);
            Ui.Box((RectTransform)ok.transform, 0.5f, 0f, 150f, 30f, 260f, 84f);
            var cancel = Ui.Button(box, "취소", Hide, Theme.PanelLight, 30);
            Ui.Box((RectTransform)cancel.transform, 0.5f, 0f, -150f, 30f, 260f, 84f);
            _root.gameObject.SetActive(false);
        }

        public void Show(string title, string body, Action onOk)
        {
            _title.text = title;
            _body.text = body;
            _onOk = onOk;
            _root.SetAsLastSibling();
            _root.gameObject.SetActive(true);
        }

        public void Hide() => _root.gameObject.SetActive(false);
    }

    // ───────────────────────────── SCREEN 01 로비 ─────────────────────────────
    public sealed class LobbyScreen : ScreenBase
    {
        private Text _account, _currency, _progress, _party, _notice;
        private Image _expFill;

        public LobbyScreen(GameApp app, Transform parent)
        {
            Create(app, parent, "Lobby");
            var safe = Safe();

            var top = Ui.Rect(safe, "Top");
            Ui.Place(top, 0, 1, 1, 1, 0, -110, 0, 0);
            Ui.Panel(top, "Bg", Theme.Panel, false);
            _account = Ui.Label(top, "", 30, Theme.Text, TextAnchor.MiddleLeft, FontStyle.Bold);
            Ui.Place(_account.rectTransform, 0, 0, 0.4f, 1, 30, 20, 0, 0);
            var expBack = Ui.Bar(top, "Exp", new Color(0.1f, 0.12f, 0.18f), Theme.Teal, out _expFill);
            Ui.Place(expBack.rectTransform, 0, 0, 0.3f, 0, 30, 14, 0, -28);
            _currency = Ui.Label(top, "", 28, Theme.Gold, TextAnchor.MiddleRight, FontStyle.Bold);
            Ui.Place(_currency.rectTransform, 0.4f, 0, 1, 1, 0, 0, 40, 0);

            var title = Ui.Label(safe, "LUMINA CHRONICLE", 76, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Box(title.rectTransform, 0.5f, 1f, 0f, -170f, 1200f, 110f);
            var sub = Ui.Label(safe, "수직 슬라이스 빌드 · 4인 파티 실시간 액션 · 캐릭터는 전원 <color=#C9A55C>검증용 임시 캐릭터</color>", 26, Theme.Dim, TextAnchor.MiddleCenter);
            Ui.Box(sub.rectTransform, 0.5f, 1f, 0f, -270f, 1400f, 50f);

            // 좌: 모험
            var left = Ui.Rect(safe, "Adventure");
            Ui.Box(left, 0f, 0.5f, 60f, 20f, 620f, 420f);
            left.pivot = new Vector2(0f, 0.5f);
            Ui.Panel(left, "Bg", Theme.Panel, false);
            var lt = Ui.Label(left, "모험", 34, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Place(lt.rectTransform, 0, 0, 1, 1, 30, 0, 30, 24);
            _progress = Ui.Label(left, "", 26, Theme.Dim, TextAnchor.UpperLeft);
            Ui.Place(_progress.rectTransform, 0, 0, 1, 1, 30, 0, 30, 80);
            var go = Ui.Button(left, "모험 계속하기", () => App.ShowStageSelect(), Theme.Teal, 38);
            Ui.Box((RectTransform)go.transform, 0.5f, 0f, 0f, 30f, 560f, 120f);

            // 우: 파티
            var right = Ui.Rect(safe, "Party");
            Ui.Box(right, 1f, 0.5f, -60f, 20f, 620f, 420f);
            right.pivot = new Vector2(1f, 0.5f);
            Ui.Panel(right, "Bg", Theme.Panel, false);
            var rt = Ui.Label(right, "현재 파티", 34, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Place(rt.rectTransform, 0, 0, 1, 1, 30, 0, 30, 24);
            _party = Ui.Label(right, "", 26, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_party.rectTransform, 0, 0, 1, 1, 30, 0, 30, 80);
            var edit = Ui.Button(right, "파티 편성", () => App.ShowParty(null), Theme.PanelLight, 32);
            Ui.Box((RectTransform)edit.transform, 0.5f, 0f, 0f, 30f, 560f, 100f);

            _notice = Ui.Label(safe, "", 24, Theme.Gold, TextAnchor.MiddleCenter);
            Ui.Box(_notice.rectTransform, 0.5f, 0f, 0f, 250f, 1400f, 50f);

            // 하단 7메뉴(구현 여부 표시)
            var nav = Ui.Rect(safe, "Nav");
            Ui.Place(nav, 0, 0, 1, 0, 0, 0, 0, -120);
            Ui.Panel(nav, "Bg", Theme.Panel, false);
            var items = new (string label, Action act)[]
            {
                ("로비", null),
                ("캐릭터", () => App.Toast("캐릭터 목록/상세(SCREEN 02·03)는 미구현 — 다음 마일스톤")),
                ("편성", () => App.ShowParty(null)),
                ("모험", () => App.ShowStageSelect()),
                ("소환", () => App.Toast("소환(SCREEN 11·12)은 미구현 — 확률표 승인 후 착수")),
                ("도감", () => App.Toast("도감(SCREEN 15·16)은 미구현")),
                ("가방", () => App.Toast("가방(SCREEN 17)은 미구현"))
            };
            for (int i = 0; i < items.Length; i++)
            {
                var it = items[i];
                bool implemented = it.label == "로비" || it.label == "편성" || it.label == "모험";
                var b = Ui.Button(nav, it.label + (implemented ? "" : "\n<size=18>미구현</size>"), it.act ?? (() => { }), it.label == "로비" ? Theme.TealDark : implemented ? Theme.PanelLight : new Color(0.16f, 0.19f, 0.27f), 28, implemented ? Theme.Text : Theme.Dim);
                Ui.Place((RectTransform)b.transform, i / 7f, 0, (i + 1) / 7f, 1, 6, 10, 6, 10);
            }

            if (App.DevTools)
            {
                var dev = Ui.Rect(safe, "Dev");
                Ui.Box(dev, 0.5f, 0f, 0f, 135f, 1500f, 90f);
                Ui.Panel(dev, "Bg", new Color(0.25f, 0.2f, 0.08f, 0.6f), false);
                var dl = Ui.Label(dev, "개발/테스트 전용", 22, Theme.Gold, TextAnchor.MiddleLeft, FontStyle.Bold);
                Ui.Place(dl.rectTransform, 0, 0, 1, 1, 20, 0, 0, 0);
                var acts = new (string, Action)[]
                {
                    ("행동력 +60", () => Grant(DevGrantKind.Stamina, 60)),
                    ("골드 +10000", () => Grant(DevGrantKind.Gold, 10000)),
                    ("별빛 결정 +1600", () => Grant(DevGrantKind.Crystals, 1600)),
                    ("저장 초기화", () => App.Confirm("저장 초기화", "성장·클리어 기록·재화를 초기값으로 되돌립니다.\n되돌릴 수 없습니다.", () =>
                    {
                        App.Session.ResetSave();
                        App.Toast("저장 데이터를 초기화했습니다.");
                        Refresh();
                    }))
                };
                for (int i = 0; i < acts.Length; i++)
                {
                    var b = Ui.Button(dev, acts[i].Item1, acts[i].Item2, new Color(0.4f, 0.33f, 0.14f), 24);
                    Ui.Box((RectTransform)b.transform, 0f, 0.5f, 260f + i * 300f, 0f, 280f, 66f);
                    ((RectTransform)b.transform).pivot = new Vector2(0f, 0.5f);
                }
            }
        }

        private void Grant(DevGrantKind k, int n)
        {
            if (App.Session.DevGrant(k, n)) Refresh();
        }

        public override void Refresh()
        {
            var s = App.Session.Save;
            int per = App.Db.Balance.prototype.accountExpPerLevel;
            _account.text = $"{s.account.name}   개척 Lv.{s.account.level}   <size=22><color=#8A93A8>EXP {s.account.exp}/{per}</color></size>";
            Ui.SetFill(_expFill, s.account.exp / (float)per);
            _currency.text = $"행동력 {s.inventory.stamina}/{App.Db.Balance.temp.staminaMax}    골드 {s.inventory.gold:N0}    별빛 결정 {s.inventory.crystals:N0}    경험의 서 {s.inventory.expBooks}";
            int cleared = App.Db.Stages.Count(st => App.Session.GetClearRecord(st.id).isCleared);
            _progress.text = $"{App.Db.MapName}\n클리어 {cleared}/{App.Db.Stages.Count}\n\n<size=22>스토리 1~90장 원고는 미확보(C-07) — 검증 스테이지로 대체</size>";
            var sb = new System.Text.StringBuilder();
            foreach (var id in s.party.Current)
            {
                var c = App.Db.GetCharacter(id);
                if (c == null) continue;
                sb.AppendLine($"{Ui.ColorTag("■", Theme.Element(c.element))} {c.displayName}  <size=22><color=#8A93A8>{ElementInfo.Ko(c.element)} · {WeaponInfo.Ko(c.weapon)} · {RoleInfo.Ko(c.role)}</color></size>");
            }
            _party.text = sb.ToString();
            _notice.text = App.Session.RecoveryNotice ?? "";
        }
    }

    // ───────────────────────────── SCREEN 09 스테이지 선택 ─────────────────────────────
    public sealed class StageSelectScreen : ScreenBase
    {
        private readonly List<Button> _nodes = new List<Button>();
        private Text _detail, _autoInfo;
        private Button _ready;
        private string _selected;

        public StageSelectScreen(GameApp app, Transform parent)
        {
            Create(app, parent, "StageSelect");
            var safe = Safe();
            Header(safe, "스테이지 선택 · " + app.Db.MapName, () => App.ShowLobby());

            var map = Ui.Rect(safe, "Map");
            Ui.Place(map, 0, 0, 0.5f, 1, 40, 40, 20, 140);
            Ui.Panel(map, "Bg", Theme.Panel, false);
            var stages = app.Db.Stages;
            for (int i = 0; i < stages.Count; i++)
            {
                var st = stages[i];
                var b = Ui.Button(map, "", () => Select(st.id), Theme.PanelLight, 28);
                Ui.Box((RectTransform)b.transform, 0.5f, 1f, (i - 1) * 260f, -120f - i * 200f, 360f, 150f);
                ((RectTransform)b.transform).pivot = new Vector2(0.5f, 1f);
                _nodes.Add(b);
            }

            var panel = Ui.Rect(safe, "Detail");
            Ui.Place(panel, 0.5f, 0, 1, 1, 20, 40, 40, 140);
            Ui.Panel(panel, "Bg", Theme.Panel, false);
            _detail = Ui.Label(panel, "", 26, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_detail.rectTransform, 0, 0, 1, 1, 36, 230, 36, 30);
            _autoInfo = Ui.Label(panel, "", 24, Theme.Dim, TextAnchor.LowerLeft);
            Ui.Place(_autoInfo.rectTransform, 0, 0, 1, 1, 36, 160, 36, 0);
            _ready = Ui.Button(panel, "전투 준비", () => App.ShowParty(_selected), Theme.Teal, 36);
            Ui.Box((RectTransform)_ready.transform, 0.5f, 0f, 0f, 30f, 600f, 110f);
        }

        public void Select(string id)
        {
            _selected = id;
            Refresh();
        }

        public override void Refresh()
        {
            var stages = App.Db.Stages;
            if (_selected == null || App.Db.GetStage(_selected) == null)
            {
                _selected = stages[0].id;
                foreach (var st in stages)
                {
                    if (App.Session.IsStageUnlocked(st.id)) _selected = st.id;
                }
            }
            for (int i = 0; i < stages.Count; i++)
            {
                var st = stages[i];
                var rec = App.Session.GetClearRecord(st.id);
                bool unlocked = App.Session.IsStageUnlocked(st.id);
                bool boss = st.waves.Any(w => w.spawns.Any(sp => App.Db.GetEnemy(sp.enemy)?.isBoss == true));
                var t = Ui.ButtonText(_nodes[i]);
                t.text = $"{st.order}{(boss ? "  <color=#E05A5A>BOSS</color>" : "")}\n<size=22>{(unlocked ? (rec.isCleared ? Ui.Stars(rec.bestStars) : "미클리어") : "잠김")}</size>";
                var img = (Image)_nodes[i].targetGraphic;
                img.color = st.id == _selected ? Theme.TealDark : unlocked ? Theme.PanelLight : Theme.Locked;
            }

            var s = App.Db.GetStage(_selected);
            var r = App.Session.GetClearRecord(s.id);
            bool open = App.Session.IsStageUnlocked(s.id);
            var enemies = s.waves.SelectMany(w => w.spawns).GroupBy(sp => sp.enemy).Select(g =>
            {
                var e = App.Db.GetEnemy(g.Key);
                return $"{e.displayName} Lv.{e.level} ×{g.Count()}{(e.isBoss ? " (보스·브레이크 " + App.Db.Balance.BreakGauge(e.breakTier) + ")" : "")}";
            });
            var fr = s.firstClearReward;
            var rr = s.repeatReward;
            _detail.text =
                $"<size=38><b>{s.displayName}</b></size>\n<color=#8A93A8>{s.description}</color>\n\n" +
                $"<b>등장 적</b> ({s.waves.Length}웨이브)\n  " + string.Join("\n  ", enemies) + "\n\n" +
                $"<b>3성 조건</b>  승리 / 전원 생존 / {App.Db.Balance.prototype.starTimeLimit:0}초 이내\n" +
                $"<b>최초 보상</b>  결정 {fr.crystals} · 골드 {fr.gold} · 경험의 서 {fr.expBooks} · 계정EXP {fr.accountExp}" + (r.firstClearRewardClaimed ? "  <color=#6BD49A>수령 완료</color>" : "") + "\n" +
                $"<b>반복 보상</b>  결정 {rr.crystals} · 골드 {rr.gold} · 경험의 서 {rr.expBooks} · 계정EXP {rr.accountExp}\n" +
                $"<b>입장 행동력</b>  {s.staminaCost}  (보유 {App.Session.Save.inventory.stamina})\n\n" +
                $"<b>클리어 기록</b>  {(r.isCleared ? $"클리어 · 최고 {Ui.Stars(r.bestStars)} · {r.clearCount}회" : "없음")}" +
                $"\n<size=20><color=#8A93A8>보상·적 구성·행동력은 검증용 Temp 값(승인 필요)</color></size>";
            bool autoOk = App.Session.CanUseAutoBattle(s.id, out string why);
            _autoInfo.text = autoOk ? "자동전투: <color=#6BD49A>사용 가능</color> (파티 편성 화면에서 ON/OFF)" : $"자동전투: <color=#E05A5A>잠금</color> — {why}";
            _ready.interactable = open;
            Ui.ButtonText(_ready).text = open ? "전투 준비" : App.Session.LockReason(s.id);
        }
    }

    // ───────────────────────────── SCREEN 10 파티 편성 ─────────────────────────────
    public sealed class PartySetupScreen : ScreenBase
    {
        private readonly Text _title;
        private readonly Button[] _presetBtns = new Button[3];
        private readonly Button[] _slots = new Button[4];
        private Text _analysis, _autoText, _autoRecord;
        private Button _autoToggle, _sortie;
        private RectTransform _picker;
        private readonly List<Button> _pickerCards = new List<Button>();
        private int _pickSlot = -1;
        private string _stageId;
        private bool _autoOn;

        public PartySetupScreen(GameApp app, Transform parent)
        {
            Create(app, parent, "Party");
            var safe = Safe();
            _title = Header(safe, "파티 편성", Back);

            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                _presetBtns[i] = Ui.Button(safe, $"파티 0{i + 1}", () =>
                {
                    App.Session.SelectPreset(idx);
                    Refresh();
                }, Theme.PanelLight, 26);
                Ui.Box((RectTransform)_presetBtns[i].transform, 1f, 1f, -40f - (2 - i) * 210f, -18f, 190f, 74f);
                ((RectTransform)_presetBtns[i].transform).pivot = new Vector2(1f, 1f);
            }

            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                _slots[i] = Ui.Button(safe, "", () => OpenPicker(idx), Theme.Panel, 26);
                Ui.Place((RectTransform)_slots[i].transform, 0.02f + i * 0.245f, 0.42f, 0.02f + i * 0.245f + 0.225f, 0.86f);
                var t = Ui.ButtonText(_slots[i]);
                t.alignment = TextAnchor.UpperLeft;
                Ui.Place(t.rectTransform, 0, 0, 1, 1, 24, 16, 16, 20);
                t.fontStyle = FontStyle.Normal;
            }

            var bottom = Ui.Rect(safe, "Bottom");
            Ui.Place(bottom, 0.02f, 0.03f, 0.98f, 0.4f);
            Ui.Panel(bottom, "Bg", Theme.Panel, false);
            _analysis = Ui.Label(bottom, "", 24, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_analysis.rectTransform, 0, 0, 0.5f, 1, 30, 20, 10, 20);

            var autoBox = Ui.Rect(bottom, "Auto");
            Ui.Place(autoBox, 0.5f, 0, 1, 1, 10, 20, 30, 20);
            _autoText = Ui.Label(autoBox, "", 24, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_autoText.rectTransform, 0, 0, 1, 1, 0, 0, 0, 0);
            _autoToggle = Ui.Button(autoBox, "자동전투 OFF", ToggleAuto, Theme.PanelLight, 28);
            Ui.Box((RectTransform)_autoToggle.transform, 0f, 1f, 0f, -60f, 320f, 80f);
            ((RectTransform)_autoToggle.transform).pivot = new Vector2(0f, 1f);
            _autoRecord = Ui.Label(autoBox, "", 22, Theme.Dim, TextAnchor.UpperLeft);
            Ui.Place(_autoRecord.rectTransform, 0, 0, 1, 1, 340, 0, 0, 60);
            _sortie = Ui.Button(autoBox, "출전", Sortie, Theme.Teal, 36);
            Ui.Box((RectTransform)_sortie.transform, 1f, 0f, 0f, 0f, 380f, 110f);
            ((RectTransform)_sortie.transform).pivot = new Vector2(1f, 0f);

            BuildPicker();
        }

        public void Open(string stageId)
        {
            _stageId = stageId;
            _autoOn = stageId != null && App.Session.GetAutoPreference(stageId);
            _pickSlot = -1;
            _picker.gameObject.SetActive(false);
        }

        private void Back()
        {
            if (_stageId != null) App.ShowStageSelect();
            else App.ShowLobby();
        }

        private void ToggleAuto()
        {
            if (_stageId == null) return;
            if (!App.Session.CanUseAutoBattle(_stageId, out string why))
            {
                App.Toast("자동전투 잠금: " + why);
                return;
            }
            _autoOn = !_autoOn;
            App.Session.SetAutoPreference(_stageId, _autoOn);
            Refresh();
        }

        private void Sortie()
        {
            if (_stageId == null) return;
            var r = App.Session.TryStartBattle(_stageId, _autoOn);
            if (!r.Ok)
            {
                App.Toast(r.message);
                Refresh();
                return;
            }
            App.StartBattle(r);
        }

        private void BuildPicker()
        {
            _picker = Ui.Rect(Root, "Picker");
            Ui.Place(_picker, 0, 0, 1, 1);
            Ui.Panel(_picker, "Dim", new Color(0f, 0f, 0f, 0.7f), true);
            var box = Ui.Rect(_picker, "Box");
            Ui.Place(box, 0.06f, 0.06f, 0.94f, 0.94f);
            Ui.Panel(box, "Bg", Theme.Panel, true);
            var t = Ui.Label(box, "동료 선택 — 이미 편성된 동료를 고르면 두 자리를 교환합니다", 30, Theme.Text, TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Place(t.rectTransform, 0, 0, 1, 1, 30, 0, 30, 24);
            var close = Ui.Button(box, "닫기", () => _picker.gameObject.SetActive(false), Theme.PanelLight, 26);
            Ui.Box((RectTransform)close.transform, 1f, 1f, -20f, -16f, 160f, 64f);
            ((RectTransform)close.transform).pivot = new Vector2(1f, 1f);
            var chars = App.Db.Characters;
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                int col = i % 4, row = i / 4;
                var b = Ui.Button(box, "", () => Pick(c.id), Theme.PanelLight, 24);
                Ui.Place((RectTransform)b.transform, 0.02f + col * 0.245f, 0.78f - (row + 1) * 0.19f, 0.02f + col * 0.245f + 0.23f, 0.78f - row * 0.19f - 0.015f);
                _pickerCards.Add(b);
            }
            _picker.gameObject.SetActive(false);
        }

        private void OpenPicker(int slot)
        {
            _pickSlot = slot;
            _picker.SetAsLastSibling();
            _picker.gameObject.SetActive(true);
            Refresh();
        }

        private void Pick(string id)
        {
            if (_pickSlot >= 0) App.Session.SetPartySlot(_pickSlot, id);
            _picker.gameObject.SetActive(false);
            _pickSlot = -1;
            Refresh();
        }

        public override void Refresh()
        {
            var db = App.Db;
            var stage = _stageId != null ? db.GetStage(_stageId) : null;
            _title.text = stage != null ? $"파티 편성 · {stage.displayName}" : "파티 편성";
            for (int i = 0; i < 3; i++) ((Image)_presetBtns[i].targetGraphic).color = i == App.Session.Save.party.current ? Theme.TealDark : Theme.PanelLight;

            var party = App.Session.Save.party.Current;
            var defs = party.Select(db.GetCharacter).ToList();
            for (int i = 0; i < 4; i++)
            {
                var c = defs[i];
                var o = App.Session.GetOwned(c.id);
                var col = Theme.Element(c.element);
                Ui.ButtonText(_slots[i]).text =
                    $"<size=22><color=#8A93A8>슬롯 {i + 1}</color></size>\n<size=34><b>{c.displayName}</b></size>\n" +
                    $"{Ui.ColorTag("● " + ElementInfo.Ko(c.element), col)}  ·  {RoleInfo.Ko(c.role)}\n" +
                    $"{WeaponInfo.Ko(c.weapon)} · {AttackTypeInfo.Ko(c.attackType)}\nLv.{o.level}  ·  {c.rarity}\n\n<size=20><color=#C9A55C>검증용 임시 캐릭터</color></size>";
                ((Image)_slots[i].targetGraphic).color = Color.Lerp(Theme.Panel, col, 0.12f);
            }

            // 반응·역할 점검(권고만, 강제 아님)
            var els = defs.Select(d => d.element).Distinct().ToList();
            var reacts = new List<string>();
            for (int a = 0; a < els.Count; a++)
            {
                for (int b = a + 1; b < els.Count; b++)
                {
                    var r = db.FindReaction(els[a], els[b]);
                    if (r != null) reacts.Add($"{r.name}({ElementInfo.Ko(r.a)}+{ElementInfo.Ko(r.b)})");
                }
            }
            var roles = defs.Select(d => d.role).ToList();
            var missing = new[] { Role.MainDps, Role.SubDps, Role.Healer, Role.Buffer }.Where(r => !roles.Contains(r)).Select(RoleInfo.Ko).ToList();
            string err = App.Session.ValidateParty(party);
            _analysis.text =
                $"<b>가능한 속성 반응</b>\n  {(reacts.Count > 0 ? string.Join(", ", reacts) : "없음")}\n" +
                $"<size=20><color=#8A93A8>반응은 스킬/궁극기의 속성 부착으로 발동(일반공격 부착 없음 — 가정 A-07)</color></size>\n\n" +
                $"<b>역할 구성</b>  {(missing.Count == 0 ? "메인·서브·힐러·버퍼 모두 포함" : "누락: " + string.Join(", ", missing) + " (권장 사항 · 출전 가능)")}\n" +
                (err != null ? $"\n<color=#E05A5A>{err}</color>" : "");

            bool hasStage = stage != null;
            _autoToggle.gameObject.SetActive(hasStage);
            _sortie.gameObject.SetActive(hasStage);
            if (hasStage)
            {
                var rec = App.Session.GetClearRecord(stage.id);
                bool can = App.Session.CanUseAutoBattle(stage.id, out string why);
                if (!can) _autoOn = false;
                Ui.ButtonText(_autoToggle).text = can ? (_autoOn ? "자동전투 ON" : "자동전투 OFF") : "[잠금] 자동전투";
                ((Image)_autoToggle.targetGraphic).color = can ? (_autoOn ? Theme.Teal : Theme.PanelLight) : Theme.Locked;
                _autoRecord.text = $"ClearRecord({stage.id}): {(rec.isCleared ? $"클리어 완료 · 최고 {Ui.Stars(rec.bestStars)} · {rec.clearCount}회" : "기록 없음")}" +
                                   (can ? "" : $"\n<color=#E05A5A>{why}</color>");
                _autoText.text = "\n\n\n<size=21><color=#8A93A8>자동전투 ON 시 일반공격·스킬·회피·궁극기를 자동 사용합니다.\n수동 입력이 들어오면 1초간 수동이 우선합니다(가정 A-12). 태그는 수동.</color></size>";
                int stamina = App.Session.Save.inventory.stamina;
                Ui.ButtonText(_sortie).text = $"출전  <size=24>행동력 {stage.staminaCost} / 보유 {stamina}</size>";
                _sortie.interactable = err == null;
                ((Image)_sortie.targetGraphic).color = stamina >= stage.staminaCost ? Theme.Teal : Theme.Locked;
            }
            else
            {
                _autoRecord.text = "";
                _autoText.text = "<size=22><color=#8A93A8>스테이지를 선택한 뒤 이 화면에서 출전·자동전투를 설정합니다.</color></size>";
            }

            if (_picker.gameObject.activeSelf)
            {
                var chars = db.Characters;
                for (int i = 0; i < chars.Count; i++)
                {
                    var c = chars[i];
                    int at = Array.IndexOf(party, c.id);
                    var col = Theme.Element(c.element);
                    Ui.ButtonText(_pickerCards[i]).text = $"<b>{c.displayName}</b>{(at >= 0 ? $"  <color=#C9A55C>[편성 {at + 1}]</color>" : "")}\n<size=20>{Ui.ColorTag(ElementInfo.Ko(c.element), col)} · {WeaponInfo.Ko(c.weapon)} · {RoleInfo.Ko(c.role)}</size>";
                    ((Image)_pickerCards[i].targetGraphic).color = at == _pickSlot ? Theme.TealDark : Color.Lerp(Theme.PanelLight, col, 0.15f);
                }
            }
        }
    }

    // ───────────────────────────── SCREEN 14 전투 결과 ─────────────────────────────
    public sealed class ResultScreen : ScreenBase
    {
        private readonly Text _title, _stars, _stats, _reward;
        private readonly RectTransform _bars;
        private readonly Button _retry;
        private BattleSummary _s;

        public ResultScreen(GameApp app, Transform parent)
        {
            Create(app, parent, "Result");
            var safe = Safe();
            _title = Ui.Label(safe, "", 84, Theme.Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Box(_title.rectTransform, 0.5f, 1f, 0f, -30f, 1400f, 120f);
            _stars = Ui.Label(safe, "", 30, Theme.Text, TextAnchor.MiddleCenter);
            Ui.Box(_stars.rectTransform, 0.5f, 1f, 0f, -160f, 1600f, 110f);

            var left = Ui.Rect(safe, "Stats");
            Ui.Place(left, 0.03f, 0.18f, 0.5f, 0.74f);
            Ui.Panel(left, "Bg", Theme.Panel, false);
            _stats = Ui.Label(left, "", 26, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_stats.rectTransform, 0, 0.45f, 1, 1, 30, 0, 30, 20);
            _bars = Ui.Rect(left, "Bars");
            Ui.Place(_bars, 0, 0, 1, 0.45f, 30, 20, 30, 0);

            var right = Ui.Rect(safe, "Reward");
            Ui.Place(right, 0.52f, 0.18f, 0.97f, 0.74f);
            Ui.Panel(right, "Bg", Theme.Panel, false);
            _reward = Ui.Label(right, "", 28, Theme.Text, TextAnchor.UpperLeft);
            Ui.Place(_reward.rectTransform, 0, 0, 1, 1, 30, 20, 30, 20);

            var b1 = Ui.Button(safe, "스테이지 선택", () => App.ShowStageSelect(), Theme.PanelLight, 30);
            Ui.Place((RectTransform)b1.transform, 0.03f, 0.03f, 0.33f, 0.14f);
            var b2 = Ui.Button(safe, "파티 재편성", () => App.ShowParty(_s?.stage.id), Theme.PanelLight, 30);
            Ui.Place((RectTransform)b2.transform, 0.35f, 0.03f, 0.65f, 0.14f);
            _retry = Ui.Button(safe, "다시 도전", Retry, Theme.Teal, 30);
            Ui.Place((RectTransform)_retry.transform, 0.67f, 0.03f, 0.97f, 0.14f);
        }

        public void Set(BattleSummary s) => _s = s;

        private void Retry()
        {
            if (_s == null) return;
            // 재도전 = 행동력 재검사·재차감 후 새 battleId 로 새 전투
            bool auto = App.Session.GetAutoPreference(_s.stage.id);
            var r = App.Session.TryStartBattle(_s.stage.id, auto);
            if (!r.Ok)
            {
                App.Toast(r.message);
                return;
            }
            App.StartBattle(r);
        }

        public override void Refresh()
        {
            if (_s == null) return;
            var rc = _s.receipt;
            bool win = _s.outcome == Lumina.Core.Combat.BattleOutcome.Victory;
            _title.text = win ? "VICTORY" : "DEFEAT";
            _title.color = win ? Theme.Gold : Theme.Danger;
            var flags = rc != null ? rc.starFlags : new bool[3];
            string F(bool ok, string label) => (ok ? "<color=#C9A55C>★</color> " : "<color=#3A4258>☆</color> ") + label;
            _stars.text = $"{_s.stage.displayName}{(_s.autoBattle ? "  <color=#2BB5A6>[자동전투]</color>" : "")}\n" +
                          $"{F(flags[0], "승리")}     {F(flags[1], "전원 생존")}     {F(flags[2], $"{App.Db.Balance.prototype.starTimeLimit:0}초 이내")}";
            var st = _s.stats;
            string reacts = st.reactionCounts.Count > 0 ? string.Join(", ", st.reactionCounts.Select(kv => $"{kv.Key}×{kv.Value}")) : "없음";
            _stats.text = $"<b>전투 통계</b>\n소요 시간 {_s.time:0.0}초 · 총 피해 {st.totalDamage:N0}\n반응 {st.reactions}회 ({reacts})\n브레이크 {st.breaks}회 · 완벽 회피 {st.perfectDodges}회 · 피격 {st.hitsTaken}회";
            foreach (Transform c in _bars) UnityEngine.Object.Destroy(c.gameObject);
            float max = Mathf.Max(1f, st.memberDamage.Max());
            for (int i = 0; i < _s.memberNames.Length; i++)
            {
                var row = Ui.Rect(_bars, "Row" + i);
                Ui.Place(row, 0, 1f - (i + 1) * 0.25f, 1, 1f - i * 0.25f, 0, 4, 0, 4);
                var name = Ui.Label(row, _s.memberNames[i], 22, Theme.Text, TextAnchor.MiddleLeft);
                Ui.Place(name.rectTransform, 0, 0, 0.3f, 1);
                var bar = Ui.Bar(row, "Bar", new Color(0.1f, 0.12f, 0.18f), _s.memberColors[i], out var fill);
                Ui.Place(bar.rectTransform, 0.3f, 0.2f, 0.85f, 0.8f);
                Ui.SetFill(fill, st.memberDamage[i] / max);
                var val = Ui.Label(row, st.memberDamage[i].ToString("N0"), 22, Theme.Dim, TextAnchor.MiddleRight);
                Ui.Place(val.rectTransform, 0.85f, 0, 1, 1);
            }
            if (rc == null)
            {
                _reward.text = "<color=#E05A5A>전투 결과 확정 실패(저장 상태 확인 필요)</color>";
            }
            else if (!win)
            {
                _reward.text = "<b>보상</b>\n패배 시 보상이 지급되지 않습니다.\n\n<color=#8A93A8>파티 재편성 또는 다른 무기 조합을 시도해 보세요.\n(성장 화면은 다음 마일스톤)</color>";
            }
            else
            {
                var r = rc.reward;
                _reward.text = $"<b>{(rc.firstClear ? "최초 클리어 보상" : "반복 보상")}</b>  <color=#6BD49A>지급 완료</color>\n\n" +
                               $"별빛 결정 +{r.crystals}\n골드 +{r.gold:N0}\n경험의 서 +{r.expBooks}\n계정 EXP +{r.accountExp}" +
                               (rc.accountLevelAfter > rc.accountLevelBefore ? $"\n\n<color=#C9A55C>개척 레벨 업! Lv.{rc.accountLevelBefore} → Lv.{rc.accountLevelAfter}</color>" : "") +
                               $"\n\n<size=20><color=#8A93A8>battleId {rc.battleId} — 같은 전투는 다시 지급되지 않음</color></size>";
            }
            Ui.ButtonText(_retry).text = $"다시 도전 (행동력 {_s.stage.staminaCost})";
        }
    }
}
