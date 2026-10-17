using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Core.Combat;
using Lumina.Core.Data;

namespace Lumina.Core.Progress
{
    public enum StartBattleError
    {
        None,
        UnknownStage,
        StageLocked,
        PartyInvalid,
        NotEnoughStamina,
        AutoBattleLocked
    }

    public sealed class StartBattleResult
    {
        public StartBattleError error;
        public string message;
        public string battleId;
        public BattleSetup setup;
        public bool Ok => error == StartBattleError.None;
    }

    public enum DevGrantKind
    {
        Stamina,
        Gold,
        Crystals
    }

    /// <summary>별 판정 [웹 시제품 기준]: 승리 / 전원 생존 / 90초 이내 승리.</summary>
    public static class StarRules
    {
        public static bool[] Evaluate(bool victory, bool allAlive, float clearTime, float starTimeLimit)
        {
            return new[] { victory, victory && allAlive, victory && clearTime <= starTimeLimit };
        }

        public static int Count(bool[] flags) => flags.Count(f => f);
    }

    /// <summary>
    /// 계정 진행 파사드. 모든 소비/지급은 '검사 → 한 번에 변경 → 저장' 순서로 처리해 부분 변경을 막는다.
    /// 서버 트랜잭션이 아닌 로컬 저장 기준이며, 서버 원자 거래는 별도 구현 대상(미구현).
    /// </summary>
    public sealed class GameSession
    {
        public GameDatabase Db { get; }
        public SaveData Save { get; private set; }
        public bool DevToolsEnabled { get; }
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;
        /// <summary>로드 시 발견한 미완료 전투(강제 종료) 처리 메시지.</summary>
        public string RecoveryNotice { get; private set; }

        private readonly ISaveStore _store;

        public static readonly string[][] DefaultPresets =
        {
            new[] { "tp_longsword", "tp_scroll", "tp_spear", "tp_staff" },
            new[] { "tp_greatsword", "tp_bow", "tp_gauntlet", "tp_grimoire" },
            new[] { "tp_dagger", "tp_launcher", "tp_scythe", "tp_magicring" }
        };

        public GameSession(GameDatabase db, ISaveStore store, bool devToolsEnabled)
        {
            Db = db;
            _store = store;
            DevToolsEnabled = devToolsEnabled;
            Save = store.Load();
            if (Save == null)
            {
                Save = CreateNewSave(db, Clock());
                Commit();
            }
            else
            {
                RecoverPending();
            }
        }

        public static SaveData CreateNewSave(GameDatabase db, DateTime now)
        {
            var t = db.Balance.temp;
            var s = new SaveData
            {
                createdUtc = now.ToString("o"),
                inventory = new InventoryData { stamina = t.staminaMax, gold = t.startGold, crystals = t.startCrystals, expBooks = t.startExpBooks }
            };
            foreach (var c in db.Characters)
            {
                s.characters.Add(new OwnedCharacter { characterId = c.id });
            }
            s.party.presets = new List<string[]>();
            for (int i = 0; i < PartyPresets.PresetCount; i++)
            {
                var src = i < DefaultPresets.Length ? DefaultPresets[i] : DefaultPresets[0];
                var preset = src.Where(id => db.GetCharacter(id) != null).ToArray();
                if (preset.Length != PartyPresets.Size) preset = db.Characters.Take(PartyPresets.Size).Select(c => c.id).ToArray();
                s.party.presets.Add(preset);
            }
            return s;
        }

        public void Commit()
        {
            Save.savedUtc = Clock().ToString("o");
            _store.Save(Save);
        }

        /// <summary>강제 종료 등으로 남은 진행 중 전투는 무효 처리(보상 없음, 행동력 환불 없음 — 가정 A-15).</summary>
        private void RecoverPending()
        {
            var p = Save.pending;
            if (p == null) return;
            if (!Save.receipts.ContainsKey(p.battleId))
            {
                Save.receipts[p.battleId] = new BattleReceipt
                {
                    battleId = p.battleId,
                    stageId = p.stageId,
                    victory = false,
                    outcome = "Abandoned",
                    accountLevelBefore = Save.account.level,
                    accountLevelAfter = Save.account.level,
                    processedUtc = Clock().ToString("o")
                };
            }
            Save.pending = null;
            RecoveryNotice = "이전 전투가 완료되지 않아 무효 처리되었습니다(보상 없음).";
            Commit();
        }

        // ───────────────────── 조회 ─────────────────────

        public OwnedCharacter GetOwned(string characterId) => Save.characters.FirstOrDefault(c => c.characterId == characterId);

