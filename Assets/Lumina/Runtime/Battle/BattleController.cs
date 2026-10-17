using System.Collections.Generic;
using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Progress;
using Lumina.Runtime.Audio;
using Lumina.Runtime.UI;
using UnityEngine;

namespace Lumina.Runtime.Battle
{
    /// <summary>전투 종료 후 결과 화면에 넘길 요약.</summary>
    public sealed class BattleSummary
    {
        public BattleReceipt receipt;
        public StageDef stage;
        public BattleOutcome outcome;
        public float time;
        public BattleStats stats;
        public string[] memberNames;
        public Color[] memberColors;
        public bool autoBattle;
    }

    /// <summary>
    /// 전투 1회의 런타임 진행자. 고정 60틱 시뮬레이션을 돌리고, 틱마다 나온 이벤트만으로 연출/효과음/카메라를 재생한다.
    /// 히트스톱은 시뮬레이션 진행 자체를 잠시 멈추는 방식이라 모션·판정·소리가 서로 어긋나지 않는다.
    /// </summary>
    public sealed class BattleController : MonoBehaviour
    {
        private GameApp _app;
        private BattleSim _sim;
        private string _battleId;
        private bool _auto;
        private readonly AutoBattleController _ai = new AutoBattleController();
        private readonly InputMerger _merger = new InputMerger();
        private SfxRouter _router;
        private SfxPlayer _sfx;
        private BattleHud _hud;
        private CameraRig _cam;
        private VfxSystem _vfx;
        private Transform _world;
        private AvatarView[] _avatars;
        private readonly Dictionary<int, EnemyView> _enemies = new Dictionary<int, EnemyView>();
        private readonly Dictionary<int, GameObject> _entities = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, GameObject> _enemyProj = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, TelegraphView> _telegraphs = new Dictionary<int, TelegraphView>();
        private readonly List<int> _tmp = new List<int>();
        private float _acc;
        private float _hitstop;
        private bool _paused;
        private bool _ended;
        private float _endDelay;
        private int _shownActive = -1;
        private BattleSummary _summary;

        public static BattleController Begin(GameApp app, StartBattleResult start, Transform uiParent)
        {
            var go = new GameObject("Battle");
            var bc = go.AddComponent<BattleController>();
            bc.Init(app, start, uiParent);
            return bc;
        }

        private void Init(GameApp app, StartBattleResult start, Transform uiParent)
        {
            _app = app;
            _battleId = start.battleId;
            _auto = start.setup.autoBattle;
            _sim = new BattleSim(app.Db, start.setup);
            _router = new SfxRouter(app.Db);
            _sfx = app.Sfx;

            _world = new GameObject("World").transform;
            _world.SetParent(transform, false);
            ArenaBuilder.Build(_world, _sim.ArenaRadius);
            _vfx = new VfxSystem(_world);
            _cam = new CameraRig(app.MainCamera);
            _cam.Snap(V.W(_sim.Body.pos));

            _avatars = new AvatarView[_sim.Party.Length];
            var defs = new List<CharacterDef>();
            for (int i = 0; i < _sim.Party.Length; i++)
            {
                _avatars[i] = new AvatarView(_world, _sim.Party[i]);
                _avatars[i].SetVisible(false);
                defs.Add(_sim.Party[i].def);
            }
            _sfx.Prewarm(defs, app.Db);

            _hud = new BattleHud(uiParent, start.setup.stage.displayName, _auto, _sim.Party.Length, app.DevTools);
            _hud.PauseRequested += () => SetPaused(true);
            _hud.ResumeRequested += () => SetPaused(false);
            _hud.RetreatRequested += () =>
            {
                SetPaused(false);
                _sim.Retreat();
                HandleEvents(_sim.Events);
            };
            _hud.DebugFillResonance += () =>
            {
                if (_app.DevTools) _sim.DebugAddResonance(_sim.ResonanceMax);
            };
        }

        private void SetPaused(bool p)
        {
            if (_ended) p = false;
            _paused = p;
            _hud.SetPaused(p);
            if (!p) _hud.input.Clear();
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause && !_ended) SetPaused(true);
        }

