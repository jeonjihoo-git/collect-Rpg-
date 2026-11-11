using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Presentation;
using NUnit.Framework;

namespace Lumina.Tests
{
    /// <summary>
    /// 베기 명중 이펙트 v3 연동(계획서 v1.1) 검증 18개.
    /// A 공격 판정 불변 · B 스킬·궁극기 제외 · C 재생 시간 · D 폴백·풀.
    /// 연출 판정 로직은 Core 순수 코드라 Unity Test Runner 와 dotnet 양쪽에서 같은 결과가 나와야 한다.
    /// </summary>
    public class BladeImpactFlipbookTests
    {
        private static readonly string[] Blades = { "tp_longsword", "tp_dagger", "tp_dualblades", "tp_scythe" };

        // ───────────── A. 공격 판정 불변 ─────────────

        /// <summary>
        /// 연동 전 코드(f8c7835)에서 생성한 기준값. 시나리오: t_dummy, 시드 1, 등장 대기 후 공격을 3초(180틱) 누름.
        /// hits = 틱:단계:타번호:등급:행동:피해 / actions = 틱:행동:단계 / swings = 틱:단계.
        /// </summary>
        private static readonly Dictionary<string, (string hits, string actions, string swings)> Baseline =
            new Dictionary<string, (string, string, string)>
            {
                ["tp_longsword"] = (
                    "79:0:0:Light:Normal:66.32513;96:1:0:Light:Normal:72.35468;116:2:0:Finisher:Normal:120.59113;139:0:0:Light:Normal:66.32513;156:1:0:Light:Normal:72.35468;176:2:0:Finisher:Normal:120.59113;199:0:0:Light:Normal:66.32513;216:1:0:Light:Normal:72.35468;236:2:0:Finisher:Normal:120.59113",
                    "71:Normal:0;88:Normal:1;105:Normal:2;131:Normal:0;148:Normal:1;165:Normal:2;191:Normal:0;208:Normal:1;225:Normal:2",
                    "73:0;90:1;111:2;133:0;150:1;171:2;193:0;210:1;231:2"),
                ["tp_dagger"] = (
                    "76:0:0:Light:Normal:42.206898;87:1:0:Light:Normal:42.206898;98:2:0:Light:Normal:42.206898;110:3:0:Finisher:Normal:84.413795;127:0:0:Light:Normal:42.206898;138:1:0:Light:Normal:42.206898;149:2:0:Light:Normal:42.206898;161:3:0:Finisher:Normal:84.413795;178:0:0:Light:Normal:42.206898;189:1:0:Light:Normal:42.206898;200:2:0:Light:Normal:42.206898;212:3:0:Finisher:Normal:84.413795;229:0:0:Light:Normal:42.206898;240:1:0:Light:Normal:42.206898",
                    "71:Normal:0;82:Normal:1;93:Normal:2;104:Normal:3;122:Normal:0;133:Normal:1;144:Normal:2;155:Normal:3;173:Normal:0;184:Normal:1;195:Normal:2;206:Normal:3;224:Normal:0;235:Normal:1;246:Normal:2",
                    "73:0;84:1;95:2;107:3;124:0;135:1;146:2;158:3;175:0;186:1;197:2;209:3;226:0;237:1;248:2"),
                ["tp_dualblades"] = (
                    "76:0:0:Light:Normal:39.19212;82:0:1:Light:Normal:39.19212;94:1:0:Light:Normal:39.19212;100:1:1:Light:Normal:39.19212;113:2:0:Finisher:Normal:36.17734;118:2:1:Finisher:Normal:36.17734;122:2:2:Finisher:Normal:36.17734;126:2:3:Finisher:Normal:36.17734;144:0:0:Light:Normal:39.19212;150:0:1:Light:Normal:39.19212;162:1:0:Light:Normal:39.19212;168:1:1:Light:Normal:39.19212;181:2:0:Finisher:Normal:36.17734;186:2:1:Finisher:Normal:36.17734;190:2:2:Finisher:Normal:36.17734;194:2:3:Finisher:Normal:36.17734;212:0:0:Light:Normal:39.19212;218:0:1:Light:Normal:39.19212;230:1:0:Light:Normal:39.19212;236:1:1:Light:Normal:39.19212;249:2:0:Finisher:Normal:36.17734",
                    "71:Normal:0;89:Normal:1;107:Normal:2;139:Normal:0;157:Normal:1;175:Normal:2;207:Normal:0;225:Normal:1;243:Normal:2",
                    "73:0;78:0;91:1;96:1;110:2;114:2;118:2;122:2;141:0;146:0;159:1;164:1;178:2;182:2;186:2;190:2;209:0;214:0;227:1;232:1;246:2;250:2"),
                ["tp_scythe"] = (
                    "83:0:0:Medium:Normal:84.413795;107:1:0:Medium:Normal:84.413795;137:2:0:Finisher:Normal:156.76848;167:0:0:Medium:Normal:84.413795;191:1:0:Medium:Normal:84.413795;221:2:0:Finisher:Normal:156.76848",
                    "71:Normal:0;95:Normal:1;119:Normal:2;155:Normal:0;179:Normal:1;203:Normal:2;239:Normal:0",
                    "75:0;99:1;129:2;159:0;183:1;213:2;243:0"),
            };

