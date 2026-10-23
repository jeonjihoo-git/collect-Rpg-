using System;
using System.Collections.Generic;

namespace Lumina.Core.Data
{
    // ─────────────────────────────────────────────────────────────
    // 정의(마스터) 데이터. JSON(Resources/LuminaData/*.json)에서 로드한다.
    // 필드명은 JSON 키와 동일(camelCase). 수치의 근거는 status/note 로 남긴다.
    // ─────────────────────────────────────────────────────────────

    /// <summary>타격 판정 형태.</summary>
    public enum HitShape
    {
        Cone,
        Circle,
        Line,
        Projectile,
        TargetArea,
        Ring
    }

    /// <summary>타격 강도 단계. 효과음·히트스톱·카메라 반응의 세기 차이에 사용.</summary>
    public enum SfxTier
    {
        Light,
        Medium,
        Heavy,
        Finisher,
        Skill,
        Ultimate
    }

    /// <summary>프레젠테이션 모션 힌트(코어 판정에는 영향 없음).</summary>
    public enum SwingStyle
    {
        HorizontalLR,
        HorizontalRL,
        Overhead,
        Thrust,
        Punch,
        Uppercut,
        Spin,
        Draw,
        Fire,
        Cast,
        Unfurl,
        RingSpin,
        Slam
    }

    /// <summary>일반공격 1타·스킬·궁극기의 공통 모션/판정 정의.</summary>
    [Serializable]
    public class AttackStepDef
    {
        public string name;
        /// <summary>시작~첫 명중(근접) 또는 발사/발동까지의 시간(초).</summary>
        public float startup;
        public int hits = 1;
        public float hitInterval;
        /// <summary>마지막 명중/발사 후 행동 종료까지(초).</summary>
        public float recovery;
        /// <summary>회복 구간 중 다음 콤보 입력을 받기 시작하는 비율(0~1).</summary>
        public float chainAt = 0.5f;
        /// <summary>일반공격 1타당 계수. 스킬/궁극기는 SkillDef.coefTotal 을 사용.</summary>
        public float coef;
        public HitShape shape;
        public float range;
        public float arcDeg;
        public float radius;
        public float offset;
        public float back;
        public float speed;
        public bool pierce;
        public float explodeRadius;
        public float delay;
        public float spreadDeg;
        public float lunge;
        public float breakDmg;
        public float hitstop;
        public float knockback;
        public float shake;
        public SfxTier sfxTier = SfxTier.Medium;
        public SwingStyle swing;

        public int SafeHits => hits < 1 ? 1 : hits;

        /// <summary>시작부터 마지막 명중/발사 시점까지.</summary>
        public float LastHitTime => startup + (SafeHits - 1) * hitInterval;

        public float TotalDuration => LastHitTime + recovery;

        public float ChainTime => LastHitTime + recovery * chainAt;
    }

    [Serializable]
    public class WeaponTypeDef
    {
        public WeaponType id;
        public string koName;
        public WeaponCategory category;
        public AttackStepDef[] combo;
        public AttackStepDef skill;
        public AttackStepDef ultimate;
        /// <summary>콤보 연결 허용 시간(초). 넘기면 1타로 초기화.</summary>
        public float comboResetTime = 0.8f;
        public DataStatus status;
        public string note;
    }

    public enum SkillEffect
    {
        Damage,
        Heal,
        Buff
    }

    [Serializable]
    public class SkillDef
    {
        public string name;
        /// <summary>전체 계수(다단히트는 분배).</summary>
        public float coefTotal;
        public bool appliesElement = true;
        public SkillEffect effect = SkillEffect.Damage;
        /// <summary>회복량 = 시전자 공격력 × 비율 [TEMP: 독립 회복력 스탯 미확정].</summary>
        public float healAtkRatio;
        public float buffAtkPct;
        public float buffDuration;
        public DataStatus status;
    }

