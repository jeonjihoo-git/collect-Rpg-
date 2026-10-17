using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Progress;
using Lumina.Core.Util;

namespace Lumina.Sim
{
    /// <summary>
    /// 사용법:
    ///   dotnet run --project tools/Lumina.Sim -- flow [저장폴더]     전체 흐름 헤드리스 실행(실패 시 종료코드 1)
    ///   dotnet run --project tools/Lumina.Sim -- sfx  [출력폴더]     무기별 효과음 WAV + 전투 이벤트 기반 쇼케이스
    ///   dotnet run --project tools/Lumina.Sim -- ttk                 무기별 처치 시간 표
    /// </summary>
    public static class Program
    {
        private static int _failures;

        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "flow";
            string arg = args.Length > 1 ? args[1] : null;
            var db = GameDatabase.Load(new DirectoryDataSource(DataDir()));
            var errors = db.Validate();
            Console.WriteLine($"[data] {DataDir()}");
            Console.WriteLine(errors.Count == 0 ? "[data] 검증 통과 (오류 0)" : "[data] 오류:\n  " + string.Join("\n  ", errors));
            if (errors.Count > 0) return 1;
            switch (cmd)
            {
                case "flow": Flow(db, arg ?? Path.Combine("sim_out", "save")); break;
                case "sfx": Sfx(db, arg ?? Path.Combine("sim_out", "sfx")); break;
                case "ttk": Ttk(db); break;
                default:
                    Console.WriteLine("알 수 없는 명령: " + cmd);
                    return 2;
            }
            Console.WriteLine(_failures == 0 ? "\n[결과] 모든 확인 통과" : $"\n[결과] 실패 {_failures}건");
            return _failures == 0 ? 0 : 1;
        }

