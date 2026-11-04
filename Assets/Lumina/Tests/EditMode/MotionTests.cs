using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Presentation;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>
    /// 무기 16종 공격 모션 계약: 근접은 무기 끝이 명중 순간 가장 빠르고 몸 앞을 지나며,
    /// 원거리/마법은 발동 순간 정면을 겨눈다. 포즈가 튀지 않고 대기 자세로 복귀한다.
    /// </summary>
    public class MotionTests
    {
        private static GameDatabase Db => TestData.Db;

        private static IEnumerable<(WeaponType w, ActionKind kind, int step, AttackStepDef def)> AllAttacks()
        {
            foreach (var wd in Db.Weapons.Values)
            {
                for (int i = 0; i < wd.combo.Length; i++) yield return (wd.id, ActionKind.Normal, i, wd.combo[i]);
                yield return (wd.id, ActionKind.Skill, 0, wd.skill);
                yield return (wd.id, ActionKind.Ultimate, 0, wd.ultimate);
            }
        }

        private static Pose At(WeaponType w, ActionKind k, int step, AttackStepDef d, float t) => MotionLibrary.Attack(w, k, step, d, t, out _, out _);

        private static V3 ActiveTip(WeaponType w, Pose p, bool left) => left ? MotionLibrary.TipLeft(w, p) : MotionLibrary.Solve(w, p).tip;

        [Test]
        public void EulerConvention_MatchesUnity()
        {
            var down = MotionLibrary.Rotate(new V3(90f, 0f, 0f), new V3(0f, 0f, 1f));
            Assert.AreEqual(-1f, down.y, 1e-4f, "Euler(90,0,0)*forward = down");
            var right = MotionLibrary.Rotate(new V3(0f, 90f, 0f), new V3(0f, 0f, 1f));
            Assert.AreEqual(1f, right.x, 1e-4f, "Euler(0,90,0)*forward = right");
            var left = MotionLibrary.Rotate(new V3(0f, 0f, 90f), new V3(0f, 1f, 0f));
            Assert.AreEqual(-1f, left.x, 1e-4f, "Euler(0,0,90)*up = left");
        }

        [Test]
        public void Melee_TipSpeedPeaksAtHit_AndPassesInFront()
        {
            var fails = new List<string>();
            foreach (var (w, kind, step, def) in AllAttacks().Where(a => WeaponInfo.Category(a.w) == WeaponCategory.Melee))
            {
                float h = MotionLibrary.StrikeHalf(w, def);
                for (int i = 0; i < def.SafeHits; i++)
                {
                    float H = def.startup + i * def.hitInterval;
                    bool left = UsesLeft(w, def, step, i, H, kind);
                    const float dt = 1f / 480f;
                    float bestV = -1f, bestT = 0f;
                    for (float t = MathF.Max(0f, H - h * 1.4f); t <= H + h * 1.4f; t += dt)
                    {
                        float v = (ActiveTip(w, At(w, kind, step, def, t + dt), left) - ActiveTip(w, At(w, kind, step, def, t), left)).Length / dt;
                        if (v > bestV)
                        {
                            bestV = v;
                            bestT = t;
                        }
                    }
                    float tol = MathF.Max(h * 0.5f, 1.5f / 60f);
                    // 회전 베기는 등속 회전이라 '최고속 시점' 대신 정면 통과만 검사
                    if (def.swing != SwingStyle.Spin && MathF.Abs(bestT - H) > tol) fails.Add($"{w}/{kind}/{def.name} hit{i}: 최고속 {bestT * 1000:0}ms vs 명중 {H * 1000:0}ms");
                    var tip = ActiveTip(w, At(w, kind, step, def, H), left);
                    if (tip.z < 0.25f) fails.Add($"{w}/{kind}/{def.name} hit{i}: 명중 시 무기 끝이 몸 앞이 아님 z={tip.z:0.00}");
                    if (bestV < 2f) fails.Add($"{w}/{kind}/{def.name} hit{i}: 스윙 속도 부족 {bestV:0.0}m/s");
                }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        /// <summary>교대 타격(쌍검/건틀릿 왼손) 판정: 명중 순간 앞에 있는 손.</summary>
        private static bool UsesLeft(WeaponType w, AttackStepDef def, int step, int hit, float H, ActionKind kind)
        {
            var prof = MotionLibrary.Profile(w);
            if (!prof.dual) return false;
            var p = At(w, kind, step, def, H);
            return MotionLibrary.TipLeft(w, p).z > MotionLibrary.Solve(w, p).tip.z;
        }

        [Test]
        public void RangedMagic_AimForwardAtRelease()
        {
            var fails = new List<string>();
            foreach (var (w, kind, step, def) in AllAttacks().Where(a => WeaponInfo.Category(a.w) != WeaponCategory.Melee))
            {
                for (int i = 0; i < def.SafeHits; i++)
                {
                    float H = def.startup + i * def.hitInterval;
                    var j = MotionLibrary.Solve(w, At(w, kind, step, def, H + 0.004f));
                    if (j.tip.z < 0.1f) fails.Add($"{w}/{kind}/{def.name}: 발동 시 정면 아님 z={j.tip.z:0.00}");
                }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }

        [Test]
        public void Poses_AreContinuous_NoPops()
        {
            var fails = new List<string>();
            foreach (var (w, kind, step, def) in AllAttacks())
            {
                var prev = At(w, kind, step, def, 0f);
                float end = def.TotalDuration + 0.1f;
                for (float t = 1f / 60f; t <= end; t += 1f / 60f)
                {
                    var p = At(w, kind, step, def, t);
                    float arm = MathF.Max(Max(p.armR - prev.armR), Max(p.armL - prev.armL));
                    float torso = Max(p.torso - prev.torso);
                    float lean = MathF.Max(MathF.Abs(p.modelRot.x - prev.modelRot.x), MathF.Abs(p.modelRot.z - prev.modelRot.z));
                    float move = (p.modelPos - prev.modelPos).Length;
                    if (arm > 120f || torso > 90f || lean > 45f || move > 0.5f)
                        fails.Add($"{w}/{kind}/{def.name} t={t:0.000}: 팔 {arm:0} 몸통 {torso:0} 기울기 {lean:0} 이동 {move:0.00}");
                    prev = p;
                }
            }
            Assert.IsEmpty(fails, string.Join("\n", fails.Take(20)));
        }

        private static float Max(V3 d) => MathF.Max(MathF.Abs(d.x), MathF.Max(MathF.Abs(d.y), MathF.Abs(d.z)));

        [Test]
        public void Attacks_ReturnToRestPose()
        {
            foreach (var (w, kind, step, def) in AllAttacks())
            {
                var p = At(w, kind, step, def, def.TotalDuration + 0.2f);
                var r = MotionLibrary.Rest(w);
                Assert.Less(Max(p.armR - r.armR), 0.5f, $"{w}/{def.name}");
                Assert.Less(Max(p.torso - r.torso), 0.5f, $"{w}/{def.name}");
                float yaw = ((p.modelRot.y % 360f) + 360f) % 360f;
                Assert.That(MathF.Min(yaw, 360f - yaw), Is.LessThan(0.5f), $"{w}/{def.name} 회전 후 정면 복귀");
            }
        }

        /// <summary>근접 8종 일반 콤보의 무기 끝 궤적 특징이 서로 달라야 한다(같은 모션 재사용 방지).</summary>
        [Test]
        public void MeleeCombos_HaveDistinctTrajectories()
        {
            var sig = new Dictionary<WeaponType, float[]>();
            foreach (var wd in Db.Weapons.Values.Where(x => x.category == WeaponCategory.Melee))
            {
                float len = 0f, dur = 0f, maxY = float.MinValue, minY = float.MaxValue, minX = 0f, maxX = 0f, maxZ = 0f, twist = 0f, bob = 0f;
                for (int s = 0; s < wd.combo.Length; s++)
                {
                    var def = wd.combo[s];
                    V3 prevTip = MotionLibrary.Solve(wd.id, At(wd.id, ActionKind.Normal, s, def, 0f)).tip;
                    for (float t = 1f / 120f; t <= def.TotalDuration; t += 1f / 120f)
                    {
                        var p = At(wd.id, ActionKind.Normal, s, def, t);
                        var tip = MotionLibrary.Solve(wd.id, p).tip;
                        var tipL = MotionLibrary.TipLeft(wd.id, p);
                        len += (tip - prevTip).Length;
                        prevTip = tip;
                        maxY = MathF.Max(maxY, MathF.Max(tip.y, tipL.y));
                        minY = MathF.Min(minY, MathF.Min(tip.y, tipL.y));
                        minX = MathF.Min(minX, MathF.Min(tip.x, tipL.x));
                        maxX = MathF.Max(maxX, MathF.Max(tip.x, tipL.x));
                        maxZ = MathF.Max(maxZ, MathF.Max(tip.z, tipL.z));
                        twist = MathF.Max(twist, MathF.Abs(p.torso.y));
                        bob = MathF.Max(bob, MathF.Abs(p.modelPos.y));
                    }
                    dur += def.TotalDuration;
                }
                sig[wd.id] = new[] { MathF.Log(dur), MathF.Log(len / dur), maxY - minY, maxX - minX, maxZ, twist / 60f, bob * 4f, wd.combo.Length / 4f };
            }
            var keys = sig.Keys.ToList();
            var close = new List<string>();
            for (int i = 0; i < keys.Count; i++)
            {
                for (int j = i + 1; j < keys.Count; j++)
                {
                    float d = MathF.Sqrt(sig[keys[i]].Zip(sig[keys[j]], (a, b) => (a - b) * (a - b)).Sum());
                    TestContext.WriteLine($"{keys[i]} vs {keys[j]}: {d:0.00}");
                    if (d < 0.25f) close.Add($"{keys[i]} vs {keys[j]}: {d:0.00}");
                }
            }
            Assert.IsEmpty(close, "궤적 구별 부족:\n" + string.Join("\n", close));
        }
    }
}