    [Serializable]
    public class CharacterDef
    {
        public string id;
        public string displayName;
        /// <summary>true = 검증용 임시 캐릭터(정식 16인 로스터 아님).</summary>
        public bool isTestProxy;
        public Rarity rarity;
        public Element element;
        public AttackType attackType;
        public Role role;
        public WeaponType weapon;
        public float baseHp;
        public float baseAtk;
        public float baseDef;
        public float critRate;
        public float critDmg;
        public float moveSpeed;
        public SkillDef skill;
        public SkillDef ultimate;
        public DataStatus status;
        public string note;
    }

    /// <summary>
    /// 최신 정식 로스터 16명 [최근 보고]. 확인된 값만 채우고 나머지는 null 유지.
    /// 이 항목은 전투에 직접 사용하지 않는다(CharacterDef 승인 전).
    /// </summary>
    [Serializable]
    public class RosterEntry
    {
        public int no;
        public string name;
        public string generation;
        public WeaponType? confirmedWeapon;
        public string weaponNote;
        public Element? element;
        public Role? role;
        public Rarity? rarity;
        public AttackType? attackType;
        public bool definitionVerified;
        public string note;
    }

    public enum EnemyMaterial
    {
        Flesh,
        Armor,
        Phantom
    }

    public enum EnemyAttackShape
    {
        Cone,
        Circle,
        Line,
        Projectile,
        TargetCircle,
        SelfRing
    }

    [Serializable]
    public class EnemyAttackDef
    {
        public string id;
        public string name;
        public EnemyAttackShape shape;
        public float telegraph;
        public float recovery;
        public float cooldown;
        public float coef;
        public float range;
        public float arcDeg;
        public float radius;
        public float offset;
        public float speed;
        public float minRange;
        public float maxRange = 99f;
        public float weight = 1f;
        /// <summary>돌진형(Line) 공격 시 이동 거리 비율.</summary>
        public bool dash;
    }

    [Serializable]
    public class EnemyDef
    {
        public string id;
        public string displayName;
        public bool isTemp = true;
        public int level;
        public float hp;
        public float atk;
        public float physDef;
        public float magDef;
        public Difficulty breakTier;
        public float moveSpeed;
        public float radius = 0.6f;
        public float preferredRange = 1.6f;
        public EnemyMaterial material;
        public bool isBoss;
        public float enrageHpPct;
        public float enrageTelegraphMult = 1f;
        public EnemyAttackDef[] attacks;
        public DataStatus status;
        public string source;
    }

    [Serializable]
    public class SpawnDef
    {
        public string enemy;
        public float x;
        public float y;
    }

    [Serializable]
    public class WaveDef
    {
        public SpawnDef[] spawns;
    }

    [Serializable]
    public class RewardDef
    {
        public int crystals;
        public int gold;
        public int expBooks;
        public int accountExp;
    }

    [Serializable]
    public class StageDef
    {
        public string id;
        public string displayName;
        public string description;
        public int order;
        /// <summary>선행 클리어 스테이지(null이면 처음부터 개방).</summary>
        public string unlockAfter;
        public int staminaCost;
        public Difficulty difficulty;
        public int recommendedCp;
        public WaveDef[] waves;
        public RewardDef firstClearReward;
        public RewardDef repeatReward;
        public DataStatus status;
        public string note;
    }

    public enum ReactionEffect
    {
        Damage,
        Shield
    }

    [Serializable]
    public class ElementReactionDef
    {
        public Element a;
        public Element b;
        public string name;
        public float multiplier;
        public ReactionEffect effect;
        public DataStatus status;
        public string note;

        public bool Matches(Element x, Element y) => (a == x && b == y) || (a == y && b == x);
    }

