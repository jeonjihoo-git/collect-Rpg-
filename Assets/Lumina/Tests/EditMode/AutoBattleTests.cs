using System.Linq;
using Lumina.Core.Combat;
using Lumina.Core.Progress;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>QA 3(자동전투): ClearRecord 잠금, 편성 화면 정책, 4종 행동 자동 사용, 수동 개입.</summary>
    public class AutoBattleTests
    {
        private static GameSession NewSession() => new GameSession(TestData.Db, new MemorySaveStore(), true);

        [Test]
        public void UnclearedStage_AutoLocked_WithReason()
        {
            var s = NewSession();
            Assert.IsFalse(s.CanUseAutoBattle("vs_1", out string reason));
            StringAssert.Contains("미클리어", reason);
            Assert.IsFalse(s.SetAutoPreference("vs_1", true), "잠긴 스테이지는 ON 저장 불가");
            Assert.IsFalse(s.GetAutoPreference("vs_1"));
        }

        [Test]
        public void StartBattle_WithAutoOnUncleared_Rejected_NoStaminaSpent()
        {
            var s = NewSession();
            int stamina = s.Save.inventory.stamina;
            var r = s.TryStartBattle("vs_1", true);
            Assert.AreEqual(StartBattleError.AutoBattleLocked, r.error);
            Assert.AreEqual(stamina, s.Save.inventory.stamina);
            Assert.IsNull(s.Save.pending);
        }

        [Test]
        public void AfterClear_AutoUnlocked_OnlyForThatStage()
        {
            var s = NewSession();
            var r = s.TryStartBattle("vs_1", false);
            Assert.IsTrue(r.Ok);
            s.CompleteBattle(r.battleId, BattleOutcome.Victory, true, 60f);
            Assert.IsTrue(s.CanUseAutoBattle("vs_1", out _));
            Assert.IsFalse(s.CanUseAutoBattle("vs_2", out _), "다른 스테이지는 여전히 잠금");
            Assert.IsTrue(s.SetAutoPreference("vs_1", true));
            Assert.IsTrue(s.GetAutoPreference("vs_1"));
            var r2 = s.TryStartBattle("vs_1", true);
            Assert.IsTrue(r2.Ok);
            Assert.IsTrue(r2.setup.autoBattle);
        }

        [Test]
        public void Defeat_DoesNotUnlockAuto()
        {
            var s = NewSession();
            var r = s.TryStartBattle("vs_1", false);
            s.CompleteBattle(r.battleId, BattleOutcome.DefeatTimeUp, false, 180f);
            Assert.IsFalse(s.CanUseAutoBattle("vs_1", out _));
        }

        /// <summary>자동전투가 일반공격·스킬·회피·궁극기를 모두 실제로 사용(웹 시제품의 '자동 일반공격만'과 구분).</summary>
        [Test]
        public void AutoController_UsesNormalSkillDodgeUltimate()
        {
            var counts = new System.Collections.Generic.Dictionary<ActionKind, int>();
            foreach (var stage in new[] { "vs_1", "vs_2", "vs_3" })
            {
                var sim = TestData.Sim(TestData.Db, stage, TestPresets.Default, true, 3);
                var ai = new AutoBattleController();
                for (int i = 0; i < 60 * 180 && sim.Outcome == BattleOutcome.InProgress; i++) sim.Step(ai.Decide(sim), true);
                foreach (var kv in sim.Stats.autoActions)
                {
                    counts.TryGetValue(kv.Key, out int n);
                    counts[kv.Key] = n + kv.Value;
                }
                Assert.AreEqual(0, sim.Stats.manualActions.Values.Sum(), "자동 모드 테스트는 수동 입력 없음");
            }
            foreach (var k in new[] { ActionKind.Normal, ActionKind.Skill, ActionKind.Dodge, ActionKind.Ultimate })
                Assert.Greater(counts.TryGetValue(k, out int n) ? n : 0, 0, k + " 자동 사용");
        }

        [Test]
        public void AutoController_ClearsStage1()
        {
            var sim = TestData.Sim(TestData.Db, "vs_1", TestPresets.Default, true, 5);
            var ai = new AutoBattleController();
            for (int i = 0; i < 60 * 180 && sim.Outcome == BattleOutcome.InProgress; i++) sim.Step(ai.Decide(sim), true);
            Assert.AreEqual(BattleOutcome.Victory, sim.Outcome, $"time {sim.Time:0.0}s");
        }

        [Test]
        public void InputMerger_ManualOverridesAuto_ThenResumes()
        {
            var m = new InputMerger();
            var auto = new PlayerInput { tagTo = -1, attackPressed = true };
            var none = PlayerInput.None;
            var manual = new PlayerInput { tagTo = -1, skillPressed = true };
            var r = m.Merge(none, auto, true, BattleSim.Dt, 1f);
            Assert.IsTrue(m.LastWasAuto);
            Assert.IsTrue(r.attackPressed);
            r = m.Merge(manual, auto, true, BattleSim.Dt, 1f);
            Assert.IsFalse(m.LastWasAuto);
            Assert.IsTrue(r.skillPressed);
            for (int i = 0; i < 30; i++) m.Merge(none, auto, true, BattleSim.Dt, 1f);
            Assert.IsFalse(m.LastWasAuto, "수동 입력 후 1초간 자동 정지");
            for (int i = 0; i < 40; i++) m.Merge(none, auto, true, BattleSim.Dt, 1f);
            Assert.IsTrue(m.LastWasAuto, "이후 자동 재개");
            m.Merge(none, auto, false, BattleSim.Dt, 1f);
            Assert.IsFalse(m.LastWasAuto, "자동 OFF 면 항상 수동");
        }
    }
}