        private static string DataDir()
        {
            foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var d = new DirectoryInfo(start);
                while (d != null)
                {
                    string p = Path.Combine(d.FullName, "Assets", "Lumina", "Resources", "LuminaData");
                    if (Directory.Exists(p)) return p;
                    d = d.Parent;
                }
            }
            throw new DirectoryNotFoundException("LuminaData 폴더를 찾지 못했습니다.");
        }

        private static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "  [OK] " : "  [FAIL] ") + what);
            if (!ok) _failures++;
        }

        // ───────────────────────── flow ─────────────────────────

        private static void Flow(GameDatabase db, string saveDir)
        {
            var store = new FileSaveStore(saveDir);
            store.Delete();
            var session = new GameSession(db, store, devToolsEnabled: true);
            Console.WriteLine($"\n[로비] 계정 Lv.{session.Save.account.level} / 행동력 {session.Save.inventory.stamina} / 골드 {session.Save.inventory.gold} / 별빛 결정 {session.Save.inventory.crystals}");
            Console.WriteLine($"[로비] 보유 캐릭터 {session.Save.characters.Count}명 (전원 검증용 임시 캐릭터)");

            Console.WriteLine("\n[스테이지 선택] " + db.MapName);
            foreach (var s in db.Stages)
            {
                bool auto = session.CanUseAutoBattle(s.id, out string why);
                Console.WriteLine($"  {s.displayName,-18} 개방:{(session.IsStageUnlocked(s.id) ? "O" : "X")}  자동전투:{(auto ? "가능" : "잠금 — " + why)}  행동력 {s.staminaCost}");
            }

            Console.WriteLine("\n[파티 편성] 프리셋 1: " + string.Join(", ", session.Save.party.Current.Select(id => db.GetCharacter(id).displayName)));
            var locked = session.TryStartBattle("vs_1", true);
            Check(locked.error == StartBattleError.AutoBattleLocked, "미클리어 vs_1 자동전투 출전 거부: " + locked.message);
            Check(session.Save.inventory.stamina == 120, "거부 시 행동력 차감 없음");

            var receipt = RunStage(db, session, "vs_1", false);
            Check(receipt != null && receipt.victory, "vs_1 수동(테스트 봇 입력) 클리어");
            int crystals = session.Save.inventory.crystals;
            var again = session.CompleteBattle(receipt?.battleId, BattleOutcome.Victory, true, 1f);
            Check(ReferenceEquals(again, receipt) && session.Save.inventory.crystals == crystals, "같은 battleId 재처리 → 보상 재지급 없음");
            Check(session.CanUseAutoBattle("vs_1", out _), "클리어 후 vs_1 자동전투 해금");
            Check(!session.CanUseAutoBattle("vs_2", out _), "vs_2 는 여전히 자동전투 잠금");

            var autoReceipt = RunStage(db, session, "vs_1", true);
            Check(autoReceipt != null && autoReceipt.victory && !autoReceipt.firstClear, "vs_1 자동전투 클리어(반복 보상)");

            var r2 = RunStage(db, session, "vs_2", false);
            Check(r2 != null && r2.victory, "vs_2 클리어");
            var r3 = RunStage(db, session, "vs_3", false);
            Check(r3 != null && r3.victory, "vs_3(보스) 클리어");

            Console.WriteLine("\n[저장 재로드]");
            var reloaded = new GameSession(db, new FileSaveStore(saveDir), true);
            foreach (var s in db.Stages)
            {
                var cr = reloaded.GetClearRecord(s.id);
                Console.WriteLine($"  ClearRecord({s.id}) 클리어:{cr.isCleared} 최고별:{cr.bestStars} 횟수:{cr.clearCount} 최초보상수령:{cr.firstClearRewardClaimed}");
            }
            Check(db.Stages.All(s => reloaded.GetClearRecord(s.id).isCleared), "재로드 후 클리어 기록 유지");
            Check(reloaded.Save.inventory.crystals == session.Save.inventory.crystals, "재로드 후 재화 일치");
            Console.WriteLine($"  저장 파일: {Path.GetFullPath(store.PathMain)}");
        }

        private static BattleReceipt RunStage(GameDatabase db, GameSession session, string stageId, bool auto)
        {
            var stage = db.GetStage(stageId);
            Console.WriteLine($"\n[출전] {stage.displayName} — 자동전투 {(auto ? "ON" : "OFF")}");
            var start = session.TryStartBattle(stageId, auto);
            if (!start.Ok)
            {
                Console.WriteLine("  출전 실패: " + start.message);
                return null;
            }
            Console.WriteLine($"  battleId {start.battleId}, 행동력 → {session.Save.inventory.stamina}");
            start.setup.keepLog = true;
            var sim = new BattleSim(db, start.setup);
            // auto=false: 테스트 봇이 '수동 입력'(태그 순환 포함)을 흉내. auto=true: 게임의 자동전투 AI 그대로.
            var ai = new AutoBattleController();
            var bot = new TestBot();
            while (sim.Outcome == BattleOutcome.InProgress) sim.Step(auto ? ai.Decide(sim) : bot.Decide(sim), auto);
            var st = sim.Stats;
            Console.WriteLine($"  결과 {sim.Outcome} / {sim.Time:0.0}초 / 총 피해 {st.totalDamage:0} / 반응 {st.reactions}회 / 브레이크 {st.breaks}회 / 완벽 회피 {st.perfectDodges}회 / 피격 {st.hitsTaken}회");
            if (st.reactionCounts.Count > 0) Console.WriteLine("  반응: " + string.Join(", ", st.reactionCounts.Select(kv => $"{kv.Key}×{kv.Value}")));
            var acts = auto ? st.autoActions : st.manualActions;
            Console.WriteLine($"  {(auto ? "자동" : "입력")} 행동: " + string.Join(", ", acts.Select(kv => $"{kv.Key}×{kv.Value}")));
            for (int i = 0; i < sim.Party.Length; i++)
                Console.WriteLine($"    {sim.Party[i].def.displayName,-8} 피해 {st.memberDamage[i],8:0}  HP {sim.Party[i].hp:0}/{sim.Party[i].maxHp:0}");
            var rc = session.CompleteBattle(start.battleId, sim.Outcome, sim.AllMembersAlive, sim.Time);
            Console.WriteLine($"  별 {rc.stars} [{string.Join(",", rc.starFlags.Select(f => f ? "★" : "☆"))}]  {(rc.firstClear ? "최초" : "반복")} 보상: 결정 {rc.reward.crystals}, 골드 {rc.reward.gold}, 경험의 서 {rc.reward.expBooks}, 계정EXP {rc.reward.accountExp}  (계정 Lv.{rc.accountLevelBefore}→{rc.accountLevelAfter})");
            return rc;
        }

        // ───────────────────────── sfx ─────────────────────────

        private sealed class DummySource : IDataSource
        {
            private readonly DirectoryDataSource _inner = new DirectoryDataSource(DataDir());

            public string ReadText(string name)
            {
                string t = _inner.ReadText(name);
                if (name == GameDatabase.EnemiesFile)
                {
                    var o = Newtonsoft.Json.Linq.JObject.Parse(t);
                    ((Newtonsoft.Json.Linq.JArray)o["enemies"]).Add(Newtonsoft.Json.Linq.JObject.Parse(
                        "{\"id\":\"sim_dummy\",\"displayName\":\"청음용 더미\",\"level\":1,\"hp\":100000000,\"atk\":1,\"physDef\":100,\"magDef\":100," +
                        "\"breakTier\":\"Hell\",\"moveSpeed\":0,\"radius\":0.6,\"preferredRange\":99,\"material\":\"Flesh\"," +
                        "\"attacks\":[{\"id\":\"none\",\"name\":\"없음\",\"shape\":\"Circle\",\"telegraph\":1,\"recovery\":1,\"cooldown\":9999,\"coef\":0,\"radius\":0.1,\"minRange\":999,\"maxRange\":999}]}"));
                    return o.ToString();
                }
                return t;
            }
        }

        private static void Sfx(GameDatabase realDb, string outDir)
        {
            Directory.CreateDirectory(Path.Combine(outDir, "parts"));
            var db = GameDatabase.Load(new DummySource());
            var router = new SfxRouter(db);
            var cache = new Dictionary<string, float[]>();
            var showcaseAll = new List<float>();
            int idx = 1;
            Console.WriteLine($"\n[효과음] 출력 폴더: {Path.GetFullPath(outDir)}");
            foreach (var c in db.Characters.OrderBy(c => (int)c.weapon))
            {
                var mix = ShowcaseFromSim(db, c, router, cache, out string timeline);
                string name = $"{idx:00}_{c.weapon}_{WeaponInfo.Ko(c.weapon)}";
                WavWriter.Write(Path.Combine(outDir, name + "_showcase.wav"), mix);
                showcaseAll.AddRange(mix);
                showcaseAll.AddRange(new float[Dsp.N(0.7f)]);
                Console.WriteLine($"  {name}_showcase.wav  ({mix.Length / (float)Dsp.SampleRate:0.0}s)  {timeline}");
                var cat = WeaponInfo.Category(c.weapon);
                foreach (var kind in cat == WeaponCategory.Melee ? new[] { SfxKind.Swing, SfxKind.Impact, SfxKind.Tail } : new[] { SfxKind.Cast, SfxKind.Projectile, SfxKind.Impact, SfxKind.Tail })
                {
                    foreach (var tier in new[] { SfxTier.Light, SfxTier.Finisher, SfxTier.Ultimate })
                    {
                        var x = SfxSynth.RenderWeapon(c.weapon, kind, tier, Element.None, 0, 0.3f);
                        WavWriter.Write(Path.Combine(outDir, "parts", $"{name}_{kind}_{tier}.wav".Replace(" ", "")), x);
                    }
                }
                idx++;
            }
            WavWriter.Write(Path.Combine(outDir, "00_all_weapons_showcase.wav"), showcaseAll.ToArray());
            Console.WriteLine($"  00_all_weapons_showcase.wav  ({showcaseAll.Count / (float)Dsp.SampleRate:0.0}s, 16무기 연속)");
            foreach (SystemSfx s in Enum.GetValues(typeof(SystemSfx)))
                WavWriter.Write(Path.Combine(outDir, "parts", $"system_{s}.wav"), SfxSynth.RenderSystem(s, Element.Wind, Element.Lightning, 0));
        }

        /// <summary>
        /// 실제 전투 시뮬레이션 이벤트 로그를 SfxRouter 로 변환해 이벤트 틱 시각에 배치 → 게임과 같은 타이밍의 청음 파일.
        /// 시나리오: 일반 콤보 2회전 → 스킬 → 궁극기.
        /// </summary>
        private static float[] ShowcaseFromSim(GameDatabase db, CharacterDef c, SfxRouter router, Dictionary<string, float[]> cache, out string timeline)
        {
            var stage = new StageDef
            {
                id = "sim", displayName = "sim", waves = new[] { new WaveDef { spawns = new[] { new SpawnDef { enemy = "sim_dummy", x = 0f, y = -2.2f } } } },
                firstClearReward = new RewardDef(), repeatReward = new RewardDef()
            };
            var setup = new BattleSetup { stage = stage, seed = 9, keepLog = true };
            setup.party.Add(new PartyMemberSnapshot { characterId = c.id });
            var sim = new BattleSim(db, setup);
            var perTick = new List<(int tick, List<SfxRequest> reqs)>();
            void Step(PlayerInput i)
            {
                sim.Step(i);
                var reqs = router.RouteTick(sim.Events);
                if (reqs.Count > 0) perTick.Add((sim.Tick, new List<SfxRequest>(reqs)));
            }
            for (int i = 0; i < 60; i++) Step(PlayerInput.None);
            int startTick = sim.Tick;
            int combo = db.GetWeapon(c.weapon).combo.Length;
            var hold = new PlayerInput { tagTo = -1, attackHeld = true };
            int actions = 0;
            int guard = 0;
            while (actions < combo * 2 && guard++ < 1200)
            {
                int before = sim.Log.Count(e => e.type == CombatEventType.ActionStart);
                Step(hold);
                if (sim.Log.Count(e => e.type == CombatEventType.ActionStart) > before) actions++;
            }
            while (sim.Body.action != ActionKind.None && guard++ < 1500) Step(PlayerInput.None);
            for (int i = 0; i < 30; i++) Step(PlayerInput.None);
            Step(new PlayerInput { tagTo = -1, skillPressed = true });
            for (int i = 0; i < 80; i++) Step(PlayerInput.None);
            sim.DebugAddResonance(100f);
            Step(new PlayerInput { tagTo = -1, ultimatePressed = true });
            for (int i = 0; i < 150; i++) Step(PlayerInput.None);

            var outBuf = new float[Dsp.N((sim.Tick - startTick) / 60f + 1.8f)];
            var variants = new Dictionary<string, int>();
            int hits = sim.Log.Count(e => e.type == CombatEventType.Hit);
            foreach (var (tick, reqs) in perTick)
            {
                if (tick < startTick) continue;
                int at = Dsp.N((tick - startTick) / 60f);
                foreach (var r in reqs)
                {
                    if (r.isSystem && r.system == SystemSfx.UltReady) continue;
                    variants.TryGetValue(r.Key, out int v);
                    variants[r.Key] = v + 1;
                    string key = r.Key + "#" + (v % 2);
                    if (!cache.TryGetValue(key, out var clip)) cache[key] = clip = r.Render(v % 2);
                    float vol = r.volume <= 0f ? 1f : r.volume;
                    for (int i = 0; i < clip.Length && at + i < outBuf.Length; i++) outBuf[at + i] += clip[i] * vol * 0.8f;
                }
            }
            for (int i = 0; i < outBuf.Length; i++) outBuf[i] = MathF.Tanh(outBuf[i]);
            timeline = $"행동 {sim.Log.Count(e => e.type == CombatEventType.ActionStart)} / 명중 {hits}";
            return outBuf;
        }

        // ───────────────────────── ttk ─────────────────────────

        private static void Ttk(GameDatabase db)
        {
            Console.WriteLine("\n[TTK] 단독 1인(태그 없음) vs 검증용 근접 환영 1체, 테스트 봇 조작, 시드 3종 평균");
            Console.WriteLine("  목표 참고: GDD '일반 적 1체 8~15초'(파티 기준). 단독 수치는 무기 간 상대 비교용.");
            var target = db.GetEnemy("test_melee");
            foreach (var c in db.Characters.OrderBy(c => (int)c.weapon))
            {
                var times = new List<float>();
                int deaths = 0;
                foreach (ulong seed in new ulong[] { 1, 2, 3 })
                {
                    var stage = new StageDef
                    {
                        id = "ttk", displayName = "ttk", waves = new[] { new WaveDef { spawns = new[] { new SpawnDef { enemy = target.id, x = 0f, y = 4f } } } },
                        firstClearReward = new RewardDef(), repeatReward = new RewardDef()
                    };
                    var setup = new BattleSetup { stage = stage, seed = seed };
                    setup.party.Add(new PartyMemberSnapshot { characterId = c.id });
                    var sim = new BattleSim(db, setup);
                    var bot = new AutoBattleController();
                    while (sim.Outcome == BattleOutcome.InProgress) sim.Step(bot.Decide(sim), true);
                    if (sim.Outcome == BattleOutcome.Victory) times.Add(sim.Time - db.Balance.temp.spawnGrace);
                    else deaths++;
                }
                string avg = times.Count > 0 ? $"{times.Average():0.0}s" : "-";
                Console.WriteLine($"  {WeaponInfo.Ko(c.weapon),-4} {c.displayName,-10} 평균 {avg,7}  (패배 {deaths})");
            }
        }
    }
}