        private sealed class ComboRun
        {
            public List<CombatEvent> Hits, Starts, Swings;
        }

        private static ComboRun RunHeldCombo(string id)
        {
            var sim = TestData.Sim(TestData.DummyDb, "t_dummy", new[] { id });
            TestData.WaitSpawn(sim);
            int from = sim.Log.Count;
            for (int i = 0; i < 180; i++)
            {
                var p = PlayerInput.None;
                p.attackHeld = true;
                p.attackPressed = i == 0;
                sim.Step(p);
            }
            var ev = sim.Log.Skip(from).ToList();
            return new ComboRun
            {
                Hits = ev.Where(e => e.type == CombatEventType.Hit).ToList(),
                Starts = ev.Where(e => e.type == CombatEventType.ActionStart).ToList(),
                Swings = ev.Where(e => e.type == CombatEventType.Swing).ToList(),
            };
        }

        private static string Signature(ComboRun r)
        {
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            foreach (var h in r.Hits) sb.Append($"{h.tick}:{h.step}:{h.hitIndex}:{h.tier}:{h.action}:{h.amount.ToString("F3", ci)};");
            sb.Append('|');
            foreach (var a in r.Starts) sb.Append($"{a.tick}:{a.action}:{a.step};");
            sb.Append('|');
            foreach (var s in r.Swings) sb.Append($"{s.tick}:{s.step};");
            return sb.ToString();
        }

        [Test]
        public void BladeNormalCombo_HitEvents_MatchBaseline()
        {
            var ci = CultureInfo.InvariantCulture;
            foreach (var id in Blades)
            {
                var run = RunHeldCombo(id);
                var expected = Baseline[id].hits.Split(';');
                Assert.AreEqual(expected.Length, run.Hits.Count, id + ": 명중 횟수가 연동 전과 같아야 함");
                for (int i = 0; i < expected.Length; i++)
                {
                    var f = expected[i].Split(':');
                    var h = run.Hits[i];
                    string at = $"{id} 명중 #{i}";
                    Assert.AreEqual(int.Parse(f[0], ci), h.tick, at + " 틱");
                    Assert.AreEqual(int.Parse(f[1], ci), h.step, at + " 콤보 단계");
                    Assert.AreEqual(int.Parse(f[2], ci), h.hitIndex, at + " 타 번호");
                    Assert.AreEqual(f[3], h.tier.ToString(), at + " 등급");
                    Assert.AreEqual(f[4], h.action.ToString(), at + " 행동");
                    float amount = float.Parse(f[5], ci);
                    Assert.AreEqual(amount, h.amount, Math.Abs(amount) * 1e-4f, at + " 피해");
                }
            }
        }

