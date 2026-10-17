using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Core.Audio;
using Lumina.Core.Data;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>
    /// QA 2(무기별 독립 타격음)의 기계적 회귀검사. 실제 합격 판정은 모바일 스피커/헤드폰 청음이며 이 테스트로 대체하지 않는다.
    /// </summary>
    public class SfxTests
    {
        private static float[] Impact(WeaponType w, SfxTier t = SfxTier.Medium) => SfxSynth.RenderWeapon(w, SfxKind.Impact, t, Element.None, 0);

        [Test]
        public void AllWeapons_RenderAllEventKinds_NonSilent_NoNaN_InRange()
        {
            foreach (var w in WeaponInfo.All)
            {
                var kinds = WeaponInfo.Category(w) == WeaponCategory.Melee
                    ? new[] { SfxKind.Swing, SfxKind.Impact, SfxKind.Tail }
                    : new[] { SfxKind.Cast, SfxKind.Projectile, SfxKind.Impact, SfxKind.Tail };
                foreach (var k in kinds)
                {
                    var x = SfxSynth.RenderWeapon(w, k, SfxTier.Finisher, Element.Fire, 0, 0.3f);
                    Assert.Greater(x.Length, 200, $"{w}/{k}");
                    float peak = 0f;
                    foreach (float v in x)
                    {
                        Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), $"{w}/{k} NaN");
                        peak = Math.Max(peak, Math.Abs(v));
                    }
                    Assert.That(peak, Is.InRange(0.1f, 1.0f), $"{w}/{k}");
                }
            }
        }

        [Test]
        public void Render_IsDeterministic()
        {
            CollectionAssert.AreEqual(Impact(WeaponType.Axe), Impact(WeaponType.Axe));
        }

        /// <summary>16개 무기 명중음은 특징량 공간에서 서로 일정 거리 이상 떨어져 있어야 한다(같은 소리 재사용 방지).</summary>
        [Test]
        public void ImpactSounds_AreDistinct_AcrossAll16Weapons()
        {
            var feats = WeaponInfo.All.ToDictionary(w => w, w => SfxFeatures.Compute(Impact(w)));
            var report = new List<string>();
            float minD = float.MaxValue;
            string minPair = "";
            var all = WeaponInfo.All;
            for (int i = 0; i < all.Length; i++)
            {
                for (int j = i + 1; j < all.Length; j++)
                {
                    float d = SfxFeatures.Distance(feats[all[i]], feats[all[j]]);
                    if (d < minD)
                    {
                        minD = d;
                        minPair = all[i] + " vs " + all[j];
                    }
                    if (d < 0.25f) report.Add($"{all[i]} vs {all[j]}: {d:0.000}");
                }
            }
            TestContext.WriteLine($"최소 거리 {minD:0.000} ({minPair})");
            foreach (var w in all) TestContext.WriteLine($"{WeaponInfo.Ko(w)}: {feats[w]}");
            Assert.IsEmpty(report, "구별 불충분:\n" + string.Join("\n", report));
        }

        /// <summary>무기 계열 내 '시그니처'(휘두름/발사 + 명중)도 서로 달라야 한다.</summary>
        [Test]
        public void WeaponSignatures_PreImpactSoundsAreDistinct()
        {
            var feats = new Dictionary<WeaponType, SfxFeatures>();
            foreach (var w in WeaponInfo.All)
            {
                var kind = WeaponInfo.Category(w) == WeaponCategory.Melee ? SfxKind.Swing : SfxKind.Projectile;
                feats[w] = SfxFeatures.Compute(SfxSynth.RenderWeapon(w, kind, SfxTier.Medium, Element.None, 0));
            }
            var all = WeaponInfo.All;
            for (int i = 0; i < all.Length; i++)
            {
                for (int j = i + 1; j < all.Length; j++)
                {
                    float d = SfxFeatures.Distance(feats[all[i]], feats[all[j]]);
                    Assert.GreaterOrEqual(d, 0.15f, $"{all[i]} vs {all[j]} 휘두름/발사음 구별 불충분");
                }
            }
        }

        [Test]
        public void TierLoudness_LightQuieterThanFinisher_UltimateLongest()
        {
            foreach (var w in WeaponInfo.All)
            {
                var light = Impact(w, SfxTier.Light);
                var fin = Impact(w, SfxTier.Finisher);
                var ult = SfxSynth.RenderWeapon(w, SfxKind.Impact, SfxTier.Ultimate, Element.None, 0);
                Assert.Less(light.Max(Math.Abs), fin.Max(Math.Abs), w + ": 1타보다 피니시가 강해야 함");
                Assert.GreaterOrEqual(ult.Length, light.Length, w + ": 궁극기 명중음이 가장 길거나 같음");
            }
        }

        /// <summary>근접 휘두름 소리의 에너지 피크가 SwingLead(명중 프레임까지의 시간) 부근에 있어야 한다.</summary>
        [Test]
        public void SwingPeak_AlignsWithHitFrameLead()
        {
            foreach (var w in WeaponInfo.All.Where(x => WeaponInfo.Category(x) == WeaponCategory.Melee))
            {
                var x = SfxSynth.RenderWeapon(w, SfxKind.Swing, SfxTier.Medium, Element.None, 0);
                int win = Dsp.N(0.005f);
                float best = 0f;
                int bestIdx = 0;
                for (int s = 0; s + win < x.Length; s += win)
                {
                    float e = 0f;
                    for (int i = 0; i < win; i++) e += x[s + i] * x[s + i];
                    if (e > best)
                    {
                        best = e;
                        bestIdx = s;
                    }
                }
                float peakT = bestIdx / (float)Dsp.SampleRate;
                float lead = SfxTiming.SwingLead(w);
                Assert.That(peakT, Is.InRange(lead * 0.55f - 0.01f, lead * 1.35f + 0.015f), $"{w}: 휘두름 피크 {peakT * 1000:0}ms vs 명중 선행 {lead * 1000:0}ms");
            }
        }

        [Test]
        public void HitReaction_DependsOnTargetMaterial()
        {
            var flesh = SfxFeatures.Compute(SfxSynth.RenderHitReaction(EnemyMaterial.Flesh, SfxTier.Medium, 0));
            var armor = SfxFeatures.Compute(SfxSynth.RenderHitReaction(EnemyMaterial.Armor, SfxTier.Medium, 0));
            var phantom = SfxFeatures.Compute(SfxSynth.RenderHitReaction(EnemyMaterial.Phantom, SfxTier.Medium, 0));
            Assert.Greater(SfxFeatures.Distance(flesh, armor), 0.2f);
            Assert.Greater(SfxFeatures.Distance(flesh, phantom), 0.2f);
            Assert.Greater(SfxFeatures.Distance(armor, phantom), 0.2f);
        }
    }
}
