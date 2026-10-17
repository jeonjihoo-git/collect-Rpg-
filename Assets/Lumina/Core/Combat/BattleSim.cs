using System;
using System.Collections.Generic;
using Lumina.Core.Audio;
using Lumina.Core.Data;
using Lumina.Core.Util;

namespace Lumina.Core.Combat
{
    /// <summary>
    /// 4인 파티 실시간 전투 시뮬레이션(고정 60틱/초, 결정론적).
    /// 처리 순서(GDD 03-1): 입력 → 이동/회피 → 공격/스킬/태그/궁극기 → 명중 → 타입 방어 → 속성·반응 → 브레이크 → 사망·결과.
    /// UnityEngine 의존이 없으므로 dotnet 테스트·헤드리스 도구·Unity 런타임이 같은 코드를 사용한다.
    /// </summary>
    public sealed class BattleSim
    {
        public const int TicksPerSecond = 60;
        public const float Dt = 1f / TicksPerSecond;

        private readonly GameDatabase _db;
        private readonly PrototypeBalance P;
        private readonly TempBalance T;
        private readonly DeterministicRng _rng;
        private readonly List<CombatEvent> _events = new List<CombatEvent>();

        public StageDef Stage { get; }
        public bool AutoBattle { get; }
        public MemberState[] Party { get; }
        public int ActiveIndex { get; private set; }
        public PlayerBody Body { get; } = new PlayerBody();
        public List<EnemyState> Enemies { get; } = new List<EnemyState>();
        public List<PlayerProjectile> Projectiles { get; } = new List<PlayerProjectile>();
        public List<PendingArea> Areas { get; } = new List<PendingArea>();
        public List<RingWave> Rings { get; } = new List<RingWave>();
        public List<EnemyProjectile> EnemyProjectiles { get; } = new List<EnemyProjectile>();

        public float Resonance { get; private set; }
        public float Shield { get; private set; }
        public float ShieldTimer { get; private set; }
        public float BuffAtkPct { get; private set; }
        public float BuffTimer { get; private set; }
        public float DodgeCd { get; private set; }
        public float TagCd { get; private set; }
        public float NormalCd { get; private set; }
        public int Tick { get; private set; }
        public int WaveIndex { get; private set; } = -1;
        public BattleOutcome Outcome { get; private set; } = BattleOutcome.InProgress;
        public BattleStats Stats { get; } = new BattleStats();

        /// <summary>true 면 모든 이벤트를 Log 에 누적(테스트/도구용).</summary>
        public bool KeepLog { get; set; }
        public List<CombatEvent> Log { get; } = new List<CombatEvent>();

        public float Time => Tick * Dt;
        public float TimeLimit => P.timeLimit;
        public float TimeRemaining => MathF.Max(0f, P.timeLimit - Time);
        public float ResonanceMax => P.resonanceMax;
        public float UltimateCost => P.ultimateCost;
        public float ArenaRadius => T.arenaRadius;
        public float PlayerRadius => T.playerRadius;
        public int WaveCount => Stage.waves.Length;
        public MemberState Active => Party[ActiveIndex];
        public IReadOnlyList<CombatEvent> Events => _events;
        public PrototypeBalance Prototype => P;
        public TempBalance Temp => T;

        private int _nextId = 1;
        private float _waveTimer;
        private ActionKind _buffered = ActionKind.None;
        private int _bufferedTag = -1;
        private float _bufferTimer;
        private bool _bufferedAuto;
        private bool _resonanceFullAnnounced;
        private bool _currentInputAuto;
        private bool _justStarted;
        private int _lastComboStep = -1;
        private float _comboChainTimer = 99f;
        private bool _initialEventsPending = true;

        public BattleSim(GameDatabase db, BattleSetup setup)
        {
            if (setup.party == null || setup.party.Count < 1 || setup.party.Count > 4)
                throw new ArgumentException("파티는 1~4명이어야 합니다.");
            _db = db;
            P = db.Balance.prototype;
            T = db.Balance.temp;
            Stage = setup.stage ?? throw new ArgumentException("stage 없음");
            AutoBattle = setup.autoBattle;
            _rng = new DeterministicRng(setup.seed);
            KeepLog = setup.keepLog;

            Party = new MemberState[setup.party.Count];
            var seen = new HashSet<string>();
            for (int i = 0; i < setup.party.Count; i++)
            {
                var snap = setup.party[i];
                if (!seen.Add(snap.characterId)) throw new ArgumentException("파티에 같은 캐릭터 중복: " + snap.characterId);
                var def = db.GetCharacter(snap.characterId) ?? throw new ArgumentException("알 수 없는 캐릭터: " + snap.characterId);
                var weapon = db.GetWeapon(def.weapon) ?? throw new ArgumentException("무기 정의 없음: " + def.weapon);
                float hp = DamageCalc.LevelStat(def.baseHp, P.levelHpSlope, snap.level, snap.breakthrough, P.breakthroughStatBonus);
                Party[i] = new MemberState
                {
                    index = i,
                    def = def,
                    weapon = weapon,
                    level = snap.level,
                    maxHp = hp,
                    hp = hp,
                    atk = DamageCalc.LevelStat(def.baseAtk, P.levelAtkSlope, snap.level, snap.breakthrough, P.breakthroughStatBonus),
                    defense = DamageCalc.LevelStat(def.baseDef, P.levelDefSlope, snap.level, snap.breakthrough, P.breakthroughStatBonus),
                    critRate = def.critRate,
                    critDmg = def.critDmg,
                    moveSpeed = def.moveSpeed,
                    normalMult = db.Balance.SkillLevelMultiplier(snap.normalLevel),
                    skillMult = db.Balance.SkillLevelMultiplier(snap.skillLevel),
                    ultimateMult = db.Balance.SkillLevelMultiplier(snap.ultimateLevel)
                };
            }

            Body.pos = new Vec2(0f, -4f);
            Body.prevPos = Body.pos;
            Emit(new CombatEvent { type = CombatEventType.BattleStart, member = 0, enemy = -1 });
            SpawnWave(0);
        }

        // ───────────────────────── 공개 조회 ─────────────────────────

        public EnemyState FindEnemy(int id)
        {
            foreach (var e in Enemies)
            {
                if (e.id == id) return e;
            }
            return null;
        }

        /// <summary>가장 가까운 생존 적 id(-1 없음).</summary>
        public int FindNearestEnemy(Vec2 from, float maxRange)
        {
            int best = -1;
            float bestD = maxRange * maxRange;
            foreach (var e in Enemies)
            {
                if (!e.Alive || e.ai == EnemyAi.Spawning) continue;
                float d = (e.pos - from).SqrLength;
                if (d <= bestD)
                {
                    bestD = d;
                    best = e.id;
                }
            }
            return best;
        }