        private void OnApplicationFocus(bool focus)
        {
            if (!focus && !_ended && !Application.isEditor) SetPaused(true);
        }

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _hud.input.Poll();
            if (_hud.input.PausePressed && !_ended) SetPaused(!_paused);

            if (!_paused && !_ended)
            {
                if (_hitstop > 0f)
                {
                    _hitstop -= dt;
                }
                else
                {
                    _acc += dt;
                    int steps = 0;
                    while (_acc >= BattleSim.Dt && steps < 5)
                    {
                        var manual = _hud.input.ConsumeTick();
                        var autoIn = _auto ? _ai.Decide(_sim) : PlayerInput.None;
                        var merged = _merger.Merge(manual, autoIn, _auto, BattleSim.Dt, _sim.Temp.autoManualOverride);
                        _sim.Step(merged, _merger.LastWasAuto);
                        HandleEvents(_sim.Events);
                        _acc -= BattleSim.Dt;
                        steps++;
                        if (_hitstop > 0f)
                        {
                            _acc = 0f;
                            break;
                        }
                        if (_ended) break;
                    }
                    if (steps >= 5) _acc = 0f;
                }
            }

            bool frozen = _paused || _hitstop > 0f;
            float alpha = frozen || _ended ? 1f : Mathf.Clamp01(_acc / BattleSim.Dt);
            UpdateViews(alpha, frozen ? 0f : dt);
            var active = _avatars[_sim.ActiveIndex];
            _cam.Update(active.root.transform.position, dt);
            _hud.Refresh(_sim, _auto, _auto && !_merger.LastWasAuto && _sim.Outcome == BattleOutcome.InProgress && !_paused, dt);

