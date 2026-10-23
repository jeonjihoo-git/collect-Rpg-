using System.Linq;
using Lumina.Core.Data;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>QA 1(실행/데이터 정합) + 04_MACHINE_RULES.json 교차 검증.</summary>
    public class DataIntegrityTests
    {
        private static GameDatabase Db => TestData.Db;

        [Test]
        public void Database_Validates_WithoutErrors()
        {
            var errors = Db.Validate();
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void Elements_AreNine_AndMatchMachineRules()
        {
            var rules = TestData.MachineRules["combat"]["elements"].Select(t => (string)t).ToArray();
            var ours = ElementInfo.All.Select(ElementInfo.Ko).ToArray();
            CollectionAssert.AreEqual(rules, ours);
        }

        [Test]
        public void Weapons_AreSixteen_WithCategories_MatchMachineRules()
        {
            var w = TestData.MachineRules["weapons"];
            Assert.AreEqual(16, (int)w["count"]);
            Assert.AreEqual(16, Db.Weapons.Count);
            void Check(string key, WeaponCategory cat)
            {
                var names = w[key].Select(t => (string)t).ToArray();
                var ours = WeaponInfo.All.Where(x => WeaponInfo.Category(x) == cat).Select(WeaponInfo.Ko).ToArray();
                CollectionAssert.AreEqual(names, ours, key);
                foreach (var n in names) Assert.AreEqual(cat, Db.GetWeapon(WeaponInfo.FromKo(n)).category, n);
            }
            Check("melee", WeaponCategory.Melee);
            Check("ranged", WeaponCategory.Ranged);
            Check("magic", WeaponCategory.Magic);
        }

        [Test]
        public void Reactions_MatchMachineRules_PairsNamesMultipliersEffects()
        {
            var rules = (JArray)TestData.MachineRules["combat"]["elemental_reactions"];
            Assert.AreEqual(13, rules.Count);
            Assert.AreEqual(13, Db.Reactions.Count);
            foreach (var r in rules)
            {
                var els = r["elements"].Select(t => ElementInfo.FromKo((string)t)).ToArray();
                var def = Db.FindReaction(els[0], els[1]);
                Assert.IsNotNull(def, (string)r["name"]);
                Assert.AreEqual((string)r["name"], def.name);
                Assert.AreEqual((float)r["prototype_multiplier"], def.multiplier, 1e-5f, def.name);
                var expected = (string)r["effect_type"] == "shield" ? ReactionEffect.Shield : ReactionEffect.Damage;
                Assert.AreEqual(expected, def.effect, def.name);
            }
        }

        [Test]
        public void ReactionPairs_36Pairs_13React_23None_Commutative()
        {
            int react = 0, none = 0;
            var all = ElementInfo.All;
            for (int i = 0; i < all.Length; i++)
            {
                for (int j = i + 1; j < all.Length; j++)
                {
                    var ab = Db.FindReaction(all[i], all[j]);
                    var ba = Db.FindReaction(all[j], all[i]);
                    Assert.AreSame(ab, ba, "A+B 와 B+A 는 같은 반응이어야 함");
                    if (ab != null) react++;
                    else none++;
                }
                Assert.IsNull(Db.FindReaction(all[i], all[i]), "같은 속성은 반응 없음");
            }
            Assert.AreEqual(13, react);
            Assert.AreEqual(23, none);
        }

        [Test]
        public void RockPlusIce_IsShield_NotDamage()
        {
            var r = Db.FindReaction(Element.Rock, Element.Ice);
            Assert.AreEqual("결빙 방벽", r.name);
            Assert.AreEqual(ReactionEffect.Shield, r.effect);
            Assert.AreEqual(1, Db.Reactions.Count(x => x.effect == ReactionEffect.Shield));
        }

        [Test]
        public void CanonRoster_Is16_NamesInOrder_NoInventedFields()
        {
            var rules = (JArray)TestData.MachineRules["roster"]["characters"];
            Assert.AreEqual(16, Db.RosterCanon.Count);
            for (int i = 0; i < 16; i++)
            {
                var rule = rules[i];
                var ours = Db.RosterCanon[i];
                Assert.AreEqual((string)rule["name"], ours.name);
                Assert.AreEqual((string)rule["generation"], ours.generation);
                string ruleWeapon = (string)rule["confirmed_weapon_or_concept"];
                if (ruleWeapon == null) Assert.IsNull(ours.confirmedWeapon, ours.name + " 무기 미확정이어야 함");
                else Assert.IsTrue(ruleWeapon.StartsWith(WeaponInfo.Ko(ours.confirmedWeapon.Value)), ours.name);
                // 확정 자료가 없는 필드는 비워둔다(AI 가 사실처럼 채우지 않음)
                Assert.IsNull(ours.element, ours.name);
                Assert.IsNull(ours.role, ours.name);
                Assert.IsNull(ours.rarity, ours.name);
                Assert.IsNull(ours.attackType, ours.name);
                Assert.IsFalse(ours.definitionVerified, ours.name);
            }
        }

        [Test]
        public void TestProxies_AreMarked_AndDoNotUseCanonOrLegacyNames()
        {
            var canon = Db.RosterCanon.Select(r => r.name).ToList();
            var legacy = TestData.MachineRules["roster"]["legacy_art_concept_names"].Select(t => (string)t).ToList();
            Assert.AreEqual(18, legacy.Count);
            foreach (var c in Db.Characters)
            {
                Assert.IsTrue(c.isTestProxy, c.id);
                Assert.AreEqual(DataStatus.Temp, c.status, c.id);
                foreach (var n in canon.Concat(legacy))
                    Assert.IsFalse(c.displayName.Contains(n), $"검증용 캐릭터 {c.displayName} 가 실제 이름 {n} 을 사용");
            }
        }

        [Test]
        public void TestProxies_CoverAll16Weapons_OnePerWeapon()
        {
            var weapons = Db.Characters.Select(c => c.weapon).ToList();
            CollectionAssert.AreEquivalent(WeaponInfo.All, weapons);
        }

        [Test]
        public void Balance_PrototypeValues_MatchMachineRules()
        {
            var p = TestData.MachineRules["combat"]["prototype"];
            var b = Db.Balance.prototype;
            Assert.AreEqual((float)p["time_limit_seconds"], b.timeLimit);
            Assert.AreEqual((int)p["resonance_required_ultimate"], b.ultimateCost);
            Assert.AreEqual((int)p["resonance_gain_normal"], b.resonanceNormal);
            Assert.AreEqual((int)p["resonance_gain_skill"], b.resonanceSkill);
            Assert.AreEqual((int)p["resonance_gain_perfect_dodge"], b.resonancePerfectDodge);
            Assert.AreEqual((int)p["resonance_gain_tag"], b.resonanceTag);
            var gauges = p["break_gauge_normal_hard_hell"].Select(t => (float)t).ToArray();
            Assert.AreEqual(gauges[0], b.breakGaugeNormal);
            Assert.AreEqual(gauges[1], b.breakGaugeHard);
            Assert.AreEqual(gauges[2], b.breakGaugeHell);
            Assert.AreEqual((float)p["break_stagger_seconds"], b.breakStaggerSeconds);
            Assert.AreEqual((float)p["break_damage_multiplier"], b.breakDamageMultiplier, 1e-5f);
        }

        [Test]
        public void Progression_Constants_MatchMachineRules()
        {
            var p = TestData.MachineRules["progression"];
            Assert.AreEqual(5, ((JArray)p["rarities"]).Count);
            Assert.IsFalse((bool)p["UR_directly_summonable"]);
            Assert.AreEqual(1.85f, Db.Balance.SkillLevelMultiplier((int)p["skill_max_level"]), 1e-5f);
        }

        [Test]
        public void Stages_ThreeStages_OneMap_UnlockChain()
        {
            var real = Db.Stages.Where(s => s.id.StartsWith("vs_")).ToList();
            Assert.AreEqual(3, real.Count);
            Assert.IsNull(real[0].unlockAfter);
            Assert.AreEqual(real[0].id, real[1].unlockAfter);
            Assert.AreEqual(real[1].id, real[2].unlockAfter);
            Assert.IsTrue(real.SelectMany(s => s.waves).SelectMany(w => w.spawns).Any(sp => Db.GetEnemy(sp.enemy).isBoss), "보스 1종 포함");
            Assert.AreEqual(2, Db.Enemies.Count(e => !e.isBoss), "일반 적 2종");
        }

        [Test]
        public void AllWeaponSteps_HaveSaneTiming()
        {
            foreach (var w in Db.Weapons.Values)
            {
                foreach (var s in GameDatabase.AllSteps(w))
                {
                    Assert.Greater(s.TotalDuration, 0.05f, w.id + "/" + s.name);
                    Assert.LessOrEqual(s.ChainTime, s.TotalDuration + 1e-5f, w.id + "/" + s.name);
                }
                // 일반공격 평균 연결 주기: 원본 기준 0.58초 축에서 크게 벗어나지 않음(0.15~1.2초)
                float avg = w.combo.Average(s => s.ChainTime);
                Assert.That(avg, Is.InRange(0.15f, 1.2f), w.id.ToString());
            }
        }
    }
}