        public bool CanUltimate => Resonance >= P.ultimateCost && Body.action != ActionKind.Ultimate;
        public bool CanSkill => Active.skillCd <= 0f;
        public bool CanDodge => DodgeCd <= 0f;

        /// <summary>예고 중인 적 공격 범위 안에 점 p가 있는가(자동전투 회피 판단용).</summary>
        public bool PointInEnemyAttack(EnemyState e, Vec2 p, float r)
        {
            if (e.ai != EnemyAi.Telegraph || e.attack == null) return false;
            var a = e.attack;
            switch (a.shape)
            {
                case EnemyAttackShape.Cone: return Geometry.InCone(e.lockedPos, e.lockedDir, a.range, a.arcDeg, p, r);
                case EnemyAttackShape.Circle: return Geometry.InCircle(e.lockedPos, a.radius, p, r);
                case EnemyAttackShape.Line: return Geometry.InLine(e.lockedPos, e.lockedDir, a.range, 0f, a.radius, p, r);
                case EnemyAttackShape.TargetCircle: return Geometry.InCircle(e.lockedPos, a.radius, p, r);
                case EnemyAttackShape.SelfRing: return Geometry.InCircle(e.lockedPos, a.radius, p, r);
                case EnemyAttackShape.Projectile: return Geometry.InLine(e.lockedPos, e.lockedDir, a.range, 0f, a.radius, p, r);
                default: return false;
            }
        }

        /// <summary>개발/테스트 전용: 공명 게이지 추가(궁극기 연출·타격음 점검용). 정식 빌드 UI 에서는 노출하지 않는다.</summary>
        public void DebugAddResonance(float amount) => AddResonance(amount);

        /// <summary>전투 포기(일시정지 메뉴). 보상 없음.</summary>
        public void Retreat()
        {
            if (Outcome != BattleOutcome.InProgress) return;
            _events.Clear();
            SetOutcome(BattleOutcome.Retreat);
        }

        // ───────────────────────── 메인 루프 ─────────────────────────

        public void Step(PlayerInput input, bool inputIsAuto = false)
        {
            // 생성자에서 발생한 BattleStart/첫 웨이브 이벤트는 첫 Step 결과에 함께 전달한다.
            if (_initialEventsPending) _initialEventsPending = false;
            else _events.Clear();
            if (Outcome != BattleOutcome.InProgress) return;
            Tick++;
            _currentInputAuto = inputIsAuto;

            DodgeCd = MathF.Max(0f, DodgeCd - Dt);
            TagCd = MathF.Max(0f, TagCd - Dt);
            NormalCd = MathF.Max(0f, NormalCd - Dt);
            foreach (var m in Party) m.skillCd = MathF.Max(0f, m.skillCd - Dt);
            if (ShieldTimer > 0f)
            {
                ShieldTimer -= Dt;
                if (ShieldTimer <= 0f) Shield = 0f;
            }
            if (BuffTimer > 0f)
            {
                BuffTimer -= Dt;
                if (BuffTimer <= 0f) BuffAtkPct = 0f;
            }
            Body.invulnTimer = MathF.Max(0f, Body.invulnTimer - Dt);
            Body.prevPos = Body.pos;
            foreach (var e in Enemies) e.prevPos = e.pos;

            HandleInput(input, inputIsAuto);
            UpdatePlayer(input);
            UpdatePlayerEntities();
            UpdateEnemies();
            UpdateEnemyProjectiles();
            UpdateWaves();

            if (Outcome == BattleOutcome.InProgress && Time >= P.timeLimit) SetOutcome(BattleOutcome.DefeatTimeUp);
        }

        // ───────────────────────── 입력/행동 ─────────────────────────

        private void HandleInput(PlayerInput input, bool isAuto)
        {
            // 같은 틱 다중 입력 우선순위: 회피 > 태그 > 궁극기 > 스킬 > 일반
            ActionKind pressed = ActionKind.None;
            int tag = -1;
            if (input.dodgePressed) pressed = ActionKind.Dodge;
            else if (input.tagTo >= 0) { pressed = ActionKind.None; tag = input.tagTo; }
            else if (input.ultimatePressed) pressed = ActionKind.Ultimate;
            else if (input.skillPressed) pressed = ActionKind.Skill;
            else if (input.attackPressed) pressed = ActionKind.Normal;

            if (pressed != ActionKind.None || tag >= 0)
            {
                _buffered = pressed;
                _bufferedTag = tag;
                _bufferTimer = T.inputBuffer;
                _bufferedAuto = isAuto;
            }

            if (_buffered != ActionKind.None || _bufferedTag >= 0)
            {
                bool ok = _bufferedTag >= 0 ? TryTag(_bufferedTag, input) : TryExecute(_buffered, input, _bufferedAuto);
                if (ok)
                {
                    _buffered = ActionKind.None;
                    _bufferedTag = -1;
                }
                else
                {
                    _bufferTimer -= Dt;
                    if (_bufferTimer <= 0f)
                    {
                        _buffered = ActionKind.None;
                        _bufferedTag = -1;
                    }
                }
            }
            else if (input.attackHeld)
            {
                TryExecute(ActionKind.Normal, input, isAuto);
            }
        }

        private bool IsCancelable()
        {
            switch (Body.action)
            {
                case ActionKind.None:
                    return true;
                case ActionKind.Normal:
                case ActionKind.Skill:
                case ActionKind.Ultimate:
                    return Body.current != null && Body.actionTime >= Body.current.def.ChainTime;
                default:
                    return false;
            }
        }

        private bool TryExecute(ActionKind kind, PlayerInput input, bool isAuto)
        {
            var m = Active;
            switch (kind)
            {
                case ActionKind.Dodge:
                {
                    if (DodgeCd > 0f || Body.action == ActionKind.Dodge) return false;
                    if (Body.action == ActionKind.Ultimate && Body.current != null && Body.actionTime < Body.current.def.startup) return false;
                    Vec2 dir = input.move.SqrLength > 0.04f ? input.move.Normalized : -Body.facing;
                    CancelAction();
                    Body.action = ActionKind.Dodge;
                    Body.actionTicks = 0;
                    Body.actionTime = 0f;
                    Body.actionDuration = T.dodgeDuration;
                    Body.dodgeDir = dir;
                    Body.invulnTimer = P.dodgeInvulnerable;
                    Body.perfectGrantedThisDodge = false;
                    DodgeCd = P.dodgeCooldown;
                    CountAction(ActionKind.Dodge, isAuto);
                    Emit(new CombatEvent { type = CombatEventType.Dodge, member = ActiveIndex, enemy = -1, pos = Body.pos, dir = dir, weapon = m.weapon.id, isAuto = isAuto });
                    return true;
                }
                case ActionKind.Ultimate:
                {
                    if (!CanUltimate) return false;
                    if (Body.action == ActionKind.HitStun) return false;
                    Resonance -= P.ultimateCost;
                    _resonanceFullAnnounced = false;
                    StartAction(ActionKind.Ultimate, 0, isAuto);
                    return true;
                }
                case ActionKind.Skill:
                {
                    if (m.skillCd > 0f || !IsCancelable()) return false;
                    m.skillCd = P.skillCooldown;
                    StartAction(ActionKind.Skill, 0, isAuto);
                    return true;
                }
                case ActionKind.Normal:
                {
                    if (NormalCd > 0f || !IsCancelable()) return false;
                    var combo = m.weapon.combo;
                    int next = (_lastComboStep >= 0 && _comboChainTimer <= m.weapon.comboResetTime) ? (_lastComboStep + 1) % combo.Length : 0;
                    StartAction(ActionKind.Normal, next, isAuto);
                    NormalCd = combo[next].ChainTime;
                    _lastComboStep = next;
                    _comboChainTimer = 0f;
                    return true;
                }
            }
            return false;
        }