        [Test]
        public void BladeNormalCombo_ActionTiming_MatchBaseline()
        {
            foreach (var id in Blades)
            {
                var run = RunHeldCombo(id);
                string actions = string.Join(";", run.Starts.Select(a => $"{a.tick}:{a.action}:{a.step}"));
                string swings = string.Join(";", run.Swings.Select(s => $"{s.tick}:{s.step}"));
                Assert.AreEqual(Baseline[id].actions, actions, id + ": 행동 시작 틱(공격 동작 타이밍)이 연동 전과 같아야 함");
                Assert.AreEqual(Baseline[id].swings, swings, id + ": 휘두름 틱이 연동 전과 같아야 함");
            }
        }

        [Test]
        public void FlipbookCode_DoesNotTouchCombat()
        {
            var before = Blades.Select(id => Signature(RunHeldCombo(id))).ToList();

            // 연출 판정 코드를 대량으로 호출 — 전역 상태·난수·데이터를 건드리면 아래 전투 결과가 달라진다.
            var gate = new ImpactFlipbookGate();
            var pool = new FlipbookPool();
            var pb = new FlipbookPlayback();
            int sink = 0;
            for (int i = 0; i < 10000; i++)
            {
                var w = (WeaponType)(i % 16);
                var t = (SfxTier)(i % 6);
                gate.SetResources(i % 3 != 0, i % 5 != 0);
                if (gate.UseFlipbook(w, t)) sink++;
                int slot = pool.Acquire(i * 0.01, out _, out _);
                if (i % 2 == 0) pool.Release(slot);
                pb.Start(i * 0.01);
                sink += pb.Frame(i * 0.01 + 0.1);
                FlipbookSpec.Uv(i % 16, out _, out _, out _, out _);
            }
            Assert.Greater(sink, 0);

            var after = Blades.Select(id => Signature(RunHeldCombo(id))).ToList();
            CollectionAssert.AreEqual(before, after, "연출 코드 호출 전후 전투 결과가 같아야 함");
        }

        // ───────────── B. 스킬·궁극기 제외 ─────────────

        [Test]
        public void Policy_SkillAndUltimate_NeverUseFlipbook()
        {
            foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
            {
                Assert.IsFalse(ImpactFlipbookPolicy.UsesBladeFlipbook(w, SfxTier.Skill), w + " 스킬");
                Assert.IsFalse(ImpactFlipbookPolicy.UsesBladeFlipbook(w, SfxTier.Ultimate), w + " 궁극기");
            }
        }

        [Test]
        public void Policy_OnlyBladeNormalTiers()
        {
            var blades = new HashSet<WeaponType> { WeaponType.Longsword, WeaponType.Dagger, WeaponType.DualBlades, WeaponType.Scythe };
            var normal = new[] { SfxTier.Light, SfxTier.Medium, SfxTier.Heavy, SfxTier.Finisher };
            int on = 0;
            foreach (WeaponType w in Enum.GetValues(typeof(WeaponType)))
            {
                foreach (SfxTier t in Enum.GetValues(typeof(SfxTier)))
                {
                    bool expected = blades.Contains(w) && normal.Contains(t);
                    Assert.AreEqual(expected, ImpactFlipbookPolicy.UsesBladeFlipbook(w, t), $"{w} × {t}");
                    if (expected) on++;
                }
            }
            Assert.AreEqual(16, on, "베기 4종 × 일반 공격 4등급만 사용");
        }

