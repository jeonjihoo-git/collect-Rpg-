using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>QA 2(전투 손맛 규칙) — 공명/쿨타임/태그/회피/브레이크/승패/명중 동기화.</summary>
    public class BattleSimTests
    {
        private static GameDatabase Db => TestData.DummyDb;

        private static PlayerInput Attack => new PlayerInput { tagTo = -1, attackPressed = true };
        private static PlayerInput Skill => new PlayerInput { tagTo = -1, skillPressed = true };
        private static PlayerInput Ult => new PlayerInput { tagTo = -1, ultimatePressed = true };
        private static PlayerInput Dodge => new PlayerInput { tagTo = -1, dodgePressed = true };
        private static PlayerInput Tag(int i) => new PlayerInput { tagTo = i };

        private static bool RunUntil(BattleSim sim, Func<bool> cond, float maxSeconds, PlayerInput? each = null)
        {
            int n = (int)(maxSeconds * BattleSim.TicksPerSecond);
            for (int i = 0; i < n; i++)
            {
                if (cond()) return true;
                sim.Step(each ?? PlayerInput.None);
            }
            return cond();
        }

        private static void WaitActionEnd(BattleSim sim) => RunUntil(sim, () => sim.Body.action == ActionKind.None, 3f);

        [Test]
        public void NormalHit_GrantsResonance4_OncePerAction()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_dualblades" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            WaitActionEnd(sim);
            Assert.AreEqual(2, TestData.EventsOf(sim, CombatEventType.Hit).Count, "쌍검 1타는 2히트");
            Assert.AreEqual(4f, sim.Resonance, 1e-4f, "다단히트여도 행동 1회당 +4");
        }

        [Test]
        public void Whiff_GrantsNoResonance()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            sim.Step(Attack); // 적이 아직 등장 유예 중 → 빗나감
            WaitActionEnd(sim);
            Assert.AreEqual(0f, sim.Resonance, 1e-4f);
        }

        [Test]
        public void Skill_GrantsResonance10_AndCooldown6()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_greatsword" });
            TestData.WaitSpawn(sim);
            sim.Step(Skill);
            Assert.AreEqual(6f, sim.Active.skillCd, 0.02f);
            WaitActionEnd(sim);
            Assert.AreEqual(10f, sim.Resonance, 1e-4f);
            int starts = TestData.EventsOf(sim, CombatEventType.ActionStart).Count(e => e.action == ActionKind.Skill);
            sim.Step(Skill);
            WaitActionEnd(sim);
            Assert.AreEqual(starts, TestData.EventsOf(sim, CombatEventType.ActionStart).Count(e => e.action == ActionKind.Skill), "쿨타임 중 스킬 불가");
        }

        [Test]
        public void Tag_GrantsResonance3_Cooldown1s_HalvesNormalCooldown()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword", "tp_spear" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            float before = sim.NormalCd;
            Assert.Greater(before, 0f);
            sim.Step(Tag(1));
            Assert.AreEqual(1, sim.ActiveIndex);
            Assert.AreEqual(3f, sim.Resonance, 1e-4f);
            Assert.AreEqual((before - BattleSim.Dt) * 0.5f, sim.NormalCd, 1e-3f, "태그 시 남은 일반공격 쿨타임 50% 감소");
            Assert.AreEqual(1f, sim.TagCd, 1e-4f);
            sim.Step(Tag(0));
            Assert.AreEqual(1, sim.ActiveIndex, "태그 쿨타임 1초 중 재태그 불가");
            TestData.Idle(sim, 1.05f);
            sim.Step(Tag(0));
            Assert.AreEqual(0, sim.ActiveIndex);
        }

        [Test]
        public void SkillCooldown_IsPerCharacter_AndPersistsThroughTag()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword", "tp_spear" });
            TestData.WaitSpawn(sim);
            sim.Step(Skill);
            WaitActionEnd(sim);
            sim.Step(Tag(1));
            Assert.AreEqual(0f, sim.Active.skillCd, 1e-4f, "들어온 캐릭터의 스킬은 별도 쿨타임");
            TestData.Idle(sim, 1.05f);
            sim.Step(Tag(0));
            Assert.Greater(sim.Party[0].skillCd, 3f, "교대 후에도 스킬 재사용 6초 유지");
        }

        [Test]
        public void Ultimate_Requires100_AndConsumes100()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_dagger" });
            TestData.WaitSpawn(sim);
            sim.Step(Ult);
            Assert.IsFalse(TestData.EventsOf(sim, CombatEventType.ActionStart).Any(e => e.action == ActionKind.Ultimate), "공명 100 미만 궁극기 불가");
            var hold = new PlayerInput { tagTo = -1, attackHeld = true };
            Assert.IsTrue(RunUntil(sim, () => sim.Resonance >= 100f, 30f, hold), "일반공격으로 공명 100 도달");
            Assert.IsTrue(TestData.EventsOf(sim, CombatEventType.ResonanceFull).Count == 1);
            WaitActionEnd(sim);
            float r = sim.Resonance;
            sim.Step(Ult);
            Assert.AreEqual(r - 100f, sim.Resonance, 1e-4f);
            Assert.IsTrue(TestData.EventsOf(sim, CombatEventType.ActionStart).Any(e => e.action == ActionKind.Ultimate));
        }

        [Test]
        public void PerfectDodge_InvulnerableOverlap_Grants18_NoDamage()
        {
            var sim = TestData.Sim(Db, "t_striker", new[] { "tp_longsword" });
            var striker = sim.Enemies[0];
            Assert.IsTrue(RunUntil(sim, () => striker.ai == EnemyAi.Telegraph && striker.aiTimer <= 0.04f, 10f));
            float hp = sim.Active.hp;
            sim.Step(Dodge);
            TestData.Idle(sim, 0.3f);
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.PerfectDodge).Count);
            Assert.AreEqual(18f, sim.Resonance, 1e-4f);
            Assert.AreEqual(hp, sim.Active.hp, 1e-4f, "완벽 회피 시 피해 없음");
            Assert.AreEqual(0, TestData.EventsOf(sim, CombatEventType.PlayerHit).Count);
        }

        [Test]
        public void NoDodge_TakesHit_AndHitStun()
        {
            var sim = TestData.Sim(Db, "t_striker", new[] { "tp_longsword" });
            Assert.IsTrue(RunUntil(sim, () => TestData.EventsOf(sim, CombatEventType.PlayerHit).Count > 0, 10f));
            Assert.Less(sim.Active.hp, sim.Active.maxHp);
            Assert.AreEqual(ActionKind.HitStun, sim.Body.action);
        }

        [Test]
        public void DodgeInvulnerability_Lasts065s_Cooldown25s()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            sim.Step(Dodge);
            Assert.AreEqual(0.65f, sim.Body.invulnTimer, 1e-4f);
            Assert.AreEqual(2.5f, sim.DodgeCd, 1e-4f);
            TestData.Idle(sim, 0.66f);
            Assert.AreEqual(0f, sim.Body.invulnTimer, 1e-4f);
            int dodges = TestData.EventsOf(sim, CombatEventType.Dodge).Count;
            sim.Step(Dodge);
            Assert.AreEqual(dodges, TestData.EventsOf(sim, CombatEventType.Dodge).Count, "쿨타임 중 회피 불가");
        }

        [Test]
        public void ActiveKO_AutoSwaps_CannotTagToKO_AllKO_Defeat()
        {
            var sim = TestData.Sim(Db, "t_striker", new[] { "tp_longsword", "tp_spear" });
            sim.Party[0].hp = 1f;
            Assert.IsTrue(RunUntil(sim, () => sim.Party[0].hp <= 0f, 10f));
            Assert.AreEqual(1, sim.ActiveIndex, "조작 캐릭터 쓰러지면 생존자로 자동 교대");
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.AutoSwap).Count);
            TestData.Idle(sim, 1.1f);
            sim.Step(Tag(0));
            Assert.AreEqual(1, sim.ActiveIndex, "KO 캐릭터로 태그 불가");
            sim.Party[1].hp = 1f;
            Assert.IsTrue(RunUntil(sim, () => sim.Outcome != BattleOutcome.InProgress, 10f));
            Assert.AreEqual(BattleOutcome.DefeatAllDown, sim.Outcome);
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.BattleEnd).Count);
        }

        [Test]
        public void TimeLimit180_Defeat()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            Assert.IsTrue(RunUntil(sim, () => sim.Outcome != BattleOutcome.InProgress, 181f));
            Assert.AreEqual(BattleOutcome.DefeatTimeUp, sim.Outcome);
            Assert.AreEqual(180f, sim.Time, 0.02f);
        }

        [Test]
        public void Victory_ProcessedOnce_ThenSimFrozen()
        {
            var sim = TestData.Sim(Db, "t_weak", new[] { "tp_longsword" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            Assert.IsTrue(RunUntil(sim, () => sim.Outcome != BattleOutcome.InProgress, 3f));
            Assert.AreEqual(BattleOutcome.Victory, sim.Outcome);
            int tick = sim.Tick;
            TestData.Idle(sim, 1f);
            Assert.AreEqual(tick, sim.Tick);
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.BattleEnd).Count);
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.EnemyDeath).Count);
        }

        [Test]
        public void Break_Stagger5s_Damage15x_ThenGaugeRestores()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_greatsword" });
            TestData.WaitSpawn(sim);
            var e = sim.Enemies[0];
            Assert.AreEqual(100f, e.breakMax, "일반 적 브레이크 100");
            var hold = new PlayerInput { tagTo = -1, attackHeld = true };
            Assert.IsTrue(RunUntil(sim, () => e.Staggered, 20f, hold));
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.BreakStart).Count);
            Assert.AreEqual(5f, e.staggerTimer, 0.05f);
            WaitActionEnd(sim);
            // 넉백으로 밀려난 더미에게 다시 접근
            RunUntil(sim, () => (e.pos - sim.Body.pos).Length < 2.2f, 3f);
            for (int i = 0; i < 180 && (e.pos - sim.Body.pos).Length >= 2.2f; i++)
                sim.Step(new PlayerInput { tagTo = -1, move = (e.pos - sim.Body.pos).Normalized });

            int before = sim.Log.Count;
            sim.Step(Attack);
            RunUntil(sim, () => sim.Log.Skip(before).Any(x => x.type == CombatEventType.Hit), 2f);
            var hit = sim.Log.Skip(before).First(x => x.type == CombatEventType.Hit);
            var m = sim.Active;
            var step = m.weapon.combo[hit.step];
            float expected = DamageCalc.Compute(sim.Prototype, new DamageCalc.Input
            {
                attack = m.atk, coef = step.coef, skillLevelMult = 1f, targetDefense = e.def.physDef, targetLevel = e.def.level,
                staggerMult = 1.5f, crit = hit.crit, critDmg = m.critDmg
            });
            Assert.AreEqual(expected, hit.amount, expected * 1e-4f, "스태거 중 받는 피해 ×1.5");

            Assert.IsTrue(RunUntil(sim, () => !e.Staggered, 6f));
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.BreakEnd).Count);
            Assert.AreEqual(100f, e.breakGauge, 1e-3f);
        }

        [Test]
        public void BreakGauge_RegeneratesWhenNotHit()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            WaitActionEnd(sim);
            var e = sim.Enemies[0];
            float g = e.breakGauge;
            Assert.Less(g, 100f);
            TestData.Idle(sim, 2f);
            Assert.Greater(e.breakGauge, g, "미피격 시 브레이크 회복(초당 3)");
        }

        /// <summary>
        /// 16개 무기 전부: 피해 적용 틱 = Hit 이벤트 틱(타격음·피격 반응 트리거), 근접은 선딜 틱에 명중,
        /// 휘두름 큐는 명중보다 SwingLead 만큼 앞섬, 원거리는 발사→비행→명중 순서.
        /// </summary>
        [Test]
        public void HitFrame_Sync_AllSixteenWeapons()
        {
            foreach (var c in Db.Characters)
            {
                var sim = TestData.Sim(Db, "t_dummy", new[] { c.id });
                TestData.WaitSpawn(sim);
                var e = sim.Enemies[0];
                int startTick = sim.Tick + 1;
                bool first = true;
                for (int i = 0; i < 150; i++)
                {
                    float hpBefore = e.hp;
                    sim.Step(first ? Attack : PlayerInput.None);
                    first = false;
                    float hitSum = sim.Events.Where(x => x.type == CombatEventType.Hit && x.enemy == e.id).Sum(x => x.amount);
                    Assert.AreEqual(hpBefore - e.hp, hitSum, 0.1f, $"{c.weapon}: 피해 적용과 Hit 이벤트가 같은 틱이어야 함 (tick {sim.Tick})");
                }
                var log = sim.Log;
                var start = log.First(x => x.type == CombatEventType.ActionStart);
                Assert.AreEqual(startTick, start.tick, c.weapon.ToString());
                var hits = log.Where(x => x.type == CombatEventType.Hit).ToList();
                Assert.IsNotEmpty(hits, c.weapon + ": 정면 근거리 더미에 명중해야 함");
                var step = Db.GetWeapon(c.weapon).combo[0];
                int expectedOffset = TicksFor(step.startup);
                var cat = WeaponInfo.Category(c.weapon);
                if (cat == WeaponCategory.Melee)
                {
                    Assert.AreEqual(start.tick + expectedOffset, hits[0].tick, c.weapon + ": 근접 명중 틱 = 시작 + 선딜");
                    var swing = log.First(x => x.type == CombatEventType.Swing);
                    float leadTicks = SfxTiming.SwingLead(c.weapon) * BattleSim.TicksPerSecond;
                    int gap = hits[0].tick - swing.tick;
                    Assert.LessOrEqual(Math.Abs(gap - Math.Min(leadTicks, expectedOffset)), 1.01f, c.weapon + ": 휘두름 피크가 명중 프레임에 맞아야 함");
                }
                else
                {
                    var release = log.First(x => x.type == CombatEventType.ProjectileLaunch || x.type == CombatEventType.AreaSpawn || x.type == CombatEventType.RingSpawn);
                    Assert.AreEqual(start.tick + expectedOffset, release.tick, c.weapon + ": 발사/발동 틱 = 시작 + 선딜");
                    Assert.Greater(hits[0].tick, release.tick, c.weapon + ": 명중은 발사 이후(비행/지연)");
                    Assert.IsTrue(log.Any(x => x.type == CombatEventType.Cast && x.tick == start.tick), c.weapon + ": 캐스팅/장전 큐");
                    if (release.type == CombatEventType.AreaSpawn)
                        Assert.LessOrEqual(Math.Abs(hits[0].tick - release.tick - TicksFor(step.delay)), 1, c.weapon + ": 장판 지연 후 명중");
                }
                foreach (var h in hits)
                {
                    Assert.AreEqual(c.weapon, h.weapon);
                    Assert.Greater(h.amount, 0f);
                }
            }
        }

        private static int TicksFor(float seconds)
        {
            int k = 0;
            while (k * BattleSim.Dt + 1e-5f < seconds) k++;
            return k;
        }

        [Test]
        public void Determinism_SameSeed_SameEvents()
        {
            string Run()
            {
                var sim = TestData.Sim(TestData.Db, "vs_2", TestPresets.Default, true, 77);
                var ai = new AutoBattleController();
                for (int i = 0; i < 60 * 40 && sim.Outcome == BattleOutcome.InProgress; i++) sim.Step(ai.Decide(sim), true);
                return string.Join("|", sim.Log.Select(x => $"{x.tick}:{x.type}:{x.amount:0.###}"));
            }
            Assert.AreEqual(Run(), Run());
        }

        // ───── 속성 반응 ─────

        private static BattleSim ReactionSim(string first, string second)
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { first, second });
            TestData.WaitSpawn(sim);
            sim.Step(Skill);
            WaitActionEnd(sim);
            TestData.Idle(sim, 0.4f);
            sim.Step(Tag(1));
            sim.Step(Skill);
            WaitActionEnd(sim);
            TestData.Idle(sim, 0.6f);
            return sim;
        }

        [Test]
        public void Reaction_WindThenLightning_Thunderstorm_AppliesMultiplier()
        {
            var sim = ReactionSim("tp_longsword", "tp_scroll");
            var reactions = TestData.EventsOf(sim, CombatEventType.Reaction);
            Assert.AreEqual(1, reactions.Count);
            Assert.AreEqual("뇌폭풍", reactions[0].label);
            var hit = sim.Log.First(x => x.type == CombatEventType.Hit && x.label == "뇌폭풍");
            var m = sim.Party[1];
            var e = sim.Enemies[0];
            float expected = DamageCalc.Compute(sim.Prototype, new DamageCalc.Input
            {
                attack = m.atk * (1f + 0.2f), coef = m.def.skill.coefTotal / m.weapon.skill.SafeHits, skillLevelMult = 1f,
                targetDefense = e.def.magDef, targetLevel = e.def.level, reactionMult = 1.8f, crit = hit.crit, critDmg = m.critDmg
            });
            Assert.AreEqual(expected, hit.amount, expected * 1e-3f, "반응 배율 1.8 + 버퍼 스킬 공격 +20% 적용");
            Assert.AreEqual(Element.None, e.aura, "반응 후 부착 해제");
        }

        [Test]
        public void Reaction_RockIce_CreatesShield_NoDamageMultiplier()
        {
            var sim = ReactionSim("tp_greatsword", "tp_scythe");
            var reactions = TestData.EventsOf(sim, CombatEventType.Reaction);
            Assert.AreEqual(1, reactions.Count);
            Assert.AreEqual("결빙 방벽", reactions[0].label);
            Assert.AreEqual(1, TestData.EventsOf(sim, CombatEventType.ShieldGain).Count);
            Assert.Greater(sim.Shield, 0f);
            var hit = sim.Log.First(x => x.type == CombatEventType.Hit && x.label == "결빙 방벽");
            var m = sim.Party[1];
            var e = sim.Enemies[0];
            float expected = DamageCalc.Compute(sim.Prototype, new DamageCalc.Input
            {
                attack = m.atk, coef = m.def.skill.coefTotal / m.weapon.skill.SafeHits, skillLevelMult = 1f,
                targetDefense = e.def.physDef, targetLevel = e.def.level, reactionMult = 1f, crit = hit.crit, critDmg = m.critDmg
            });
            Assert.AreEqual(expected, hit.amount, expected * 1e-3f, "결빙 방벽은 피해 배율 대신 보호막");
        }

        [Test]
        public void SameElement_NoReaction()
        {
            var sim = ReactionSim("tp_spear", "tp_staff");
            Assert.AreEqual(0, TestData.EventsOf(sim, CombatEventType.Reaction).Count);
            Assert.AreEqual(Element.Water, sim.Enemies[0].aura);
        }

        [Test]
        public void Aura_ExpiresAfter5s_NoReaction()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword", "tp_scroll" });
            TestData.WaitSpawn(sim);
            sim.Step(Skill);
            WaitActionEnd(sim);
            TestData.Idle(sim, 5.2f);
            Assert.AreEqual(Element.None, sim.Enemies[0].aura);
            sim.Step(Tag(1));
            sim.Step(Skill);
            WaitActionEnd(sim);
            TestData.Idle(sim, 0.6f);
            Assert.AreEqual(0, TestData.EventsOf(sim, CombatEventType.Reaction).Count);
        }

        [Test]
        public void NormalAttack_DoesNotApplyElement()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            WaitActionEnd(sim);
            Assert.Greater(TestData.EventsOf(sim, CombatEventType.Hit).Count, 0);
            Assert.AreEqual(Element.None, sim.Enemies[0].aura);
        }

        [Test]
        public void InputBuffer_PressDuringAction_ExecutesAfterChain()
        {
            var sim = TestData.Sim(Db, "t_dummy", new[] { "tp_longsword" });
            TestData.WaitSpawn(sim);
            sim.Step(Attack);
            TestData.Idle(sim, 0.1f);
            sim.Step(Attack); // 선딜 중 입력 → 버퍼 → 연결 시점에 2타 실행
            TestData.Idle(sim, 0.6f);
            var starts = TestData.EventsOf(sim, CombatEventType.ActionStart).Where(x => x.action == ActionKind.Normal).ToList();
            Assert.AreEqual(2, starts.Count);
            Assert.AreEqual(1, starts[1].step, "콤보 2타");
        }
    }

    public static class TestPresets
    {
        public static readonly string[] Default = { "tp_longsword", "tp_scroll", "tp_spear", "tp_staff" };
    }
}