        private bool TryTag(int to, PlayerInput input)
        {
            if (TagCd > 0f || to < 0 || to >= Party.Length || to == ActiveIndex) return false;
            if (!Party[to].Alive || Body.action == ActionKind.Ultimate) return false;
            int from = ActiveIndex;
            CancelAction();
            ActiveIndex = to;
            AddResonance(P.resonanceTag);
            NormalCd *= 1f - P.tagNormalCooldownCut;
            TagCd = P.tagCooldown;
            _lastComboStep = -1;
            Emit(new CombatEvent { type = CombatEventType.Tag, member = to, step = from, enemy = -1, pos = Body.pos, weapon = Party[to].weapon.id, isAuto = _currentInputAuto });
            return true;
        }

        private void CancelAction()
        {
            Body.action = ActionKind.None;
            Body.current = null;
            Body.actionTicks = 0;
            Body.actionTime = 0f;
            _justStarted = false;
        }

        private void StartAction(ActionKind kind, int stepIndex, bool isAuto)
        {
            var m = Active;
            var w = m.weapon;
            AttackStepDef def = kind == ActionKind.Normal ? w.combo[stepIndex] : kind == ActionKind.Skill ? w.skill : w.ultimate;
            var act = new ActionInstance
            {
                id = _nextId++,
                kind = kind,
                member = ActiveIndex,
                step = stepIndex,
                def = def,
                skill = kind == ActionKind.Skill ? m.def.skill : kind == ActionKind.Ultimate ? m.def.ultimate : null,
                weapon = w.id,
                isAuto = isAuto
            };
            Body.action = kind;
            Body.current = act;
            Body.actionTicks = 0;
            Body.actionTime = 0f;
            Body.actionDuration = def.TotalDuration;
            _justStarted = true;

            int target = FindNearestEnemy(Body.pos, T.softLockRange);
            Body.lockTarget = target;
            var te = target >= 0 ? FindEnemy(target) : null;
            if (te != null)
            {
                Vec2 to = te.pos - Body.pos;
                if (to.SqrLength > 1e-4f) Body.facing = to.Normalized;
            }

            CountAction(kind, isAuto);
            Emit(new CombatEvent
            {
                type = CombatEventType.ActionStart, member = ActiveIndex, enemy = target, action = kind, step = stepIndex,
                weapon = w.id, tier = act.Tier, swing = def.swing, hitShape = def.shape, pos = Body.pos, dir = Body.facing,
                duration = def.TotalDuration, isAuto = isAuto, element = m.def.element
            });
            if (WeaponInfo.Category(w.id) != WeaponCategory.Melee)
            {
                Emit(new CombatEvent
                {
                    type = CombatEventType.Cast, member = ActiveIndex, enemy = target, action = kind, step = stepIndex,
                    weapon = w.id, tier = act.Tier, pos = Body.pos, dir = Body.facing, duration = def.startup, element = m.def.element
                });
            }
        }

        private void CountAction(ActionKind kind, bool isAuto)
        {
            var dict = isAuto ? Stats.autoActions : Stats.manualActions;
            dict.TryGetValue(kind, out int n);
            dict[kind] = n + 1;
        }

        // ───────────────────────── 플레이어 갱신 ─────────────────────────

        private void UpdatePlayer(PlayerInput input)
        {
            var m = Active;
            if (Body.action != ActionKind.Normal || (Body.current != null && Body.actionTime >= Body.current.def.ChainTime))
                _comboChainTimer += Dt;

            switch (Body.action)
            {
                case ActionKind.None:
                {
                    Vec2 mv = input.move.ClampLength(1f);
                    if (mv.SqrLength > 0.0025f)
                    {
                        Body.pos += mv * (m.moveSpeed * Dt);
                        Body.facing = mv.Normalized;
                    }
                    break;
                }
                case ActionKind.Dodge:
                {
                    Body.actionTicks++;
                    Body.actionTime = Body.actionTicks * Dt;
                    Body.pos += Body.dodgeDir * (T.dodgeDistance / T.dodgeDuration * Dt);
                    if (Body.actionTime >= Body.actionDuration) CancelAction();
                    break;
                }
                case ActionKind.HitStun:
                {
                    Body.actionTicks++;
                    Body.actionTime = Body.actionTicks * Dt;
                    if (Body.actionTime >= Body.actionDuration) CancelAction();
                    break;
                }
                case ActionKind.Normal:
                case ActionKind.Skill:
                case ActionKind.Ultimate:
                    UpdateAttackAction(input);
                    break;
            }

            Body.pos = Geometry.ClampToArena(Body.pos, T.arenaRadius, T.playerRadius);
            // 적과 겹침 방지(몸이 적을 통과하지 않음)
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                Vec2 d = Body.pos - e.pos;
                float minD = e.def.radius + T.playerRadius;
                float len = d.Length;
                if (len < minD && len > 1e-4f) Body.pos = e.pos + d * (minD / len);
            }
        }