        /// <summary>항상 non-null. 기록이 없으면 미클리어 상태 객체(저장되지 않음).</summary>
        public ClearRecord GetClearRecord(string stageId)
        {
            return Save.clearRecords.TryGetValue(stageId, out var r) ? r : new ClearRecord { stageId = stageId };
        }

        public bool IsStageUnlocked(string stageId)
        {
            var s = Db.GetStage(stageId);
            if (s == null) return false;
            return s.unlockAfter == null || GetClearRecord(s.unlockAfter).isCleared;
        }

        public string LockReason(string stageId)
        {
            var s = Db.GetStage(stageId);
            if (s == null) return "존재하지 않는 스테이지";
            if (IsStageUnlocked(stageId)) return null;
            var prev = Db.GetStage(s.unlockAfter);
            return (prev != null ? prev.displayName : s.unlockAfter) + " 클리어 후 개방";
        }

        /// <summary>자동전투 허용 여부. ClearRecord(stageId).isCleared 가 유일한 판정 원천.</summary>
        public bool CanUseAutoBattle(string stageId, out string reason)
        {
            if (Db.GetStage(stageId) == null)
            {
                reason = "존재하지 않는 스테이지";
                return false;
            }
            if (!GetClearRecord(stageId).isCleared)
            {
                reason = "미클리어 스테이지 — 1회 클리어 후 자동전투 사용 가능";
                return false;
            }
            reason = null;
            return true;
        }

        public bool GetAutoPreference(string stageId)
        {
            return CanUseAutoBattle(stageId, out _) && Save.autoPreference.TryGetValue(stageId, out bool v) && v;
        }

        public bool SetAutoPreference(string stageId, bool on)
        {
            if (on && !CanUseAutoBattle(stageId, out _)) return false;
            Save.autoPreference[stageId] = on;
            Commit();
            return true;
        }

        // ───────────────────── 파티 ─────────────────────

