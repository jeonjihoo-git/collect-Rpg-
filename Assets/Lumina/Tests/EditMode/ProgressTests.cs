using System;
using System.IO;
using System.Linq;
using Lumina.Core.Combat;
using Lumina.Core.Progress;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>QA 1·4·6: 파티 중복 금지, 행동력 차감 시점, 보상 1회, 최고 별, 저장/복구, 개발 재화 권한.</summary>
    public class ProgressTests
    {
        private static GameSession NewSession(ISaveStore store = null, bool dev = true) =>
            new GameSession(TestData.Db, store ?? new MemorySaveStore(), dev);

        [Test]
        public void NewSave_Owns16TestProxies_ValidPresets_Stamina()
        {
            var s = NewSession();
            Assert.AreEqual(16, s.Save.characters.Count);
            Assert.AreEqual(3, s.Save.party.presets.Count);
            foreach (var p in s.Save.party.presets) Assert.IsNull(s.ValidateParty(p));
            Assert.AreEqual(120, s.Save.inventory.stamina);
            Assert.AreEqual(1, s.Save.account.level);
            Assert.IsEmpty(s.Save.clearRecords);
        }

        [Test]
        public void Party_Rejects_DuplicateOrIncompleteOrUnowned()
        {
            var s = NewSession();
            Assert.IsNotNull(s.ValidateParty(new[] { "tp_longsword", "tp_longsword", "tp_spear", "tp_staff" }));
            Assert.IsNotNull(s.ValidateParty(new[] { "tp_longsword", "tp_spear", "tp_staff" }));
            Assert.IsNotNull(s.ValidateParty(new[] { "tp_longsword", "tp_spear", "tp_staff", "unknown" }));
            s.Save.characters.RemoveAll(c => c.characterId == "tp_axe");
            Assert.IsNotNull(s.ValidateParty(new[] { "tp_longsword", "tp_spear", "tp_staff", "tp_axe" }));
        }

        [Test]
        public void SetPartySlot_SameCharacter_SwapsInsteadOfDuplicate()
        {
            var s = NewSession();
            var p = s.Save.party.Current;
            string a = p[0], b = p[2];
            Assert.IsTrue(s.SetPartySlot(0, b));
            Assert.AreEqual(b, p[0]);
            Assert.AreEqual(a, p[2]);
            Assert.AreEqual(4, p.Distinct().Count());
            Assert.IsTrue(s.SetPartySlot(1, "tp_axe"));
            Assert.AreEqual("tp_axe", p[1]);
            Assert.IsNull(s.ValidateParty(p));
        }

        [Test]
        public void StartBattle_DeductsStaminaOnlyOnSuccess()
        {
            var s = NewSession();
            Assert.AreEqual(StartBattleError.StageLocked, s.TryStartBattle("vs_2", false).error);
            Assert.AreEqual(120, s.Save.inventory.stamina);
            s.Save.inventory.stamina = 5;
            var r = s.TryStartBattle("vs_1", false);
            Assert.AreEqual(StartBattleError.NotEnoughStamina, r.error);
            Assert.AreEqual(5, s.Save.inventory.stamina, "부족 시 부분 차감 없음");
            Assert.IsNull(s.Save.pending);
            s.Save.inventory.stamina = 120;
            r = s.TryStartBattle("vs_1", false);
            Assert.IsTrue(r.Ok);
            Assert.AreEqual(110, s.Save.inventory.stamina);
            Assert.AreEqual(4, r.setup.party.Count);
        }

        [Test]
        public void FirstClearReward_Once_ThenRepeatReward()
        {
            var s = NewSession();
            var stage = TestData.Db.GetStage("vs_1");
            var r1 = s.TryStartBattle("vs_1", false);
            var rc1 = s.CompleteBattle(r1.battleId, BattleOutcome.Victory, true, 50f);
            Assert.IsTrue(rc1.firstClear);
            Assert.AreEqual(stage.firstClearReward.crystals, s.Save.inventory.crystals);

            var r2 = s.TryStartBattle("vs_1", false);
            var rc2 = s.CompleteBattle(r2.battleId, BattleOutcome.Victory, true, 50f);
            Assert.IsFalse(rc2.firstClear);
            Assert.AreEqual(stage.firstClearReward.crystals + stage.repeatReward.crystals, s.Save.inventory.crystals);
            Assert.AreEqual(2, s.GetClearRecord("vs_1").clearCount);
        }

        [Test]
        public void CompleteBattle_SameBattleId_Idempotent_NoDoubleGrant()
        {
            var s = NewSession();
            var r = s.TryStartBattle("vs_1", false);
            var a = s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 50f);
            int crystals = s.Save.inventory.crystals, gold = s.Save.inventory.gold, exp = s.Save.account.exp;
            var b = s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 50f);
            var c = s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 50f);
            Assert.AreSame(a, b);
            Assert.AreSame(a, c);
            Assert.AreEqual(crystals, s.Save.inventory.crystals);
            Assert.AreEqual(gold, s.Save.inventory.gold);
            Assert.AreEqual(exp, s.Save.account.exp);
            Assert.AreEqual(1, s.GetClearRecord("vs_1").clearCount);
            Assert.IsNull(s.CompleteBattle("b999999", BattleOutcome.Victory, true, 1f), "시작하지 않은 전투는 보상 불가");
        }

        [Test]
        public void Defeat_NoReward_NoClearRecord()
        {
            var s = NewSession();
            int crystals = s.Save.inventory.crystals;
            var r = s.TryStartBattle("vs_1", false);
            var rc = s.CompleteBattle(r.battleId, BattleOutcome.DefeatAllDown, false, 30f);
            Assert.IsFalse(rc.victory);
            Assert.AreEqual(0, rc.stars);
            Assert.AreEqual(crystals, s.Save.inventory.crystals);
            Assert.IsFalse(s.GetClearRecord("vs_1").isCleared);
        }

        [Test]
        public void Stars_WinAllAlive90s_BestStarsNeverDecrease()
        {
            var s = NewSession();
            var r = s.TryStartBattle("vs_1", false);
            Assert.AreEqual(3, s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 89f).stars);
            r = s.TryStartBattle("vs_1", false);
            var rc = s.CompleteBattle(r.battleId, BattleOutcome.Victory, false, 120f);
            Assert.AreEqual(1, rc.stars);
            CollectionAssert.AreEqual(new[] { true, false, false }, rc.starFlags);
            Assert.AreEqual(3, s.GetClearRecord("vs_1").bestStars, "최고 별은 하락하지 않음");
        }

        [Test]
        public void AccountExp_1000PerLevel_CarryOver()
        {
            var s = NewSession();
            s.Save.account.exp = 900;
            var r = s.TryStartBattle("vs_1", false);
            var rc = s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 50f);
            Assert.AreEqual(2, s.Save.account.level);
            Assert.AreEqual(50, s.Save.account.exp);
            Assert.AreEqual(1, rc.accountLevelBefore);
            Assert.AreEqual(2, rc.accountLevelAfter);
        }

        [Test]
        public void FileSave_Roundtrip_RestoresClearRecordPartyInventory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lumina_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSaveStore(dir);
                var s = NewSession(store);
                s.SetPartySlot(1, "tp_axe");
                s.SelectPreset(0);
                var r = s.TryStartBattle("vs_1", false);
                s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 70f);
                s.SetAutoPreference("vs_1", true);

                var s2 = NewSession(new FileSaveStore(dir));
                Assert.IsTrue(s2.GetClearRecord("vs_1").isCleared);
                Assert.AreEqual(3, s2.GetClearRecord("vs_1").bestStars);
                Assert.AreEqual("tp_axe", s2.Save.party.Current[1]);
                Assert.AreEqual(s.Save.inventory.crystals, s2.Save.inventory.crystals);
                Assert.AreEqual(s.Save.inventory.stamina, s2.Save.inventory.stamina);
                Assert.IsTrue(s2.GetAutoPreference("vs_1"));
                Assert.IsTrue(s2.Save.receipts.ContainsKey(r.battleId));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void FileSave_CorruptMain_RecoversFromBackup()
        {
            string dir = Path.Combine(Path.GetTempPath(), "lumina_test_" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new FileSaveStore(dir);
                var s = NewSession(store);
                s.DevGrant(DevGrantKind.Gold, 123);
                int goldAfterFirst = s.Save.inventory.gold;
                s.DevGrant(DevGrantKind.Gold, 1);
                File.WriteAllText(store.PathMain, "{ broken json");
                var loaded = new FileSaveStore(dir).Load();
                Assert.IsNotNull(loaded, "손상 시 .bak 복구");
                Assert.AreEqual(goldAfterFirst, loaded.inventory.gold);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [Test]
        public void ForceQuit_DuringBattle_PendingVoided_NoReward_NoRefund()
        {
            var store = new MemorySaveStore();
            var s = NewSession(store);
            var r = s.TryStartBattle("vs_1", false);
            int stamina = s.Save.inventory.stamina;
            int crystals = s.Save.inventory.crystals;

            var s2 = NewSession(store); // 강제 종료 후 재실행
            Assert.IsNull(s2.Save.pending);
            Assert.IsNotNull(s2.RecoveryNotice);
            Assert.AreEqual(stamina, s2.Save.inventory.stamina);
            var rc = s2.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 10f);
            Assert.IsFalse(rc.victory, "무효 처리된 전투로 보상 받을 수 없음");
            Assert.AreEqual(crystals, s2.Save.inventory.crystals);
            Assert.IsFalse(s2.GetClearRecord("vs_1").isCleared);
        }

        [Test]
        public void DevGrant_DisabledInReleaseMode()
        {
            var rel = NewSession(dev: false);
            int g = rel.Save.inventory.gold;
            Assert.IsFalse(rel.DevGrant(DevGrantKind.Gold, 1000));
            Assert.AreEqual(g, rel.Save.inventory.gold);
            var dev = NewSession(dev: true);
            Assert.IsTrue(dev.DevGrant(DevGrantKind.Stamina, 60));
            Assert.AreEqual(180, dev.Save.inventory.stamina);
            Assert.IsFalse(dev.DevGrant(DevGrantKind.Gold, -5), "음수 지급 불가");
        }

        [Test]
        public void StageUnlock_Chain()
        {
            var s = NewSession();
            Assert.IsTrue(s.IsStageUnlocked("vs_1"));
            Assert.IsFalse(s.IsStageUnlocked("vs_2"));
            StringAssert.Contains("클리어 후 개방", s.LockReason("vs_2"));
            var r = s.TryStartBattle("vs_1", false);
            s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 50f);
            Assert.IsTrue(s.IsStageUnlocked("vs_2"));
            Assert.IsFalse(s.IsStageUnlocked("vs_3"));
        }
    }
}