        private void UpdateAttackAction(PlayerInput input)
        {
            var act = Body.current;
            var def = act.def;
            float prevT = Body.actionTime;
            if (_justStarted) _justStarted = false;
            else Body.actionTicks++;
            Body.actionTime = Body.actionTicks * Dt;
            float t = Body.actionTime;

            var target = Body.lockTarget >= 0 ? FindEnemy(Body.lockTarget) : null;
            if (target != null && !target.Alive) target = null;

            // 선딜 동안 대상 추적(소프트 락온) + 전진(lunge)
            if (t <= def.startup && target != null)
            {
                Vec2 to = target.pos - Body.pos;
                if (to.SqrLength > 1e-4f) Body.facing = to.Normalized;
            }
            if (def.lunge > 0f && prevT < def.startup && def.startup > 0f)
            {
                float a = MathF.Min(t, def.startup) - MathF.Min(prevT, def.startup);
                if (a > 0f)
                {
                    float dist = def.lunge * (a / def.startup);
                    bool meleeStop = target != null && def.shape != HitShape.Line && WeaponInfo.Category(act.weapon) == WeaponCategory.Melee;
                    if (meleeStop)
                    {
                        float gap = (target.pos - Body.pos).Length - (target.def.radius + T.playerRadius + 0.35f);
                        dist = MathF.Min(dist, MathF.Max(0f, gap));
                    }
                    Body.pos += Body.facing * dist;
                }
            }

            // 휘두름 큐(근접): 피크가 명중 프레임에 오도록 선행 시간만큼 앞서 발생
            if (WeaponInfo.Category(act.weapon) == WeaponCategory.Melee)
            {
                float lead = SfxTiming.SwingLead(act.weapon);
                while (act.swingsCued < def.SafeHits)
                {
                    int i = act.swingsCued;
                    if (i > 0 && def.hitInterval < SfxTiming.MinSwingCueInterval)
                    {
                        act.swingsCued = def.SafeHits;
                        break;
                    }
                    float cueT = MathF.Max(0f, def.startup + i * def.hitInterval - lead);
                    if (t + 1e-5f < cueT) break;
                    act.swingsCued++;
                    Emit(new CombatEvent
                    {
                        type = CombatEventType.Swing, member = act.member, enemy = Body.lockTarget, action = act.kind, step = act.step,
                        hitIndex = i, weapon = act.weapon, tier = act.Tier, swing = def.swing, pos = Body.pos, dir = Body.facing,
                        duration = lead
                    });
                }
            }

            // 명중/발사 처리(다단 포함, 한 틱에 여러 타가 지나가도 모두 처리)
            while (act.hitsResolved < def.SafeHits && t + 1e-5f >= def.startup + act.hitsResolved * def.hitInterval)
            {
                ResolveHit(act, act.hitsResolved);
                act.hitsResolved++;
            }

            if (t >= def.ChainTime && input.move.SqrLength > 0.04f && Body.action != ActionKind.Ultimate)
            {
                CancelAction();
                return;
            }
            if (t >= Body.actionDuration) CancelAction();
        }

        private void ResolveHit(ActionInstance act, int hitIndex)
        {
            var def = act.def;
            var m = Party[act.member];

            if (hitIndex == 0 && act.skill != null && !act.effectApplied)
            {
                act.effectApplied = true;
                ApplySkillEffect(m, act.skill);
            }

            var target = Body.lockTarget >= 0 ? FindEnemy(Body.lockTarget) : null;
            if (target != null && !target.Alive) target = null;

            switch (def.shape)
            {
                case HitShape.Cone:
                case HitShape.Circle:
                case HitShape.Line:
                {
                    Vec2 origin = Body.pos;
                    Vec2 center = Body.pos + Body.facing * def.offset;
                    var hitList = new List<EnemyState>();
                    foreach (var e in Enemies)
                    {
                        if (!e.Alive || e.ai == EnemyAi.Spawning) continue;
                        bool inside;
                        if (def.shape == HitShape.Cone) inside = Geometry.InCone(origin, Body.facing, def.range, def.arcDeg, e.pos, e.def.radius);
                        else if (def.shape == HitShape.Circle) inside = Geometry.InCircle(center, def.radius, e.pos, e.def.radius);
                        else inside = Geometry.InLine(origin, Body.facing, def.range, def.back, def.radius, e.pos, e.def.radius);
                        if (inside) hitList.Add(e);
                    }
                    foreach (var e in hitList)
                    {
                        Vec2 dir = (e.pos - Body.pos).Normalized;
                        ApplyHit(act, e, dir.SqrLength > 0 ? dir : Body.facing, hitIndex);
                    }
                    break;
                }
                case HitShape.Projectile:
                {
                    Vec2 aim = Body.facing;
                    if (target != null)
                    {
                        Vec2 to = target.pos - Body.pos;
                        if (to.SqrLength > 1e-4f) aim = to.Normalized;
                        Body.facing = aim;
                    }
                    float angle = 0f;
                    if (def.spreadDeg > 0f && def.SafeHits > 1) angle = def.spreadDeg * ((hitIndex % 3) - 1);
                    Vec2 dir = aim.Rotated(angle);
                    var p = new PlayerProjectile
                    {
                        id = _nextId++, act = act, hitIndex = hitIndex, spawnTick = Tick, pos = Body.pos + dir * 0.6f, dir = dir, speed = def.speed,
                        remaining = def.range, radius = def.radius, pierce = def.pierce, explodeRadius = def.explodeRadius
                    };
                    Projectiles.Add(p);
                    Emit(new CombatEvent
                    {
                        type = CombatEventType.ProjectileLaunch, member = act.member, enemy = -1, entity = p.id, action = act.kind,
                        step = act.step, hitIndex = hitIndex, weapon = act.weapon, tier = act.Tier, pos = p.pos, dir = dir,
                        range = def.range, radius = def.explodeRadius, duration = def.range / MathF.Max(0.01f, def.speed),
                        element = ElementOf(act)
                    });
                    break;
                }
                case HitShape.TargetArea:
                {
                    Vec2 center;
                    if (target != null && (target.pos - Body.pos).Length <= def.range + 1f) center = target.pos;
                    else center = Body.pos + Body.facing * MathF.Min(def.range, 5f);
                    if (hitIndex > 0)
                    {
                        Vec2 jitter = Vec2.FromAngleDeg(_rng.Range(0f, 360f)) * (def.radius * 0.6f * _rng.NextFloat());
                        center += jitter;
                    }
                    center = Geometry.ClampToArena(center, T.arenaRadius, 0f);
                    var area = new PendingArea { id = _nextId++, act = act, hitIndex = hitIndex, spawnTick = Tick, center = center, radius = def.radius, timer = def.delay, delay = def.delay };
                    Areas.Add(area);
                    Emit(new CombatEvent
                    {
                        type = CombatEventType.AreaSpawn, member = act.member, enemy = -1, entity = area.id, action = act.kind,
                        step = act.step, hitIndex = hitIndex, weapon = act.weapon, tier = act.Tier, pos = center, radius = def.radius,
                        duration = def.delay, element = ElementOf(act)
                    });
                    if (def.delay <= 0f) ResolveArea(area);
                    break;
                }
                case HitShape.Ring:
                {
                    var ring = new RingWave
                    {
                        id = _nextId++, act = act, spawnTick = Tick, center = Body.pos, current = 0.3f, max = def.range, speed = def.speed,
                        thickness = MathF.Max(0.4f, def.radius)
                    };
                    Rings.Add(ring);
                    Emit(new CombatEvent
                    {
                        type = CombatEventType.RingSpawn, member = act.member, enemy = -1, entity = ring.id, action = act.kind,
                        step = act.step, hitIndex = hitIndex, weapon = act.weapon, tier = act.Tier, pos = ring.center,
                        range = def.range, radius = ring.thickness, duration = def.range / MathF.Max(0.01f, def.speed),
                        element = ElementOf(act)
                    });
                    break;
                }
            }
        }