            if (_ended)
            {
                _endDelay -= dt;
                if (_endDelay <= 0f && _summary != null)
                {
                    var s = _summary;
                    _summary = null;
                    _app.OnBattleFinished(s);
                }
            }
        }

        // ───────────────────────── 이벤트 → 연출 ─────────────────────────

        private void HandleEvents(IReadOnlyList<CombatEvent> events)
        {
            if (events.Count == 0) return;
            _sfx.PlayAll(_router.RouteTick(events));
            foreach (var ev in events)
            {
                switch (ev.type)
                {
                    case CombatEventType.EnemySpawn:
                    {
                        var e = _sim.FindEnemy(ev.enemy);
                        if (e != null && !_enemies.ContainsKey(e.id))
                        {
                            _enemies[e.id] = new EnemyView(_world, e);
                            _vfx.Burst(V.W(e.pos), new Color(0.7f, 0.4f, 1f, 0.9f), 14, 0.35f);
                        }
                        break;
                    }
                    case CombatEventType.WaveStart:
                        _hud.Float(V.W(_sim.Body.pos, 3.2f), $"WAVE {ev.step + 1}/{(int)ev.amount}", Theme.Gold, 54, 1.4f);
                        break;
                    case CombatEventType.ActionStart:
                        if (ev.action == ActionKind.Ultimate)
                        {
                            _cam.Zoom(1f);
                            _cam.Shake(0.15f);
                            _hud.Float(Avatar.root.transform.position + Vector3.up * 2.8f, _sim.Party[ev.member].def.ultimate.name, Theme.Gold, 40, 1f);
                        }
                        break;
                    case CombatEventType.Dodge:
                        _vfx.Afterimage(Avatar.root.transform.position, Avatar.root.transform.rotation, new Color(0.6f, 0.9f, 1f, 0.45f));
                        break;
                    case CombatEventType.ProjectileLaunch:
                    {
                        var go = EntityViews.Projectile(_world, ev.weapon, ev.element);
                        go.transform.position = V.W(ev.pos, 1.1f);
                        go.transform.rotation = V.Facing(ev.dir);
                        _entities[ev.entity] = go;
                        _vfx.Flash(V.W(ev.pos, 1.1f), new Color(1f, 0.95f, 0.8f, 0.9f), ev.weapon == WeaponType.Launcher ? 1.4f : ev.weapon == WeaponType.Gun ? 0.8f : 0.5f, 0.07f);
                        break;
                    }
                    case CombatEventType.AreaSpawn:
                    {
                        var go = EntityViews.AreaMarker(_world, ev.weapon, ev.element, ev.radius);
                        go.transform.position = V.W(ev.pos);
                        _entities[ev.entity] = go;
                        break;
                    }
                    case CombatEventType.RingSpawn:
                    {
                        var go = EntityViews.RingWave(_world, ev.element);
                        go.transform.position = V.W(ev.pos, 0.6f);
                        go.transform.localScale = Vector3.one * 0.3f;
                        _entities[ev.entity] = go;
                        break;
                    }
                    case CombatEventType.ProjectileExpire:
                        DestroyEntity(ev.entity);
                        break;
                    case CombatEventType.AreaBurst:
                        DestroyEntity(ev.entity);
                        if (ev.weapon == WeaponType.Launcher || ev.weapon == WeaponType.Crossbow) _vfx.Explosion(V.W(ev.pos), ev.radius, new Color(1f, 0.6f, 0.25f));
                        else _vfx.GroundRing(V.W(ev.pos), ev.radius, Theme.WithAlpha(ev.element != Element.None ? Theme.Element(ev.element) : Color.white, 0.9f), 0.3f, 0.18f);
                        if (ev.amount <= 0f) _cam.Shake(0.08f);
                        break;
                    case CombatEventType.Hit:
                    {
                        Vector3 pos = V.W(ev.pos);
                        Vector3 dir = new Vector3(ev.dir.x, 0f, ev.dir.y);
                        if (_enemies.TryGetValue(ev.enemy, out var view)) view.Hit(dir, ev.tier);
                        _vfx.Impact(pos, dir, ev.weapon, ev.tier, ev.swing, ev.element, ev.crit);
                        _hitstop = Mathf.Min(0.18f, Mathf.Max(_hitstop, ev.hitstop));
                        _cam.Shake(ev.shake * 0.55f);
                        Color c = ev.crit ? Theme.Gold : ev.element != Element.None ? Theme.Element(ev.element) : Color.white;
                        _hud.Float(pos + Vector3.up * 2.2f, ev.amount.ToString("0") + (ev.crit ? "!" : ""), c, ev.crit ? 46 : ev.tier == SfxTier.Ultimate ? 42 : 32, 0.75f);
                        break;
                    }
                    case CombatEventType.Reaction:
                    {
                        var r = FindReaction(ev.label);
                        _vfx.Reaction(V.W(ev.pos), r != null ? r.a : ev.element, r != null ? r.b : ev.element);
                        _hud.Float(V.W(ev.pos, 3f), ev.label + (r != null && r.effect == ReactionEffect.Shield ? " · 보호막" : $" ×{ev.amount:0.0}"), Theme.Element(ev.element), 46, 1.1f);
                        _cam.Shake(0.25f);
                        break;
                    }
                    case CombatEventType.ShieldGain:
                        _hud.Float(Avatar.root.transform.position + Vector3.up * 2.6f, $"보호막 +{ev.amount:0}", new Color(0.7f, 0.95f, 1f), 34, 1f);
                        break;
                    case CombatEventType.BreakStart:
                        _vfx.BreakShatter(V.W(ev.pos));
                        _hud.Float(V.W(ev.pos, 3.4f), "BREAK!", new Color(0.6f, 0.95f, 1f), 62, 1.2f);
                        _hitstop = Mathf.Max(_hitstop, 0.12f);
                        _cam.Shake(0.45f);
                        break;
                    case CombatEventType.EnemyDeath:
                        if (_enemies.TryGetValue(ev.enemy, out var dv))
                        {
                            dv.Die();
                            _vfx.Burst(V.W(ev.pos), new Color(0.7f, 0.4f, 1f, 0.9f), 24, 0.35f);
                        }
                        RemoveTelegraph(ev.enemy);
                        break;
                    case CombatEventType.EnemyTelegraph:
                        RemoveTelegraph(ev.enemy);
                        _telegraphs[ev.enemy] = new TelegraphView(_world, ev);
                        break;
                    case CombatEventType.EnemyTelegraphCancel:
                        RemoveTelegraph(ev.enemy);
                        break;
                    case CombatEventType.EnemyAttack:
                        RemoveTelegraph(ev.enemy);
                        if (ev.enemyShape == EnemyAttackShape.Circle || ev.enemyShape == EnemyAttackShape.SelfRing || ev.enemyShape == EnemyAttackShape.TargetCircle)
                        {
                            _vfx.GroundRing(V.W(ev.pos), ev.radius, new Color(1f, 0.35f, 0.25f, 0.9f), 0.35f, 0.2f);
                            _cam.Shake(ev.radius > 4f ? 0.35f : 0.15f);
                        }
                        break;
                    case CombatEventType.EnemyProjectileLaunch:
                    {
                        var go = EntityViews.EnemyProjectile(_world);
                        go.transform.position = V.W(ev.pos, 1.2f);
                        _enemyProj[ev.entity] = go;
                        break;
                    }
                    case CombatEventType.EnemyProjectileExpire:
                        if (_enemyProj.TryGetValue(ev.entity, out var ep))
                        {
                            Destroy(ep);
                            _enemyProj.Remove(ev.entity);
                        }
                        break;
                    case CombatEventType.PlayerHit:
                        if (ev.member >= 0 && ev.member < _avatars.Length) _avatars[ev.member].Flash();
                        _hud.Hurt();
                        _hud.Float(V.W(ev.pos, 2.4f), "-" + ev.amount.ToString("0"), Theme.Danger, 36, 0.8f);
                        _cam.Shake(ev.shake);
                        break;
                    case CombatEventType.PerfectDodge:
                        _hud.Float(V.W(ev.pos, 2.8f), "PERFECT  공명 +18", new Color(0.6f, 0.95f, 1f), 46, 1f);
                        _vfx.GroundRing(V.W(ev.pos), 3f, new Color(0.6f, 0.95f, 1f, 0.9f), 0.4f, 0.15f);
                        break;
                    case CombatEventType.Tag:
                    case CombatEventType.AutoSwap:
                    {
                        var m = _sim.Party[ev.member];
                        _vfx.Burst(V.W(_sim.Body.pos), Theme.Element(m.def.element), 22, 0.3f);
                        _hud.Float(V.W(_sim.Body.pos, 2.8f), (ev.type == CombatEventType.AutoSwap ? "자동 교대: " : "") + m.def.displayName, Theme.Element(m.def.element), 34, 0.9f);
                        break;
                    }
                    case CombatEventType.MemberDown:
                        _hud.Float(V.W(ev.pos, 2.8f), "KO", Theme.Danger, 54, 1f);
                        break;
                    case CombatEventType.Heal:
                        _vfx.Heal(V.W(_sim.Body.pos));
                        _hud.Float(V.W(_sim.Body.pos, 2.6f), $"파티 회복 +{ev.amount:0}", Theme.Ok, 36, 1f);
                        break;
                    case CombatEventType.Buff:
                        _vfx.Burst(V.W(_sim.Body.pos), Theme.Gold, 18, 0.25f);
                        _hud.Float(V.W(_sim.Body.pos, 2.6f), $"파티 공격력 +{ev.amount * 100f:0}% ({ev.duration:0}초)", Theme.Gold, 34, 1f);
                        break;
                    case CombatEventType.ResonanceFull:
                        _hud.Float(V.W(_sim.Body.pos, 3f), "궁극기 준비 완료", Theme.Gold, 36, 1f);
                        break;
                    case CombatEventType.BattleEnd:
                        OnEnd();
                        break;
                }
            }
        }

        private AvatarView Avatar => _avatars[_sim.ActiveIndex];

        private ElementReactionDef FindReaction(string name)
        {
            foreach (var r in _app.Db.Reactions)
            {
                if (r.name == name) return r;
            }
            return null;
        }

        private void DestroyEntity(int id)
        {
            if (_entities.TryGetValue(id, out var go))
            {
                Destroy(go);
                _entities.Remove(id);
            }
        }

        private void RemoveTelegraph(int enemyId)
        {
            if (_telegraphs.TryGetValue(enemyId, out var t))
            {
                Destroy(t.root);
                _telegraphs.Remove(enemyId);
            }
        }

        private void OnEnd()
        {
            if (_ended) return;
            _ended = true;
            _endDelay = 1.8f;
            _hud.SetPaused(false);
            bool win = _sim.Outcome == BattleOutcome.Victory;
            _hud.Float(Avatar.root.transform.position + Vector3.up * 3.5f, win ? "VICTORY" : _sim.Outcome == BattleOutcome.DefeatTimeUp ? "TIME UP" : _sim.Outcome == BattleOutcome.Retreat ? "RETREAT" : "DEFEAT", win ? Theme.Gold : Theme.Danger, 84, 1.8f);
            // 보상 지급은 결과 화면 진입과 무관하게 전투 종료 시점에 1회 확정(battleId 멱등)
            var receipt = _app.Session.CompleteBattle(_battleId, _sim.Outcome, _sim.AllMembersAlive, _sim.Time);
            var names = new string[_sim.Party.Length];
            var colors = new Color[_sim.Party.Length];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = _sim.Party[i].def.displayName;
                colors[i] = Theme.Element(_sim.Party[i].def.element);
            }
            _summary = new BattleSummary
            {
                receipt = receipt, stage = _sim.Stage, outcome = _sim.Outcome, time = _sim.Time, stats = _sim.Stats,
                memberNames = names, memberColors = colors, autoBattle = _auto
            };
        }

        // ───────────────────────── 뷰 갱신 ─────────────────────────

        private void UpdateViews(float alpha, float dt)
        {
            if (_shownActive != _sim.ActiveIndex)
            {
                for (int i = 0; i < _avatars.Length; i++) _avatars[i].SetVisible(i == _sim.ActiveIndex);
                _shownActive = _sim.ActiveIndex;
            }
            Avatar.Pose(_sim, alpha, dt);

            _tmp.Clear();
            foreach (var kv in _enemies)
            {
                var e = _sim.FindEnemy(kv.Key);
                var view = kv.Value;
                if (e == null && !view.Finished)
                {
                    view.Die();
                }
                if (e != null) view.Pose(e, alpha, dt);
                else view.Pose(null, alpha, dt);
                if (view.Finished)
                {
                    Destroy(view.root);
                    _tmp.Add(kv.Key);
                }
            }
            foreach (int id in _tmp) _enemies.Remove(id);

            foreach (var p in _sim.Projectiles)
            {
                if (_entities.TryGetValue(p.id, out var go))
                {
                    go.transform.position = V.W(p.pos, 1.1f);
                    go.transform.rotation = V.Facing(p.dir);
                }
            }
            foreach (var r in _sim.Rings)
            {
                if (_entities.TryGetValue(r.id, out var go)) go.transform.localScale = Vector3.one * r.current;
            }
            foreach (var a in _sim.Areas)
            {
                if (_entities.TryGetValue(a.id, out var go))
                {
                    float p = a.delay > 0f ? 1f - Mathf.Clamp01(a.timer / a.delay) : 1f;
                    go.transform.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, p);
                }
            }
            foreach (var p in _sim.EnemyProjectiles)
            {
                if (_enemyProj.TryGetValue(p.id, out var go)) go.transform.position = V.W(p.pos, 1.2f);
            }
            foreach (var kv in _telegraphs)
            {
                var e = _sim.FindEnemy(kv.Key);
                if (e != null && e.ai == EnemyAi.Telegraph) kv.Value.SetProgress(1f - e.aiTimer / Mathf.Max(0.01f, e.aiDuration));
            }
        }

        private void OnDestroy()
        {
            if (_hud != null && _hud.root != null) Destroy(_hud.root.gameObject);
        }
    }
}
