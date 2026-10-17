using System.Collections.Generic;
using Lumina.Core.Data;
using Lumina.Core.Util;

namespace Lumina.Core.Combat
{
    public enum ActionKind
    {
        None,
        Normal,
        Skill,
        Ultimate,
        Dodge,
        HitStun
    }

    public enum BattleOutcome
    {
        InProgress,
        Victory,
        DefeatAllDown,
        DefeatTimeUp,
        Retreat
    }

    /// <summary>
    /// 전투 이벤트. 프레젠테이션(애니메이션/SFX/VFX/카메라)은 이 이벤트만 보고 재생한다.
    /// Hit 이벤트는 피해가 실제 적용된 바로 그 틱에 발생 → 명중 프레임·타격음·피격 반응이 구조적으로 일치.
    /// </summary>
    public enum CombatEventType
    {
        BattleStart,
        WaveStart,
        EnemySpawn,
        ActionStart,
        Swing,
        Cast,
        ProjectileLaunch,
        AreaSpawn,
        RingSpawn,
        Hit,
        ProjectileExpire,
        AreaBurst,
        Reaction,
        ShieldGain,
        BreakStart,
        BreakEnd,
        EnemyDeath,
        EnemyTelegraph,
        EnemyTelegraphCancel,
        EnemyAttack,
        EnemyProjectileLaunch,
        EnemyProjectileExpire,
        PlayerHit,
        ShieldAbsorb,
        PerfectDodge,
        Dodge,
        Tag,
        MemberDown,
        AutoSwap,
        Heal,
        Buff,
        ResonanceFull,
        BattleEnd
    }

    public struct CombatEvent
    {
        public CombatEventType type;
        public int tick;
        public int member;
        public int enemy;
        public int entity;
        public ActionKind action;
        public int step;
        public int hitIndex;
        public WeaponType weapon;
        public SfxTier tier;
        public SwingStyle swing;
        public HitShape hitShape;
        public EnemyAttackShape enemyShape;
        public EnemyMaterial material;
        public Element element;
        public float amount;
        public bool crit;
        public string label;
        public Vec2 pos;
        public Vec2 dir;
        public float radius;
        public float range;
        public float arcDeg;
        public float duration;
        public float hitstop;
        public float shake;
        public bool isAuto;

        public override string ToString() => $"[{tick}] {type} m{member} e{enemy} {label} {amount:0}";
    }

    /// <summary>한 틱 입력. pressed 계열은 이번 틱에 눌림(엣지), held 는 누르고 있음.</summary>
    public struct PlayerInput
    {
        public Vec2 move;
        public bool attackPressed;
        public bool attackHeld;
        public bool skillPressed;
        public bool ultimatePressed;
        public bool dodgePressed;
        /// <summary>-1 = 태그 없음, 0~3 = 파티 슬롯.</summary>
        public int tagTo;

        public static PlayerInput None => new PlayerInput { tagTo = -1 };

        public bool HasAny =>
            move.SqrLength > 0.01f || attackPressed || attackHeld || skillPressed || ultimatePressed || dodgePressed || tagTo >= 0;
    }

    public sealed class BattleStats
    {
        public float totalDamage;
        public int reactions;
        public int breaks;
        public int perfectDodges;
        public int hitsLanded;
        public int hitsTaken;
        public readonly float[] memberDamage = new float[4];
        public readonly Dictionary<ActionKind, int> autoActions = new Dictionary<ActionKind, int>();
        public readonly Dictionary<ActionKind, int> manualActions = new Dictionary<ActionKind, int>();
        public readonly Dictionary<string, int> reactionCounts = new Dictionary<string, int>();
    }
}
