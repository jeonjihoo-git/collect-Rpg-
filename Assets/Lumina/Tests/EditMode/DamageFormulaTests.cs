using Lumina.Core.Combat;
using Lumina.Core.Data;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>GDD 03-2 / 04-3 공식 검증.</summary>
    public class DamageFormulaTests
    {
        private static PrototypeBalance P => TestData.Db.Balance.prototype;

        [Test]
        public void DefenseK_Is300TimesOnePlus2PercentPerLevel()
        {
            Assert.AreEqual(330f, DamageCalc.DefenseK(P, 5), 1e-3f);
            Assert.AreEqual(300f, DamageCalc.DefenseK(P, 0), 1e-3f);
        }

        [Test]
        public void DefenseReduction_BasicRatio()
        {
            Assert.AreEqual(150f / 480f, DamageCalc.DefenseReduction(150f, 0f, 0f, 330f), 1e-5f);
        }

        [Test]
        public void PenetrationAndIgnore_EachAppliedOnce()
        {
            // 유효 방어 = 200×(1−0.5)=100 → 100/(100+300)=0.25 → −0.1%p = 0.15
            Assert.AreEqual(0.15f, DamageCalc.DefenseReduction(200f, 0.5f, 0.1f, 300f), 1e-5f);
        }

        [Test]
        public void DefenseReduction_NeverNegative()
        {
            Assert.AreEqual(0f, DamageCalc.DefenseReduction(50f, 0f, 0.9f, 300f), 1e-6f);
        }

        [Test]
        public void Compute_AppliesMultipliersOnce()
        {
            var i = new DamageCalc.Input { attack = 1000f, coef = 0.12f, skillLevelMult = 1f, targetDefense = 0f, targetLevel = 1 };
            Assert.AreEqual(120f, DamageCalc.Compute(P, i), 1e-3f);
            i.staggerMult = 1.5f;
            Assert.AreEqual(180f, DamageCalc.Compute(P, i), 1e-3f);
            i.reactionMult = 1.7f;
            Assert.AreEqual(306f, DamageCalc.Compute(P, i), 1e-3f);
            i.crit = true;
            i.critDmg = 0.5f;
            Assert.AreEqual(459f, DamageCalc.Compute(P, i), 1e-2f);
        }

        [TestCase(1, 1.00f)]
        [TestCase(3, 1.15f)]
        [TestCase(5, 1.35f)]
        [TestCase(7, 1.55f)]
        [TestCase(10, 1.85f)]
        [TestCase(2, 1.075f)]
        [TestCase(8, 1.65f)]
        public void SkillLevelMultiplier_PrototypeTable(int level, float expected)
        {
            Assert.AreEqual(expected, TestData.Db.Balance.SkillLevelMultiplier(level), 1e-4f);
        }

        [Test]
        public void LevelStat_Lv30_AndBreakthroughBonus()
        {
            Assert.AreEqual(800f * (1f + 0.045f * 29f), DamageCalc.LevelStat(800f, 0.045f, 30, 0, 0.08f), 1e-2f);
            Assert.AreEqual(800f * (1f + 0.045f * 29f) * 1.48f, DamageCalc.LevelStat(800f, 0.045f, 30, 6, 0.08f), 1e-2f);
        }
    }
}