    /// <summary>원본/웹 시제품 기준 전투·보상 수치 [Prototype]. 밸런스 승인 전 검증용.</summary>
    [Serializable]
    public class PrototypeBalance
    {
        public float timeLimit = 180f;
        public float starTimeLimit = 90f;
        public int resonanceMax = 100;
        public int ultimateCost = 100;
        public int resonanceNormal = 4;
        public int resonanceSkill = 10;
        public int resonancePerfectDodge = 18;
        public int resonanceTag = 3;
        public float breakGaugeNormal = 100f;
        public float breakGaugeHard = 150f;
        public float breakGaugeHell = 220f;
        public float breakStaggerSeconds = 5f;
        public float breakDamageMultiplier = 1.5f;
        public float breakRegenPerSecond = 3f;
        public float baseCoefNormal = 0.12f;
        public float baseCoefSkill = 0.32f;
        public float baseCoefUltimate = 0.90f;
        public float normalInterval = 0.58f;
        public float skillCooldown = 6f;
        public float tagCooldown = 1f;
        public float dodgeCooldown = 2.5f;
        public float dodgeInvulnerable = 0.65f;
        public float tagNormalCooldownCut = 0.5f;
        public float auraDuration = 5f;
        public float defenseK = 300f;
        public float defenseKPerLevel = 0.02f;
        public float levelAtkSlope = 0.045f;
        public float levelHpSlope = 0.06f;
        public float levelDefSlope = 0.03f;
        public float breakthroughStatBonus = 0.08f;
        public int accountExpPerLevel = 1000;
        public int staminaRefillTest = 120;
        /// <summary>스킬 레벨 배율 기준점(Lv → 배율). 중간 레벨은 선형 보간 [Prototype].</summary>
        public Dictionary<int, float> skillLevelMultipliers = new Dictionary<int, float>();
    }

    /// <summary>이 저장소가 수직 슬라이스 구동을 위해 둔 가정값 [Temp]. 승인 필요(docs/ASSUMPTIONS.md).</summary>
    [Serializable]
    public class TempBalance
    {
        public float dodgeDuration = 0.25f;
        public float dodgeDistance = 3.6f;
        public float breakRegenDelay = 1f;
        public float inputBuffer = 0.2f;
        public float playerHitStun = 0.2f;
        public float autoManualOverride = 1f;
        public float softLockRange = 8f;
        public float arenaRadius = 13f;
        public float playerRadius = 0.45f;
        public float waveDelay = 1.2f;
        public float spawnGrace = 0.9f;
        public float shieldAtkRatio = 1f;
        public float shieldDuration = 10f;
        public float ultimateInvulnerableStartup = 1f;
        public float bossKnockbackScale = 0.15f;
        public int staminaMax = 120;
        public int startGold = 10000;
        public int startCrystals = 0;
        public int startExpBooks = 0;
    }

    [Serializable]
    public class BalanceDef
    {
        public PrototypeBalance prototype = new PrototypeBalance();
        public TempBalance temp = new TempBalance();
        public string note;

        public float BreakGauge(Difficulty d)
        {
            switch (d)
            {
                case Difficulty.Hard: return prototype.breakGaugeHard;
                case Difficulty.Hell: return prototype.breakGaugeHell;
                default: return prototype.breakGaugeNormal;
            }
        }

        /// <summary>스킬 레벨 배율. Lv.1=1.00, 3=1.15, 5=1.35, 7=1.55, 10=1.85 사이 선형 보간 [Prototype].</summary>
        public float SkillLevelMultiplier(int level)
        {
            var table = prototype.skillLevelMultipliers;
            if (table == null || table.Count == 0) return 1f;
            int lo = int.MinValue, hi = int.MaxValue;
            foreach (int k in table.Keys)
            {
                if (k == level) return table[k];
                if (k < level && k > lo) lo = k;
                if (k > level && k < hi) hi = k;
            }
            if (lo == int.MinValue) return table[hi];
            if (hi == int.MaxValue) return table[lo];
            float t = (level - lo) / (float)(hi - lo);
            return table[lo] + (table[hi] - table[lo]) * t;
        }
    }
}
