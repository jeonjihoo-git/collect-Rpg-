using System;
using System.Collections.Generic;
using Lumina.Core.Combat;
using Lumina.Core.Data;

namespace Lumina.Core.Audio
{
    /// <summary>재생 요청 1건. Key 가 같으면 같은 합성 결과(캐시 키).</summary>
    public struct SfxRequest
    {
        public bool isSystem;
        public SystemSfx system;
        public Element elementA;
        public Element elementB;
        public WeaponType weapon;
        public SfxKind kind;
        public SfxTier tier;
        public Element element;
        public EnemyMaterial material;
        public float castSeconds;
        public float volume;
        public float pan;

        public string Key
        {
            get
            {
                if (isSystem) return $"sys:{system}:{elementA}:{elementB}";
                if (kind == SfxKind.HitReaction) return $"react:{material}:{tier}";
                return $"w:{weapon}:{kind}:{tier}:{element}:{castSeconds:0.00}";
            }
        }

        public float[] Render(int variant)
        {
            if (isSystem) return SfxSynth.RenderSystem(system, elementA, elementB, variant);
            if (kind == SfxKind.HitReaction) return SfxSynth.RenderHitReaction(material, tier, variant);
            return SfxSynth.RenderWeapon(weapon, kind, tier, element, variant, castSeconds);
        }
    }

    /// <summary>
    /// 전투 이벤트 → 효과음 요청 매핑(단일 원천). Unity SfxPlayer 와 헤드리스 미리듣기 도구가 같은 규칙을 사용한다.
    /// 명중음(impact)·피격 반응(hitReaction)은 오직 Hit 이벤트(피해가 적용된 틱)에서만 나온다.
    /// </summary>
    public sealed class SfxRouter
    {
        private readonly GameDatabase _db;
        private readonly HashSet<string> _tickKeys = new HashSet<string>();
        private readonly List<SfxRequest> _out = new List<SfxRequest>();

        public SfxRouter(GameDatabase db)
        {
            _db = db;
        }

        public static bool HasTail(SfxTier t) => t == SfxTier.Heavy || t == SfxTier.Finisher || t == SfxTier.Skill || t == SfxTier.Ultimate;

        /// <summary>한 틱의 이벤트들을 요청 목록으로 변환. 같은 틱의 동일 키는 1회만(다중 대상 동시 명중 시 소리 뭉침 방지).</summary>
        public List<SfxRequest> RouteTick(IReadOnlyList<CombatEvent> events)
        {
            _out.Clear();
            _tickKeys.Clear();
            foreach (var ev in events) Route(ev);
            return _out;
        }

        private void Add(SfxRequest r)
        {
            if (r.volume <= 0f) r.volume = 1f;
            if (_tickKeys.Add(r.Key)) _out.Add(r);
        }

        private void Weapon(WeaponType w, SfxKind kind, SfxTier tier, Element el, float pan = 0f, float cast = 0f, float vol = 1f)
        {
            Add(new SfxRequest { weapon = w, kind = kind, tier = tier, element = el, pan = pan, castSeconds = MathF.Round(cast * 20f) / 20f, volume = vol });
        }

        private void Sys(SystemSfx s, Element a = Element.None, Element b = Element.None, float vol = 1f)
        {
            Add(new SfxRequest { isSystem = true, system = s, elementA = a, elementB = b, volume = vol });
        }

        private void Route(CombatEvent ev)
        {
            switch (ev.type)
            {
                case CombatEventType.Swing:
                {
                    float pan = ev.weapon == WeaponType.DualBlades ? (ev.hitIndex % 2 == 0 ? -0.3f : 0.3f) : 0f;
                    Weapon(ev.weapon, SfxKind.Swing, ev.tier, Element.None, pan);
                    break;
                }
                case CombatEventType.Cast:
                    Weapon(ev.weapon, SfxKind.Cast, ev.tier, ev.element, 0f, ev.duration, 0.8f);
                    break;
                case CombatEventType.ProjectileLaunch:
                case CombatEventType.AreaSpawn:
                case CombatEventType.RingSpawn:
                    Weapon(ev.weapon, SfxKind.Projectile, ev.tier, ev.element);
                    break;
                case CombatEventType.Hit:
                {
                    float pan = ev.weapon == WeaponType.DualBlades ? (ev.hitIndex % 2 == 0 ? -0.25f : 0.25f) : 0f;
                    Weapon(ev.weapon, SfxKind.Impact, ev.tier, ev.element, pan);
                    Add(new SfxRequest { kind = SfxKind.HitReaction, material = ev.material, tier = ev.tier, volume = 0.8f });
                    if (HasTail(ev.tier)) Weapon(ev.weapon, SfxKind.Tail, ev.tier, Element.None, 0f, 0f, 0.8f);
                    break;
                }
                case CombatEventType.AreaBurst:
                    if (ev.amount <= 0f) Weapon(ev.weapon, SfxKind.Impact, ev.tier, ev.element, 0f, 0f, 0.6f);
                    break;
                case CombatEventType.Reaction:
                {
                    Element a = ev.element, b = Element.None;
                    foreach (var r in _db.Reactions)
                    {
                        if (r.name == ev.label)
                        {
                            a = r.a;
                            b = r.b;
                            break;
                        }
                    }
                    Sys(SystemSfx.Reaction, a, b);
                    break;
                }
                case CombatEventType.BreakStart: Sys(SystemSfx.Break); break;
                case CombatEventType.PerfectDodge: Sys(SystemSfx.PerfectDodge); break;
                case CombatEventType.Dodge: Sys(SystemSfx.Dodge, Element.None, Element.None, 0.8f); break;
                case CombatEventType.Tag:
                case CombatEventType.AutoSwap:
                    Sys(SystemSfx.Tag);
                    break;
                case CombatEventType.PlayerHit: Sys(SystemSfx.PlayerHurt); break;
                case CombatEventType.EnemyTelegraph: Sys(SystemSfx.EnemyTelegraph, Element.None, Element.None, 0.7f); break;
                case CombatEventType.EnemyAttack:
                    if (ev.enemyShape != EnemyAttackShape.Projectile) Sys(SystemSfx.EnemySwing, Element.None, Element.None, 0.8f);
                    break;
                case CombatEventType.EnemyProjectileLaunch: Sys(SystemSfx.EnemyProjectile, Element.None, Element.None, 0.7f); break;
                case CombatEventType.EnemyDeath: Sys(SystemSfx.EnemyDeath); break;
                case CombatEventType.ResonanceFull: Sys(SystemSfx.UltReady); break;
                case CombatEventType.Heal: Sys(SystemSfx.Heal); break;
                case CombatEventType.Buff: Sys(SystemSfx.Buff); break;
                case CombatEventType.ShieldGain: Sys(SystemSfx.Shield); break;
                case CombatEventType.BattleEnd:
                    Sys(ev.label == BattleOutcome.Victory.ToString() ? SystemSfx.Victory : SystemSfx.Defeat);
                    break;
            }
        }
    }
}