        private Element ElementOf(ActionInstance act)
        {
            if (act.kind == ActionKind.Normal || act.skill == null || !act.skill.appliesElement) return Element.None;
            return Party[act.member].def.element;
        }

        private void ApplySkillEffect(MemberState caster, SkillDef skill)
        {
            switch (skill.effect)
            {
                case SkillEffect.Heal:
                {
                    float amount = caster.atk * skill.healAtkRatio;
                    foreach (var m in Party)
                    {
                        if (!m.Alive) continue;
                        m.hp = MathF.Min(m.maxHp, m.hp + amount);
                    }
                    Emit(new CombatEvent { type = CombatEventType.Heal, member = caster.index, enemy = -1, amount = amount, pos = Body.pos });
                    break;
                }
                case SkillEffect.Buff:
                {
                    BuffAtkPct = MathF.Max(BuffAtkPct, skill.buffAtkPct);
                    BuffTimer = MathF.Max(BuffTimer, skill.buffDuration);
                    Emit(new CombatEvent { type = CombatEventType.Buff, member = caster.index, enemy = -1, amount = skill.buffAtkPct, duration = skill.buffDuration, pos = Body.pos });
                    break;
                }
            }
        }

        // ───────────────────────── 명중 처리 ─────────────────────────

        /// <summary>
        /// 한 대상에 대한 1회 명중. 피해 적용과 Hit 이벤트 발생이 같은 틱에서 일어난다(타격음·피격 반응 동기화의 기준).
        /// </summary>
        private void ApplyHit(ActionInstance act, EnemyState e, Vec2 dir, int hitIndex)
        {
            if (!e.Alive) return;
            var m = Party[act.member];
            var def = act.def;
            float coef = act.kind == ActionKind.Normal ? def.coef : (act.skill != null ? act.skill.coefTotal / def.SafeHits : 0f);
            float lvMult = act.kind == ActionKind.Normal ? m.normalMult : act.kind == ActionKind.Skill ? m.skillMult : m.ultimateMult;

            // 속성 부착 & 반응(스킬/궁극기만 부착 — 가정 A-07)
            ElementReactionDef reaction = null;
            float reactionMult = 1f;
            Element el = ElementOf(act);
            if (el != Element.None)
            {
                reaction = ApplyElement(e, el);
                if (reaction != null)
                {
                    Stats.reactions++;
                    Stats.reactionCounts.TryGetValue(reaction.name, out int rc);
                    Stats.reactionCounts[reaction.name] = rc + 1;
                    if (reaction.effect == ReactionEffect.Damage)
                    {
                        reactionMult = reaction.multiplier;
                    }
                    else
                    {
                        float shield = m.atk * T.shieldAtkRatio * reaction.multiplier;
                        Shield = MathF.Max(Shield, shield);
                        ShieldTimer = T.shieldDuration;
                        Emit(new CombatEvent { type = CombatEventType.ShieldGain, member = act.member, enemy = e.id, amount = shield, pos = Body.pos, label = reaction.name });
                    }
                    Emit(new CombatEvent
                    {
                        type = CombatEventType.Reaction, member = act.member, enemy = e.id, label = reaction.name, element = el,
                        pos = e.pos, amount = reaction.multiplier, weapon = act.weapon
                    });
                }
            }

            bool staggered = e.Staggered;
            bool crit = _rng.Chance(m.critRate);
            var input = new DamageCalc.Input
            {
                attack = m.atk * (1f + BuffAtkPct),
                coef = coef,
                skillLevelMult = lvMult,
                targetDefense = m.def.attackType == AttackType.Physical ? e.def.physDef : e.def.magDef,
                targetLevel = e.def.level,
                reactionMult = reactionMult,
                staggerMult = staggered ? P.breakDamageMultiplier : 1f,
                crit = crit,
                critDmg = m.critDmg
            };
            float dmg = DamageCalc.Compute(P, input);
            e.hp -= dmg;
            e.lastHitTime = Time;
            Stats.totalDamage += dmg;
            Stats.memberDamage[act.member] += dmg;
            Stats.hitsLanded++;

            // 브레이크
            bool brokeNow = false;
            if (!staggered && def.breakDmg > 0f)
            {
                e.breakGauge -= def.breakDmg;
                if (e.breakGauge <= 0f)
                {
                    e.breakGauge = 0f;
                    e.staggerTimer = P.breakStaggerSeconds;
                    brokeNow = true;
                }
            }

            // 넉백(음수=끌어당김)
            float kb = def.knockback * (e.def.isBoss ? T.bossKnockbackScale : 1f);
            if (MathF.Abs(kb) > 0f && e.ai != EnemyAi.Telegraph)
            {
                Vec2 np = e.pos + dir.Normalized * kb;
                if (kb < 0f && (np - Body.pos).Length < e.def.radius + T.playerRadius + 0.3f) np = e.pos;
                e.pos = Geometry.ClampToArena(np, T.arenaRadius, e.def.radius);
            }

            Emit(new CombatEvent
            {
                type = CombatEventType.Hit, member = act.member, enemy = e.id, action = act.kind, step = act.step, hitIndex = hitIndex,
                weapon = act.weapon, tier = act.Tier, swing = def.swing, hitShape = def.shape, amount = dmg, crit = crit,
                element = el, label = reaction?.name, pos = e.pos, dir = dir, hitstop = def.hitstop, shake = def.shake,
                material = e.def.material, isAuto = act.isAuto
            });

            if (brokeNow)
            {
                Stats.breaks++;
                if (e.ai == EnemyAi.Telegraph)
                {
                    Emit(new CombatEvent { type = CombatEventType.EnemyTelegraphCancel, enemy = e.id, member = -1, pos = e.pos });
                    e.ai = EnemyAi.Chase;
                }
                Emit(new CombatEvent { type = CombatEventType.BreakStart, enemy = e.id, member = act.member, pos = e.pos, duration = P.breakStaggerSeconds, weapon = act.weapon });
            }

            if (!act.resonanceGranted)
            {
                act.resonanceGranted = true;
                if (act.kind == ActionKind.Normal) AddResonance(P.resonanceNormal);
                else if (act.kind == ActionKind.Skill) AddResonance(P.resonanceSkill);
            }

            if (e.hp <= 0f) Kill(e);
        }

        /// <summary>
        /// 속성 부착/반응. 서로 다른 2속성(순서 무관)이 유효 부착 시간 내 만나면 반응 1회 후 부착 해제.
        /// 같은 속성은 갱신, 무반응 조합은 새 속성으로 덮어씀(가정 A-08).
        /// </summary>
        private ElementReactionDef ApplyElement(EnemyState e, Element el)
        {
            if (e.aura == Element.None || e.auraTimer <= 0f || e.aura == el)
            {
                e.aura = el;
                e.auraTimer = P.auraDuration;
                return null;
            }
            var r = _db.FindReaction(e.aura, el);
            if (r != null)
            {
                e.aura = Element.None;
                e.auraTimer = 0f;
                return r;
            }
            e.aura = el;
            e.auraTimer = P.auraDuration;
            return null;
        }

