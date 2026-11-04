using System;
using Lumina.Core.Data;

namespace Lumina.Core.Combat
{
    /// <summary>
    /// GDD 03-2 피해 공식 [원본 기준값].
    /// 피해 = 타입 공격력 × 모션 계수 × 스킬 레벨 배율 × (1 − 방어 감소율) × 상황 배율(반응·치명·스태거)
    /// K = 300 × (1 + 0.02 × 대상 레벨), 유효 방어 = D × (1 − 관통률),
    /// 감소율 = max(0, 유효 방어/(유효 방어 + K) − 방어 무시%p). 관통과 방어 무시는 중복 적용하지 않는다(각자 한 번).
    /// </summary>
    public static class DamageCalc
    {
        public static float DefenseK(PrototypeBalance p, int targetLevel) => p.defenseK * (1f + p.defenseKPerLevel * targetLevel);

        public static float DefenseReduction(float defense, float penetration, float ignorePct, float k)
        {
            float eff = MathF.Max(0f, defense * (1f - Math.Clamp(penetration, 0f, 1f)));
            if (eff + k <= 0f) return 0f;
            return MathF.Max(0f, eff / (eff + k) - ignorePct);
        }

        public struct Input
        {
            public float attack;
            public float coef;
            public float skillLevelMult;
            public float targetDefense;
            public int targetLevel;
            public float penetration;
            public float ignorePct;
            public float reactionMult;
            public float staggerMult;
            public bool crit;
            public float critDmg;
        }

        public static float Compute(PrototypeBalance p, in Input i)
        {
            float k = DefenseK(p, i.targetLevel);
            float red = DefenseReduction(i.targetDefense, i.penetration, i.ignorePct, k);
            float mult = (i.reactionMult <= 0f ? 1f : i.reactionMult) * (i.staggerMult <= 0f ? 1f : i.staggerMult);
            if (i.crit) mult *= 1f + i.critDmg;
            float lv = i.skillLevelMult <= 0f ? 1f : i.skillLevelMult;
            return MathF.Max(0f, i.attack * i.coef * lv * (1f - red) * mult);
        }

        /// <summary>레벨/돌파 반영 스탯 [원본 기준 축]. 돌파 단계당 기초 스탯 +8%, 이중 적용 금지.</summary>
        public static float LevelStat(float baseValue, float slope, int level, int breakthrough, float btBonus)
        {
            return baseValue * (1f + slope * (level - 1)) * (1f + btBonus * breakthrough);
        }
    }
}