        [Test]
        public void Sim_SkillAndUltHits_MapToFlash()
        {
            int skillHits = 0, ultHits = 0;
            foreach (var id in Blades)
            {
                var sim = TestData.Sim(TestData.DummyDb, "t_dummy", new[] { id });
                TestData.WaitSpawn(sim);
                sim.Step(new PlayerInput { tagTo = -1, skillPressed = true });
                TestData.Idle(sim, 2.5f);
                sim.DebugAddResonance(sim.ResonanceMax);
                sim.Step(new PlayerInput { tagTo = -1, ultimatePressed = true });
                TestData.Idle(sim, 5f);
                sim.Step(new PlayerInput { tagTo = -1, attackPressed = true });
                TestData.Idle(sim, 1.5f);

                var hits = TestData.EventsOf(sim, CombatEventType.Hit);
                Assert.Greater(hits.Count(h => h.action == ActionKind.Normal), 0, id + ": 일반 공격 명중");
                foreach (var h in hits)
                {
                    bool flip = ImpactFlipbookPolicy.UsesBladeFlipbook(h.weapon, h.tier);
                    switch (h.action)
                    {
                        case ActionKind.Skill:
                            Assert.AreEqual(SfxTier.Skill, h.tier, id + ": 스킬 명중 등급");
                            Assert.IsFalse(flip, id + ": 스킬 명중은 기존 VFX(Flash)");
                            skillHits++;
                            break;
                        case ActionKind.Ultimate:
                            Assert.AreEqual(SfxTier.Ultimate, h.tier, id + ": 궁극기 명중 등급");
                            Assert.IsFalse(flip, id + ": 궁극기 명중은 기존 VFX(Flash)");
                            ultHits++;
                            break;
                        case ActionKind.Normal:
                            Assert.IsTrue(flip, id + ": 베기 일반 공격 명중은 v3");
                            break;
                    }
                }
            }
            Assert.Greater(skillHits, 0, "스킬 명중이 실제로 발생해야 검증이 의미 있음");
            Assert.Greater(ultHits, 0, "궁극기 명중이 실제로 발생해야 검증이 의미 있음");
        }

        // ───────────── C. 재생 시간 ─────────────

        [Test]
        public void Clock_Duration_Is16Over60()
        {
            Assert.AreEqual(4, FlipbookSpec.Columns);
            Assert.AreEqual(4, FlipbookSpec.Rows);
            Assert.AreEqual(16, FlipbookSpec.Frames);
            Assert.AreEqual(60, FlipbookSpec.Fps);
            Assert.AreEqual(15, FlipbookSpec.LastFrame);
            Assert.AreEqual(16.0 / 60.0, FlipbookSpec.Duration, 1e-12);
            Assert.AreEqual(0.2667, FlipbookSpec.Duration, 0.0001);
        }

        [Test]
        public void Clock_60fps_ShowsEveryFrameOnce()
        {
            var pb = new FlipbookPlayback();
            double now = 1234.5;
            pb.Start(now);
            var shown = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                int f = pb.Frame(now + i / 60.0);
                if (f < 0) break;
                shown.Add(f);
            }
            CollectionAssert.AreEqual(Enumerable.Range(0, 16).ToList(), shown, "60fps: 0~15번을 한 번씩");
            Assert.IsFalse(pb.Playing, "16프레임 후 종료");

            var offsets = new HashSet<(float, float)>();
            for (int f = 0; f < 16; f++)
            {
                FlipbookSpec.Uv(f, out float sx, out float sy, out float ox, out float oy);
                Assert.AreEqual(0.25f, sx);
                Assert.AreEqual(0.25f, sy);
                offsets.Add((ox, oy));
            }
            Assert.AreEqual(16, offsets.Count, "16프레임 UV가 모두 다른 칸");
            FlipbookSpec.Uv(15, out _, out _, out float lx, out float ly);
            Assert.AreEqual(0.75f, lx, "15번 = 오른쪽 아래 칸(완전 투명 프레임)");
            Assert.AreEqual(0f, ly);
            FlipbookSpec.Uv(4, out _, out _, out float x4, out float y4);
            Assert.AreEqual(0f, x4, "4번 = 둘째 행 첫 칸");
            Assert.AreEqual(0.5f, y4);
        }