        private void Kill(EnemyState e)
        {
            e.hp = 0f;
            e.ai = EnemyAi.Dead;
            e.staggerTimer = 0f;
            Emit(new CombatEvent { type = CombatEventType.EnemyDeath, enemy = e.id, member = -1, pos = e.pos, material = e.def.material, label = e.def.displayName });
        }

        private void AddResonance(float amount)
        {
            Resonance = MathF.Min(P.resonanceMax, Resonance + amount);
            if (Resonance >= P.ultimateCost && !_resonanceFullAnnounced)
            {
                _resonanceFullAnnounced = true;
                Emit(new CombatEvent { type = CombatEventType.ResonanceFull, member = ActiveIndex, enemy = -1 });
            }
        }

        // ───────────────────────── 투사체/장판/링 ─────────────────────────

        private void UpdatePlayerEntities()
        {
            // 생성된 틱에는 움직이지 않는다(발사음과 명중음이 최소 1틱 분리 — 같은 프레임 겹침 방지)
            foreach (var p in Projectiles)
            {
                if (p.done || p.spawnTick == Tick) continue;
                float stepLen = p.speed * Dt;
                Vec2 next = p.pos + p.dir * stepLen;
                EnemyState hit = null;
                float hitAlong = float.MaxValue;
                foreach (var e in Enemies)
                {
                    if (!e.Alive || e.ai == EnemyAi.Spawning || p.hitSet.Contains(e.id)) continue;
                    if (Geometry.SegmentDistance(p.pos, next, e.pos) <= p.radius + e.def.radius)
                    {
                        float along = Vec2.Dot(e.pos - p.pos, p.dir);
                        if (along < hitAlong)
                        {
                            hitAlong = along;
                            hit = e;
                        }
                    }
                }
                if (hit != null)
                {
                    if (p.explodeRadius > 0f)
                    {
                        Explode(p, hit.pos);
                    }
                    else
                    {
                        p.hitSet.Add(hit.id);
                        ApplyHit(p.act, hit, p.dir, p.hitIndex);
                        if (!p.pierce) FinishProjectile(p, hit.pos);
                    }
                }
                if (p.done) continue;
                p.pos = next;
                p.remaining -= stepLen;
                if (p.remaining <= 0f)
                {
                    if (p.explodeRadius > 0f) Explode(p, p.pos);
                    else FinishProjectile(p, p.pos);
                }
            }
            Projectiles.RemoveAll(x => x.done);

            foreach (var a in Areas)
            {
                if (a.done || a.spawnTick == Tick) continue;
                a.timer -= Dt;
                if (a.timer <= 0f) ResolveArea(a);
            }
            Areas.RemoveAll(x => x.done);

            foreach (var r in Rings)
            {
                if (r.done || r.spawnTick == Tick) continue;
                float prev = r.current;
                r.current = MathF.Min(r.max, r.current + r.speed * Dt);
                foreach (var e in Enemies)
                {
                    if (!e.Alive || e.ai == EnemyAi.Spawning || r.hitSet.Contains(e.id)) continue;
                    float d = (e.pos - r.center).Length;
                    if (d - e.def.radius <= r.current && d + e.def.radius >= prev - r.thickness)
                    {
                        r.hitSet.Add(e.id);
                        Vec2 dir = (e.pos - r.center).Normalized;
                        ApplyHit(r.act, e, dir.SqrLength > 0 ? dir : Vec2.Forward, 0);
                    }
                }
                if (r.current >= r.max)
                {
                    r.done = true;
                    Emit(new CombatEvent { type = CombatEventType.ProjectileExpire, entity = r.id, member = r.act.member, enemy = -1, pos = r.center, weapon = r.act.weapon });
                }
            }
            Rings.RemoveAll(x => x.done);
        }

        private void Explode(PlayerProjectile p, Vec2 at)
        {
            int count = 0;
            foreach (var e in Enemies)
            {
                if (!e.Alive || e.ai == EnemyAi.Spawning) continue;
                if (Geometry.InCircle(at, p.explodeRadius, e.pos, e.def.radius))
                {
                    Vec2 dir = (e.pos - at).Normalized;
                    ApplyHit(p.act, e, dir.SqrLength > 0 ? dir : p.dir, p.hitIndex);
                    count++;
                }
            }
            Emit(new CombatEvent
            {
                type = CombatEventType.AreaBurst, entity = p.id, member = p.act.member, enemy = -1, pos = at, radius = p.explodeRadius,
                amount = count, weapon = p.act.weapon, tier = p.act.Tier, element = ElementOf(p.act)
            });
            FinishProjectile(p, at);
        }

        private void FinishProjectile(PlayerProjectile p, Vec2 at)
        {
            p.done = true;
            Emit(new CombatEvent { type = CombatEventType.ProjectileExpire, entity = p.id, member = p.act.member, enemy = -1, pos = at, weapon = p.act.weapon });
        }

        private void ResolveArea(PendingArea a)
        {
            if (a.done) return;
            a.done = true;
            int count = 0;
            foreach (var e in Enemies)
            {
                if (!e.Alive || e.ai == EnemyAi.Spawning) continue;
                if (Geometry.InCircle(a.center, a.radius, e.pos, e.def.radius))
                {
                    Vec2 dir = (e.pos - a.center).Normalized;
                    ApplyHit(a.act, e, dir.SqrLength > 0 ? dir : Vec2.Forward, a.hitIndex);
                    count++;
                }
            }
            Emit(new CombatEvent
            {
                type = CombatEventType.AreaBurst, entity = a.id, member = a.act.member, enemy = -1, pos = a.center, radius = a.radius,
                amount = count, weapon = a.act.weapon, tier = a.act.Tier, element = ElementOf(a.act)
            });
        }

        // ───────────────────────── 적 AI ─────────────────────────

