using System.Collections.Generic;
using Lumina.Core.Data;
using Lumina.Core.Util;

namespace Lumina.Core.Combat
{
    /// <summary>전투 진입 시 고정되는 파티원 스냅샷(전투 중 인벤토리 변경과 무관).</summary>
    public sealed class PartyMemberSnapshot
    {
        public string characterId;
        public int level = 1;
        public int breakthrough;
        public int normalLevel = 1;
        public int skillLevel = 1;
        public int ultimateLevel = 1;
    }

    public sealed class BattleSetup
    {
        public StageDef stage;
        public List<PartyMemberSnapshot> party = new List<PartyMemberSnapshot>();
        public bool autoBattle;
        public ulong seed = 12345;
        public bool keepLog;
    }

    public sealed class MemberState
    {
        public int index;
        public CharacterDef def;
        public WeaponTypeDef weapon;
        public int level;
        public float maxHp;
        public float hp;
        public float atk;
        public float defense;
        public float critRate;
        public float critDmg;
        public float moveSpeed;
        public float normalMult;
        public float skillMult;
        public float ultimateMult;
        public float skillCd;
        public bool Alive => hp > 0f;
    }

    /// <summary>필드에 나와 있는 조작 캐릭터의 몸체(태그 시 위치 유지).</summary>
    public sealed class PlayerBody
    {
        public Vec2 pos;
        public Vec2 prevPos;
        public Vec2 facing = Vec2.Forward;
        public ActionKind action = ActionKind.None;
        public ActionInstance current;
        public int actionTicks;
        public float actionTime;
        public float actionDuration;
        public int comboIndex;
        public float sinceComboChain = 99f;
        public Vec2 dodgeDir;
        public float invulnTimer;
        public bool perfectGrantedThisDodge;
        public int lockTarget = -1;
    }

    /// <summary>한 번의 공격 행동(일반 1타/스킬/궁극기). 투사체·장판·링이 이 객체를 공유해 공명 1회 지급을 보장.</summary>
    public sealed class ActionInstance
    {
        public int id;
        public ActionKind kind;
        public int member;
        public int step;
        public AttackStepDef def;
        public SkillDef skill;
        public WeaponType weapon;
        public bool resonanceGranted;
        public bool effectApplied;
        public int hitsResolved;
        public int swingsCued;
        public bool isAuto;
        public SfxTier Tier => kind == ActionKind.Skill ? SfxTier.Skill : kind == ActionKind.Ultimate ? SfxTier.Ultimate : def.sfxTier;
    }

    public enum EnemyAi
    {
        Spawning,
        Chase,
        Telegraph,
        Recover,
        Dead
    }

    public sealed class EnemyState
    {
        public int id;
        public EnemyDef def;
        public Vec2 pos;
        public Vec2 prevPos;
        public Vec2 facing = new Vec2(0f, -1f);
        public float maxHp;
        public float hp;
        public float breakMax;
        public float breakGauge;
        public float staggerTimer;
        public Element aura;
        public float auraTimer;
        public float lastHitTime = -99f;
        public EnemyAi ai = EnemyAi.Spawning;
        public float aiTimer;
        public float aiDuration;
        public EnemyAttackDef attack;
        public Vec2 lockedPos;
        public Vec2 lockedDir;
        public Vec2 dashFrom;
        public Vec2 dashTo;
        public float[] attackCds;
        public bool enraged;
        public bool Alive => ai != EnemyAi.Dead;
        public bool Staggered => staggerTimer > 0f;
    }

    public sealed class PlayerProjectile
    {
        public int id;
        public ActionInstance act;
        public int hitIndex;
        public int spawnTick;
        public Vec2 pos;
        public Vec2 dir;
        public float speed;
        public float remaining;
        public float radius;
        public bool pierce;
        public float explodeRadius;
        public readonly HashSet<int> hitSet = new HashSet<int>();
        public bool done;
    }

    public sealed class PendingArea
    {
        public int id;
        public ActionInstance act;
        public int hitIndex;
        public int spawnTick;
        public Vec2 center;
        public float radius;
        public float timer;
        public float delay;
        public bool done;
    }

    public sealed class RingWave
    {
        public int id;
        public ActionInstance act;
        public int spawnTick;
        public Vec2 center;
        public float current;
        public float max;
        public float speed;
        public float thickness;
        public readonly HashSet<int> hitSet = new HashSet<int>();
        public bool done;
    }

    public sealed class EnemyProjectile
    {
        public int id;
        public int owner;
        public EnemyAttackDef attack;
        public Vec2 pos;
        public Vec2 dir;
        public float speed;
        public float remaining;
        public float radius;
        public bool done;
    }
}