        [Test]
        public void Clock_StartFrameIsZero_NoAdvance()
        {
            var pb = new FlipbookPlayback();
            pb.Start(10.0);
            Assert.AreEqual(0, pb.Frame(10.0), "시작한 그 프레임(명중 틱) = 0번");
            Assert.AreEqual(0, FlipbookSpec.FrameAt(0.0));
            Assert.AreEqual(0, FlipbookSpec.FrameAt(-0.5), "시계 역행에도 0번");
            Assert.AreEqual(0, FlipbookSpec.FrameAt(double.NaN));
            FlipbookSpec.Uv(0, out _, out _, out float ox, out float oy);
            Assert.AreEqual(0f, ox, "0번 = 왼쪽 위 칸");
            Assert.AreEqual(0.75f, oy, "UV 원점이 왼쪽 아래이므로 맨 윗행 = 0.75");
        }

        [Test]
        public void Clock_OutlivesLegacyFlash()
        {
            const double legacyFlashLife = 0.12;
            var pb = new FlipbookPlayback();
            pb.Start(0.0);
            Assert.AreEqual(7, pb.Frame(legacyFlashLife), "기존 Flash 수명(0.12초) 시점에도 재생 중");
            Assert.IsTrue(pb.Playing);
            Assert.AreEqual(15, pb.Frame(FlipbookSpec.Duration - 0.001), "끝 직전 = 15번(투명)");
            Assert.IsTrue(pb.Playing, "16/60초 전에는 끝나지 않음");
            Assert.AreEqual(-1, pb.Frame(FlipbookSpec.Duration), "16/60초에 종료");
            Assert.IsFalse(pb.Playing);
            Assert.AreEqual(-1, pb.Frame(FlipbookSpec.Duration + 5.0), "종료 후 다시 켜지지 않음");
        }

        [Test]
        public void Clock_30fpsAndSpikes_KeepTotalLength()
        {
            var pb = new FlipbookPlayback();
            pb.Start(0.0);
            var shown = new List<int>();
            for (int i = 0; i < 20; i++)
            {
                int f = pb.Frame(i / 30.0);
                if (f < 0) break;
                shown.Add(f);
            }
            CollectionAssert.AreEqual(new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, shown, "30fps: 시간 기준으로 짝수 프레임");
            Assert.AreEqual(8.0 / 30.0, FlipbookSpec.Duration, 1e-12, "총 길이 동일(8화면 × 1/30초)");

            var spike = new FlipbookPlayback();
            spike.Start(0.0);
            Assert.AreEqual(0, spike.Frame(0.0));
            Assert.AreEqual(-1, spike.Frame(0.5), "0.5초 프레임 급증 → 바로 종료(예외 없음)");
        }

        // ───────────── D. 폴백·풀 ─────────────

        private static ImpactFlipbookGate ReadyGate()
        {
            var g = new ImpactFlipbookGate();
            g.SetResources(ImpactFlipbookGate.TextureValid(true, 1024, 1024), ImpactFlipbookGate.ShaderValid(true, ImpactFlipbookGate.ShaderName, true));
            return g;
        }

        [Test]
        public void Gate_AllReady_UsesFlipbook()
        {
            var g = ReadyGate();
            Assert.IsTrue(g.Ready);
            Assert.IsTrue(g.UseFlipbook(WeaponType.Longsword, SfxTier.Light));
            Assert.IsTrue(g.UseFlipbook(WeaponType.DualBlades, SfxTier.Finisher));
            Assert.IsFalse(g.UseFlipbook(WeaponType.Longsword, SfxTier.Skill), "준비돼도 스킬은 Flash");
            Assert.IsFalse(g.UseFlipbook(WeaponType.Spear, SfxTier.Light), "준비돼도 다른 무기는 Flash");
            Assert.IsFalse(new ImpactFlipbookGate().UseFlipbook(WeaponType.Longsword, SfxTier.Light), "리소스 확인 전에는 Flash");
        }