        private void UpdateEnemies()
        {
            foreach (var e in Enemies)
            {
                if (!e.Alive) continue;
                if (e.auraTimer > 0f)
                {
                    e.auraTimer -= Dt;
                    if (e.auraTimer <= 0f) e.aura = Element.None;
                }
                for (int i = 0; i < e.attackCds.Length; i++) e.attackCds[i] = MathF.Max(0f, e.attackCds[i] - Dt);

                if (e.Staggered)
                {
                    e.staggerTimer -= Dt;
                    if (e.staggerTimer <= 0f)
                    {
                        e.staggerTimer = 0f;
                        e.breakGauge = e.breakMax;
                        e.ai = EnemyAi.Chase;
                        Emit(new CombatEvent { type = CombatEventType.BreakEnd, enemy = e.id, member = -1, pos = e.pos });
                    }
                    continue;
                }
                if (Time - e.lastHitTime >= T.breakRegenDelay)
                    e.breakGauge = MathF.Min(e.breakMax, e.breakGauge + P.breakRegenPerSecond * Dt);

                if (e.def.isBoss && !e.enraged && e.def.enrageHpPct > 0f && e.hp / e.maxHp <= e.def.enrageHpPct) e.enraged = true;

                switch (e.ai)
                {
                    case EnemyAi.Spawning:
                        e.aiTimer -= Dt;
                        if (e.aiTimer <= 0f) e.ai = EnemyAi.Chase;
                        break;
                    case EnemyAi.Chase:
                        UpdateChase(e);
                        break;
                    case EnemyAi.Telegraph:
                        e.aiTimer -= Dt;
                        if (e.aiTimer <= 0f) ResolveEnemyAttack(e);
                        break;
                    case EnemyAi.Recover:
                        e.aiTimer -= Dt;
                        if (e.attack != null && e.attack.dash)
                        {
                            float dashTime = 0.25f;
                            float elapsed = e.aiDuration - e.aiTimer;
                            float k = Math.Clamp(elapsed / dashTime, 0f, 1f);
                            e.pos = Vec2.Lerp(e.dashFrom, e.dashTo, k);
                        }
                        if (e.aiTimer <= 0f) e.ai = EnemyAi.Chase;
                        break;
                }
                e.pos = Geometry.ClampToArena(e.pos, T.arenaRadius, e.def.radius);
            }
        }

        private void UpdateChase(EnemyState e)
        {
            Vec2 to = Body.pos - e.pos;
            float dist = to.Length;
            if (dist > 1e-4f) e.facing = to / dist;

            // 사용 가능한 공격(쿨다운·거리) 가중 랜덤 선택
            float total = 0f;
            for (int i = 0; i < e.def.attacks.Length; i++)
            {
                var a = e.def.attacks[i];
                if (e.attackCds[i] <= 0f && dist >= a.minRange && dist <= a.maxRange) total += a.weight;
            }
            if (total > 0f)
            {
                float pick = _rng.NextFloat() * total;
                for (int i = 0; i < e.def.attacks.Length; i++)
                {
                    var a = e.def.attacks[i];
                    if (!(e.attackCds[i] <= 0f && dist >= a.minRange && dist <= a.maxRange)) continue;
                    pick -= a.weight;
                    if (pick <= 0f)
                    {
                        StartTelegraph(e, i);
                        return;
                    }
                }
            }

            Vec2 move = Vec2.Zero;
            if (dist > e.def.preferredRange) move = e.facing;
            else if (e.def.preferredRange > 3f && dist < e.def.preferredRange - 1.5f) move = -e.facing;
            // 겹침 방지
            foreach (var o in Enemies)
            {
                if (o == e || !o.Alive) continue;
                Vec2 d = e.pos - o.pos;
                float minD = e.def.radius + o.def.radius + 0.2f;
                float len = d.Length;
                if (len < minD && len > 1e-4f) move += d / len * ((minD - len) / minD * 2f);
            }
            if (move.SqrLength > 1e-4f) e.pos += move.ClampLength(1f) * (e.def.moveSpeed * Dt);
        }

        private void StartTelegraph(EnemyState e, int attackIndex)
        {
            var a = e.def.attacks[attackIndex];
            float tele = a.telegraph * (e.enraged ? e.def.enrageTelegraphMult : 1f);
            Vec2 dir = (Body.pos - e.pos).Normalized;
            if (dir.SqrLength < 1e-4f) dir = e.facing;
            e.facing = dir;
            e.ai = EnemyAi.Telegraph;
            e.attack = a;
            e.aiTimer = tele;
            e.aiDuration = tele;
            e.lockedDir = dir;
            switch (a.shape)
            {
                case EnemyAttackShape.Circle: e.lockedPos = e.pos + dir * a.offset; break;
                case EnemyAttackShape.TargetCircle: e.lockedPos = Body.pos; break;
                default: e.lockedPos = e.pos; break;
            }
            if (a.dash)
            {
                e.dashFrom = e.pos;
                e.dashTo = Geometry.ClampToArena(e.pos + dir * MathF.Max(0f, a.range - e.def.radius), T.arenaRadius, e.def.radius);
            }
            e.attackCds[attackIndex] = a.cooldown;
            Emit(new CombatEvent
            {
                type = CombatEventType.EnemyTelegraph, enemy = e.id, member = -1, enemyShape = a.shape, pos = e.lockedPos, dir = dir,
                radius = a.radius, range = a.range, arcDeg = a.arcDeg, duration = tele, label = a.name, material = e.def.material
            });
        }

        private void ResolveEnemyAttack(EnemyState e)
        {
            var a = e.attack;
            e.ai = EnemyAi.Recover;
            e.aiTimer = a.recovery;
            e.aiDuration = a.recovery;
            Emit(new CombatEvent
            {
                type = CombatEventType.EnemyAttack, enemy = e.id, member = -1, enemyShape = a.shape, pos = e.lockedPos, dir = e.lockedDir,
                radius = a.radius, range = a.range, arcDeg = a.arcDeg, label = a.name, material = e.def.material
            });

            if (a.shape == EnemyAttackShape.Projectile)
            {
                var p = new EnemyProjectile
                {
                    id = _nextId++, owner = e.id, attack = a, pos = e.pos + e.lockedDir * (e.def.radius + 0.3f), dir = e.lockedDir,
                    speed = a.speed, remaining = a.range, radius = a.radius
                };
                EnemyProjectiles.Add(p);
                Emit(new CombatEvent { type = CombatEventType.EnemyProjectileLaunch, enemy = e.id, member = -1, entity = p.id, pos = p.pos, dir = p.dir, radius = p.radius, range = a.range });
                return;
            }

            bool inside = PointInAttackShape(e, a, Body.pos, T.playerRadius);
            if (inside) PlayerReceiveAttack(e.def, e.id, a, e.lockedDir);
        }

        private static bool PointInAttackShape(EnemyState e, EnemyAttackDef a, Vec2 p, float r)
        {
            switch (a.shape)
            {
                case EnemyAttackShape.Cone: return Geometry.InCone(e.lockedPos, e.lockedDir, a.range, a.arcDeg, p, r);
                case EnemyAttackShape.Circle:
                case EnemyAttackShape.TargetCircle:
                case EnemyAttackShape.SelfRing:
                    return Geometry.InCircle(e.lockedPos, a.radius, p, r);
                case EnemyAttackShape.Line: return Geometry.InLine(e.lockedPos, e.lockedDir, a.range, 0f, a.radius, p, r);
                default: return false;
            }
        }