        /// <summary>null 이면 유효. 4인, 중복 없음, 보유 캐릭터만.</summary>
        public string ValidateParty(string[] ids)
        {
            if (ids == null || ids.Length != PartyPresets.Size) return "파티는 4명이어야 합니다.";
            var set = new HashSet<string>();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) return "빈 슬롯이 있습니다.";
                if (!set.Add(id)) return "같은 캐릭터를 두 슬롯에 편성할 수 없습니다.";
                if (Db.GetCharacter(id) == null) return "알 수 없는 캐릭터: " + id;
                if (GetOwned(id) == null) return "보유하지 않은 캐릭터: " + id;
            }
            return null;
        }

        public void SelectPreset(int index)
        {
            if (index < 0 || index >= Save.party.presets.Count) return;
            Save.party.current = index;
            Commit();
        }

        /// <summary>슬롯 지정. 이미 파티에 있는 캐릭터면 두 자리를 교환(중복 슬롯을 만들지 않음).</summary>
        public bool SetPartySlot(int slot, string characterId)
        {
            if (slot < 0 || slot >= PartyPresets.Size) return false;
            if (Db.GetCharacter(characterId) == null || GetOwned(characterId) == null) return false;
            var preset = Save.party.Current;
            int existing = Array.IndexOf(preset, characterId);
            if (existing == slot) return true;
            if (existing >= 0)
            {
                preset[existing] = preset[slot];
                preset[slot] = characterId;
            }
            else
            {
                preset[slot] = characterId;
            }
            Commit();
            return true;
        }

        // ───────────────────── 전투 시작/완료 ─────────────────────

        /// <summary>출전: 스테이지·개방·파티·행동력·자동전투 자격을 모두 검사한 뒤에만 행동력을 차감한다.</summary>
        public StartBattleResult TryStartBattle(string stageId, bool autoBattle)
        {
            var stage = Db.GetStage(stageId);
            if (stage == null) return Fail(StartBattleError.UnknownStage, "존재하지 않는 스테이지");
            if (!IsStageUnlocked(stageId)) return Fail(StartBattleError.StageLocked, LockReason(stageId));
            var party = Save.party.Current;
            string partyErr = ValidateParty(party);
            if (partyErr != null) return Fail(StartBattleError.PartyInvalid, partyErr);
            if (autoBattle && !CanUseAutoBattle(stageId, out string why)) return Fail(StartBattleError.AutoBattleLocked, why);
            if (Save.inventory.stamina < stage.staminaCost)
                return Fail(StartBattleError.NotEnoughStamina, $"행동력 부족 ({Save.inventory.stamina}/{stage.staminaCost})");

            // 이전 미완료 전투가 남아 있으면 무효 처리 후 진행
            if (Save.pending != null) RecoverPending();

            Save.inventory.stamina -= stage.staminaCost;
            Save.battleSeq++;
            string battleId = "b" + Save.battleSeq.ToString("D6");
            ulong seed = (ulong)(Save.battleSeq * 7919 + 17);
            Save.pending = new PendingBattle
            {
                battleId = battleId,
                stageId = stageId,
                party = (string[])party.Clone(),
                autoBattle = autoBattle,
                startedUtc = Clock().ToString("o"),
                seed = seed
            };
            Commit();

            var setup = new BattleSetup { stage = stage, autoBattle = autoBattle, seed = seed };
            foreach (var id in party)
            {
                var o = GetOwned(id);
                setup.party.Add(new PartyMemberSnapshot
                {
                    characterId = id,
                    level = o.level,
                    breakthrough = o.breakthrough,
                    normalLevel = o.normalLevel,
                    skillLevel = o.skillLevel,
                    ultimateLevel = o.ultimateLevel
                });
            }
            return new StartBattleResult { error = StartBattleError.None, battleId = battleId, setup = setup };
        }

        private static StartBattleResult Fail(StartBattleError e, string msg) => new StartBattleResult { error = e, message = msg };

        /// <summary>
        /// 전투 결과 확정. 같은 battleId 는 몇 번 호출해도 첫 처리 결과만 반환(보상 1회).
        /// 결과 화면 진입/재진입은 지급 트리거가 아니다 — 이 메서드가 전투 종료 시 1회 호출된다.
        /// </summary>
        public BattleReceipt CompleteBattle(string battleId, BattleOutcome outcome, bool allAlive, float clearTime)
        {
            if (battleId == null) return null;
            if (Save.receipts.TryGetValue(battleId, out var existing)) return existing;
            if (Save.pending == null || Save.pending.battleId != battleId) return null;

            var stage = Db.GetStage(Save.pending.stageId);
            bool victory = outcome == BattleOutcome.Victory;
            var flags = StarRules.Evaluate(victory, allAlive, clearTime, Db.Balance.prototype.starTimeLimit);
            var receipt = new BattleReceipt
            {
                battleId = battleId,
                stageId = stage.id,
                victory = victory,
                outcome = outcome.ToString(),
                starFlags = flags,
                stars = StarRules.Count(flags),
                clearTime = clearTime,
                accountLevelBefore = Save.account.level,
                processedUtc = Clock().ToString("o")
            };

            if (victory)
            {
                if (!Save.clearRecords.TryGetValue(stage.id, out var rec))
                {
                    rec = new ClearRecord { stageId = stage.id };
                    Save.clearRecords[stage.id] = rec;
                }
                bool first = !rec.firstClearRewardClaimed;
                var reward = first ? stage.firstClearReward : stage.repeatReward;
                receipt.firstClear = first;
                receipt.reward = new RewardDef
                {
                    crystals = reward.crystals,
                    gold = reward.gold,
                    expBooks = reward.expBooks,
                    accountExp = reward.accountExp
                };
                Save.inventory.crystals += reward.crystals;
                Save.inventory.gold += reward.gold;
                Save.inventory.expBooks += reward.expBooks;
                GrantAccountExp(reward.accountExp);

                rec.isCleared = true;
                rec.clearCount++;
                rec.bestStars = Math.Max(rec.bestStars, receipt.stars);
                if (first)
                {
                    rec.firstClearRewardClaimed = true;
                    rec.firstClearUtc = Clock().ToString("o");
                }
            }

            receipt.accountLevelAfter = Save.account.level;
            Save.receipts[battleId] = receipt;
            Save.pending = null;
            Commit();
            return receipt;
        }

        private void GrantAccountExp(int amount)
        {
            int per = Math.Max(1, Db.Balance.prototype.accountExpPerLevel);
            Save.account.exp += amount;
            while (Save.account.exp >= per)
            {
                Save.account.exp -= per;
                Save.account.level++;
            }
        }

        // ───────────────────── 개발용 ─────────────────────

        /// <summary>개발/테스트 빌드 전용 재화 지급. 정식 빌드(DevToolsEnabled=false)에서는 거부.</summary>
        public bool DevGrant(DevGrantKind kind, int amount)
        {
            if (!DevToolsEnabled || amount <= 0) return false;
            switch (kind)
            {
                case DevGrantKind.Stamina:
                    Save.inventory.stamina += amount;
                    break;
                case DevGrantKind.Gold:
                    Save.inventory.gold += amount;
                    break;
                case DevGrantKind.Crystals:
                    Save.inventory.crystals += amount;
                    break;
            }
            Commit();
            return true;
        }

        public void ResetSave()
        {
            _store.Delete();
            Save = CreateNewSave(Db, Clock());
            RecoveryNotice = null;
            Commit();
        }
    }
}