        [Test]
        public void Gate_TextureMissing_FallsBackToFlash()
        {
            Assert.IsFalse(ImpactFlipbookGate.TextureValid(false, 0, 0), "PNG 없음");
            Assert.IsFalse(ImpactFlipbookGate.TextureValid(true, 1024, 512), "정사각형 아님");
            Assert.IsFalse(ImpactFlipbookGate.TextureValid(true, 1022, 1022), "4의 배수 아님");
            Assert.IsFalse(ImpactFlipbookGate.TextureValid(true, 32, 32), "너무 작음");
            Assert.IsTrue(ImpactFlipbookGate.TextureValid(true, 1024, 1024));
            var g = new ImpactFlipbookGate();
            g.SetResources(ImpactFlipbookGate.TextureValid(false, 0, 0), true);
            Assert.IsTrue(g.ResourcesChecked);
            Assert.IsFalse(g.UseFlipbook(WeaponType.Longsword, SfxTier.Light), "텍스처 누락 → Flash");
        }

        [Test]
        public void Gate_ShaderUnsupported_FallsBackToFlash()
        {
            Assert.IsFalse(ImpactFlipbookGate.ShaderValid(false, null, false), "재질·셰이더 없음");
            Assert.IsFalse(ImpactFlipbookGate.ShaderValid(true, "Hidden/InternalErrorShader", true), "빌드에서 빠져 오류 셰이더로 바뀐 경우");
            Assert.IsFalse(ImpactFlipbookGate.ShaderValid(true, ImpactFlipbookGate.ShaderName, false), "기기 미지원");
            Assert.IsTrue(ImpactFlipbookGate.ShaderValid(true, "Lumina/FxAdditive", true));
            var g = new ImpactFlipbookGate();
            g.SetResources(true, ImpactFlipbookGate.ShaderValid(true, ImpactFlipbookGate.ShaderName, false));
            Assert.IsFalse(g.UseFlipbook(WeaponType.Dagger, SfxTier.Light), "셰이더 미지원 → Flash");
        }

        [Test]
        public void Gate_PoolInitFailure_LatchesToFlash()
        {
            var g = ReadyGate();
            Assert.IsTrue(g.UseFlipbook(WeaponType.Scythe, SfxTier.Medium));
            g.ReportFailure();
            Assert.IsTrue(g.Failed);
            for (int i = 0; i < 100; i++)
                Assert.IsFalse(g.UseFlipbook(WeaponType.Scythe, SfxTier.Medium), "풀 생성 실패 1회 후 계속 Flash");
            g.SetResources(true, true);
            Assert.IsFalse(g.Ready, "리소스를 다시 확인해도 실패 기록은 유지");
            g.Reset();
            Assert.IsFalse(g.Failed, "새 재생 세션에서만 초기화");
            Assert.IsFalse(g.ResourcesChecked);
        }

        [Test]
        public void Gate_Disabled_FallsBackToFlash()
        {
            var g = ReadyGate();
            g.Enabled = false;
            Assert.IsFalse(g.UseFlipbook(WeaponType.Longsword, SfxTier.Finisher), "A·B 비교 메뉴로 끔 → Flash");
            g.Enabled = true;
            Assert.IsTrue(g.UseFlipbook(WeaponType.Longsword, SfxTier.Finisher), "다시 켜면 v3");
        }