        private void UpdateEnemyProjectiles()
        {
            foreach (var p in EnemyProjectiles)
            {
                if (p.done) continue;
                float stepLen = p.speed * Dt;
                Vec2 next = p.pos + p.dir * stepLen;
                if (Geometry.SegmentDistance(p.pos, next, Body.pos) <= p.radius + T.playerRadius)
                {
                    var owner = FindEnemy(p.owner);
                    p.done = true;
                    Emit(new CombatEvent { type = CombatEventType.EnemyProjectileExpire, entity = p.id, enemy = p.owner, member = -1, pos = Body.pos });
                    if (owner != null) PlayerReceiveAttack(owner.def, owner.id, p.attack, p.dir);
                    continue;
                }
                p.pos = next;
                p.remaining -= stepLen;
                if (p.remaining <= 0f)
                {
                    p.done = true;
                    Emit(new CombatEvent { type = CombatEventType.EnemyProjectileExpire, entity = p.id, enemy = p.owner, member = -1, pos = p.pos });
                }
            }
            EnemyProjectiles.RemoveAll(x => x.done);
        }

        private void PlayerReceiveAttack(EnemyDef src, int enemyId, EnemyAttackDef a, Vec2 dir)
        {
            if (Outcome != BattleOutcome.InProgress) return;
            var m = Active;

            // 회피 무적 중 피격 = 완벽 회피(회피 1회당 공명 1회)
            if (Body.invulnTimer > 0f)
            {
                if (!Body.perfectGrantedThisDodge)
                {
                    Body.perfectGrantedThisDodge = true;
                    Stats.perfectDodges++;
                    AddResonance(P.resonancePerfectDodge);
                    Emit(new CombatEvent { type = CombatEventType.PerfectDodge, member = ActiveIndex, enemy = enemyId, pos = Body.pos, label = a.name });
                }
                return;
            }
            // 궁극기 선딜 무적(가정 A-10)
            if (Body.action == ActionKind.Ultimate && Body.current != null &&
                Body.actionTime < MathF.Min(Body.current.def.startup, T.ultimateInvulnerableStartup)) return;

            float k = DamageCalc.DefenseK(P, m.level);
            float red = DamageCalc.DefenseReduction(m.defense, 0f, 0f, k);
            float dmg = src.atk * a.coef * (1f - red);
            if (Shield > 0f)
            {
                float absorbed = MathF.Min(Shield, dmg);
                Shield -= absorbed;
                dmg -= absorbed;
                Emit(new CombatEvent { type = CombatEventType.ShieldAbsorb, member = ActiveIndex, enemy = enemyId, amount = absorbed, pos = Body.pos });
            }
            m.hp -= dmg;
            Stats.hitsTaken++;
            Emit(new CombatEvent
            {
                type = CombatEventType.PlayerHit, member = ActiveIndex, enemy = enemyId, amount = dmg, pos = Body.pos, dir = dir,
                material = src.material, label = a.name, shake = MathF.Min(1f, 0.25f + a.coef * 0.1f)
            });

            if (m.hp <= 0f)
            {
                m.hp = 0f;
                Emit(new CombatEvent { type = CombatEventType.MemberDown, member = ActiveIndex, enemy = enemyId, pos = Body.pos });
                CancelAction();
                int next = -1;
                for (int i = 1; i <= Party.Length; i++)
                {
                    int j = (ActiveIndex + i) % Party.Length;
                    if (Party[j].Alive)
                    {
                        next = j;
                        break;
                    }
                }
                if (next < 0)
                {
                    SetOutcome(BattleOutcome.DefeatAllDown);
                    return;
                }
                int from = ActiveIndex;
                ActiveIndex = next;
                _lastComboStep = -1;
                Emit(new CombatEvent { type = CombatEventType.AutoSwap, member = next, step = from, enemy = -1, pos = Body.pos, weapon = Party[next].weapon.id });
                return;
            }

            if (Body.action != ActionKind.Ultimate)
            {
                CancelAction();
                Body.action = ActionKind.HitStun;
                Body.actionTicks = 0;
                Body.actionTime = 0f;
                Body.actionDuration = T.playerHitStun;
                Body.pos = Geometry.ClampToArena(Body.pos + dir.Normalized * 0.3f, T.arenaRadius, T.playerRadius);
            }
        }

        // ───────────────────────── 웨이브/결과 ─────────────────────────

        private void SpawnWave(int index)
        {
            WaveIndex = index;
            _waveTimer = 0f;
            var wave = Stage.waves[index];
            foreach (var sp in wave.spawns)
            {
                var def = _db.GetEnemy(sp.enemy) ?? throw new ArgumentException("알 수 없는 적: " + sp.enemy);
                var e = new EnemyState
                {
                    id = _nextId++,
                    def = def,
                    pos = new Vec2(sp.x, sp.y),
                    maxHp = def.hp,
                    hp = def.hp,
                    breakMax = _db.Balance.BreakGauge(def.breakTier),
                    ai = EnemyAi.Spawning,
                    aiTimer = T.spawnGrace,
                    attackCds = new float[def.attacks.Length]
                };
                e.prevPos = e.pos;
                e.breakGauge = e.breakMax;
                for (int i = 0; i < e.attackCds.Length; i++) e.attackCds[i] = _rng.Range(0.3f, 1.4f) + i * 0.4f;
                Enemies.Add(e);
                Emit(new CombatEvent { type = CombatEventType.EnemySpawn, enemy = e.id, member = -1, pos = e.pos, label = def.displayName, material = def.material, radius = def.radius });
            }
            Emit(new CombatEvent { type = CombatEventType.WaveStart, step = index, member = -1, enemy = -1, amount = Stage.waves.Length });
        }

        private void UpdateWaves()
        {
            if (Outcome != BattleOutcome.InProgress) return;
            foreach (var e in Enemies)
            {
                if (e.Alive) return;
            }
            if (WaveIndex >= Stage.waves.Length - 1)
            {
                SetOutcome(BattleOutcome.Victory);
                return;
            }
            _waveTimer += Dt;
            if (_waveTimer >= T.waveDelay)
            {
                Enemies.RemoveAll(x => !x.Alive);
                SpawnWave(WaveIndex + 1);
            }
        }

        private void SetOutcome(BattleOutcome o)
        {
            if (Outcome != BattleOutcome.InProgress) return;
            Outcome = o;
            Emit(new CombatEvent { type = CombatEventType.BattleEnd, member = -1, enemy = -1, label = o.ToString(), amount = Time });
        }

        public bool AllMembersAlive
        {
            get
            {
                foreach (var m in Party)
                {
                    if (!m.Alive) return false;
                }
                return true;
            }
        }

        private void Emit(CombatEvent ev)
        {
            ev.tick = Tick;
            _events.Add(ev);
            if (KeepLog) Log.Add(ev);
        }
    }
}
