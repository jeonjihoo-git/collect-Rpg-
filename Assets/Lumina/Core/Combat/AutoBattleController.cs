using System;
using Lumina.Core.Data;
using Lumina.Core.Util;

namespace Lumina.Core.Combat
{
    /// <summary>
    /// 자동전투 AI [확정 정책: 일반공격·스킬·회피·궁극기 자동 사용].
    /// 타깃 선택·타이밍 알고리즘은 미확정(C-12)이라 이 구현은 가정 A-11: 가장 가까운 적, 예고 종료 0.22초 전 회피,
    /// 공명 가득 시 궁극기, 쿨다운 끝나면 스킬, 사거리 밖이면 접근. 태그는 자동으로 하지 않는다(수동 태그는 허용).
    /// </summary>
    public sealed class AutoBattleController
    {
        public float DodgeWindow = 0.22f;

        public PlayerInput Decide(BattleSim sim)
        {
            var input = PlayerInput.None;
            if (sim.Outcome != BattleOutcome.InProgress) return input;
            var body = sim.Body;
            var m = sim.Active;

            // 1) 회피: 곧 명중할 예고 공격/적 투사체
            if (sim.CanDodge && body.action != ActionKind.Dodge)
            {
                Vec2 escape;
                if (ThreatImminent(sim, out escape))
                {
                    input.move = escape;
                    input.dodgePressed = true;
                    return input;
                }
            }

            int targetId = sim.FindNearestEnemy(body.pos, 99f);
            if (targetId < 0)
            {
                // 다음 웨이브 대기: 중앙 쪽으로 이동
                if (body.pos.Length > 2f) input.move = (-body.pos).Normalized;
                return input;
            }
            var target = sim.FindEnemy(targetId);
            float dist = (target.pos - body.pos).Length - target.def.radius;

            // 2) 궁극기
            if (sim.CanUltimate && dist <= EffectiveRange(m.weapon.ultimate) + 0.5f)
            {
                input.ultimatePressed = true;
                return input;
            }
            // 3) 스킬
            if (sim.CanSkill && body.action == ActionKind.None && dist <= EffectiveRange(m.weapon.skill) + 0.5f)
            {
                input.skillPressed = true;
                return input;
            }

            // 예고 범위 안이고 회피 쿨다운이면 범위 밖으로 이동
            if (!sim.CanDodge && InsideAnyTelegraph(sim, out Vec2 away))
            {
                input.move = away;
                return input;
            }

            // 4) 접근 또는 일반 공격
            float range = EffectiveRange(m.weapon.combo[0]);
            if (dist > range)
            {
                input.move = (target.pos - body.pos).Normalized;
            }
            else
            {
                input.attackHeld = true;
                input.attackPressed = body.action == ActionKind.None;
            }
            return input;
        }

        public static float EffectiveRange(AttackStepDef s)
        {
            switch (s.shape)
            {
                case HitShape.Cone: return s.range * 0.9f;
                case HitShape.Circle: return s.offset + s.radius * 0.85f;
                case HitShape.Line: return s.range * 0.9f;
                case HitShape.Projectile: return MathF.Min(s.range * 0.7f, 9f);
                case HitShape.TargetArea: return MathF.Min(s.range * 0.8f, 8f);
                case HitShape.Ring: return s.range * 0.8f;
                default: return 2f;
            }
        }

        private bool ThreatImminent(BattleSim sim, out Vec2 escape)
        {
            var body = sim.Body;
            foreach (var e in sim.Enemies)
            {
                if (e.ai != EnemyAi.Telegraph || e.attack == null) continue;
                if (e.attack.shape == EnemyAttackShape.Projectile) continue;
                if (e.aiTimer > DodgeWindow) continue;
                if (!sim.PointInEnemyAttack(e, body.pos, sim.PlayerRadius + 0.3f)) continue;
                escape = EscapeDir(e, body.pos);
                return true;
            }
            foreach (var p in sim.EnemyProjectiles)
            {
                Vec2 to = body.pos - p.pos;
                float along = Vec2.Dot(to, p.dir);
                if (along < 0f) continue;
                float tHit = along / MathF.Max(0.01f, p.speed);
                if (tHit > DodgeWindow) continue;
                float side = MathF.Abs(Vec2.Cross(p.dir, to));
                if (side > p.radius + sim.PlayerRadius + 0.3f) continue;
                Vec2 perp = new Vec2(-p.dir.y, p.dir.x);
                escape = Vec2.Cross(p.dir, to) >= 0f ? perp : -perp;
                return true;
            }
            escape = Vec2.Zero;
            return false;
        }

        private static bool InsideAnyTelegraph(BattleSim sim, out Vec2 away)
        {
            foreach (var e in sim.Enemies)
            {
                if (e.ai != EnemyAi.Telegraph || e.attack == null) continue;
                if (sim.PointInEnemyAttack(e, sim.Body.pos, sim.PlayerRadius + 0.3f))
                {
                    away = EscapeDir(e, sim.Body.pos);
                    return true;
                }
            }
            away = Vec2.Zero;
            return false;
        }

        private static Vec2 EscapeDir(EnemyState e, Vec2 p)
        {
            var a = e.attack;
            if (a.shape == EnemyAttackShape.Line || a.shape == EnemyAttackShape.Projectile)
            {
                Vec2 n = e.lockedDir;
                Vec2 perp = new Vec2(-n.y, n.x);
                return Vec2.Cross(n, p - e.lockedPos) >= 0f ? perp : -perp;
            }
            Vec2 d = p - e.lockedPos;
            return d.SqrLength > 1e-4f ? d.Normalized : new Vec2(1f, 0f);
        }
    }

    /// <summary>
    /// 자동전투 중 수동 입력 병합 [가정 A-12]: 수동 입력이 있으면 그 틱은 수동 우선,
    /// 마지막 수동 입력 후 autoManualOverride 초 동안 자동 입력을 멈춘다.
    /// </summary>
    public sealed class InputMerger
    {
        private float _manualHold;
        public bool LastWasAuto { get; private set; }

        public PlayerInput Merge(PlayerInput manual, PlayerInput auto, bool autoEnabled, float dt, float overrideSeconds)
        {
            if (!autoEnabled)
            {
                LastWasAuto = false;
                return manual;
            }
            if (manual.HasAny)
            {
                _manualHold = overrideSeconds;
                LastWasAuto = false;
                return manual;
            }
            if (_manualHold > 0f)
            {
                _manualHold -= dt;
                LastWasAuto = false;
                return manual;
            }
            LastWasAuto = true;
            return auto;
        }
    }
}