        [Test]
        public void Pool_Exhausted_ReusesOldest_NoGrowth()
        {
            var pool = new FlipbookPool();
            Assert.AreEqual(24, pool.Capacity);
            for (int i = 0; i < 24; i++)
            {
                int s = pool.Acquire(1.0 + i * 0.01, out bool created, out bool stolen);
                Assert.AreEqual(i, s);
                Assert.IsTrue(created);
                Assert.IsFalse(stolen);
            }
            Assert.AreEqual(24, pool.ActiveCount);

            int reused = pool.Acquire(1.30, out bool c25, out bool s25);
            Assert.AreEqual(0, reused, "25번째 요청 → 가장 오래 재생된 슬롯(0번) 재사용");
            Assert.IsFalse(c25, "새로 만들지 않음");
            Assert.IsTrue(s25);
            Assert.AreEqual(24, pool.Created, "용량 24 고정");
            Assert.AreEqual(1, pool.StolenCount);

            int next = pool.Acquire(1.31, out _, out bool s26);
            Assert.AreEqual(1, next, "다음으로 오래된 슬롯");
            Assert.IsTrue(s26);

            pool.Release(5);
            int freed = pool.Acquire(1.32, out bool c27, out bool s27);
            Assert.AreEqual(5, freed, "반환된 슬롯을 먼저 사용");
            Assert.IsFalse(c27);
            Assert.IsFalse(s27);

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < 1000; i++) pool.Acquire(2.0 + i * 0.001, out _, out _);
            });
            Assert.AreEqual(24, pool.Created);

            var p2 = new FlipbookPool();
            int a = p2.Acquire(0, out _, out _);
            p2.CancelCreate(a);
            Assert.AreEqual(0, p2.Created, "생성 실패한 슬롯은 되돌림");
        }

        [Test]
        public void Pool_RealBattleDemand_Within24()
        {
            int window = FlipbookSpec.Frames * BattleSim.TicksPerSecond / FlipbookSpec.Fps;   // 16/60초 = 16 틱
            Assert.AreEqual(16, window);
            int worst = 0;
            string worstAt = "";

            void Measure(BattleSim sim, string label)
            {
                var ticks = sim.Log.Where(e => e.type == CombatEventType.Hit && ImpactFlipbookPolicy.UsesBladeFlipbook(e.weapon, e.tier))
                    .Select(e => e.tick).OrderBy(t => t).ToList();
                int lo = 0;
                for (int hi = 0; hi < ticks.Count; hi++)
                {
                    while (ticks[hi] - ticks[lo] >= window) lo++;
                    int n = hi - lo + 1;
                    if (n > worst)
                    {
                        worst = n;
                        worstAt = label;
                    }
                }
            }

            foreach (var stage in new[] { "vs_1", "vs_2", "vs_3" })
            {
                foreach (ulong seed in new ulong[] { 1, 2 })
                {
                    var sim = TestData.Sim(TestData.Db, stage, Blades, true, seed);
                    var ai = new AutoBattleController();
                    for (int i = 0; i < 60 * 180 && sim.Outcome == BattleOutcome.InProgress; i++) sim.Step(ai.Decide(sim), true);
                    Measure(sim, $"자동 {stage} seed{seed}");
                }
                // 최악 조건: 쌍검(피니시 4연타)으로 제자리에서 계속 공격 → 다가온 적들을 한꺼번에 벤다.
                var dual = TestData.Sim(TestData.Db, stage, new[] { "tp_dualblades" }, false, 1);
                for (int i = 0; i < 60 * 60 && dual.Outcome == BattleOutcome.InProgress; i++)
                {
                    var p = PlayerInput.None;
                    p.attackHeld = true;
                    p.attackPressed = i % 6 == 0;
                    dual.Step(p);
                }
                Measure(dual, $"쌍검 연타 {stage}");
            }
            TestContext.WriteLine($"[v3 풀] 0.267초 구간 최대 동시 수요 = {worst} ({worstAt}), 풀 = {FlipbookPool.DefaultCapacity}");
            Assert.Greater(worst, 0, "베기 일반 공격 명중이 실제로 발생해야 함");
            Assert.LessOrEqual(worst, FlipbookPool.DefaultCapacity, "현재 콘텐츠에서 풀 24개를 넘지 않아야 함");
        }
    }
}
