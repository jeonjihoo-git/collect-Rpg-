using System;
using System.Collections.Generic;
using Lumina.Core.Data;

namespace Lumina.Core.Progress
{
    /// <summary>로컬 저장 데이터 v1. 서버 동기화/계정 인증 보장 아님(로컬 파일).</summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string createdUtc;
        public string savedUtc;
        public AccountData account = new AccountData();
        public InventoryData inventory = new InventoryData();
        public List<OwnedCharacter> characters = new List<OwnedCharacter>();
        public PartyPresets party = new PartyPresets();
        public Dictionary<string, ClearRecord> clearRecords = new Dictionary<string, ClearRecord>();
        /// <summary>battleId → 처리 결과. 같은 battleId 재처리 시 그대로 반환(중복 지급 방지).</summary>
        public Dictionary<string, BattleReceipt> receipts = new Dictionary<string, BattleReceipt>();
        public PendingBattle pending;
        public int battleSeq;
        /// <summary>스테이지별 마지막 자동전투 토글(편성 화면 복원용). 실제 허용 여부는 ClearRecord 로만 판정.</summary>
        public Dictionary<string, bool> autoPreference = new Dictionary<string, bool>();
    }

    [Serializable]
    public sealed class AccountData
    {
        public string name = "개척자";
        public int level = 1;
        public int exp;
    }

    [Serializable]
    public sealed class InventoryData
    {
        public int stamina;
        public int gold;
        /// <summary>별빛 결정(다이아).</summary>
        public int crystals;
        /// <summary>경험의 서.</summary>
        public int expBooks;
    }

    [Serializable]
    public sealed class OwnedCharacter
    {
        public string characterId;
        public int level = 1;
        public int breakthrough;
        /// <summary>돌파용 중복 보유 수(본체 제외).</summary>
        public int copies;
        public int normalLevel = 1;
        public int skillLevel = 1;
        public int ultimateLevel = 1;
    }

    [Serializable]
    public sealed class PartyPresets
    {
        public const int PresetCount = 3;
        public const int Size = 4;
        public int current;
        public List<string[]> presets = new List<string[]>();

        public string[] Current => presets[current];
    }

    /// <summary>스테이지 클리어 기록. 자동전투 잠금과 스테이지 UI 기록의 단일 원천(GDD 09-2).</summary>
    [Serializable]
    public sealed class ClearRecord
    {
        public string stageId;
        public bool isCleared;
        public int bestStars;
        public int clearCount;
        public bool firstClearRewardClaimed;
        public string firstClearUtc;
    }

    [Serializable]
    public sealed class PendingBattle
    {
        public string battleId;
        public string stageId;
        public string[] party;
        public bool autoBattle;
        public string startedUtc;
        public ulong seed;
    }

    [Serializable]
    public sealed class BattleReceipt
    {
        public string battleId;
        public string stageId;
        public bool victory;
        public string outcome;
        public int stars;
        public bool[] starFlags = new bool[3];
        public bool firstClear;
        public RewardDef reward = new RewardDef();
        public int accountLevelBefore;
        public int accountLevelAfter;
        public float clearTime;
        public string processedUtc;
    }
}
