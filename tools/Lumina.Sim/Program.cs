using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumina.Core.Audio;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Lumina.Core.Presentation;
using Lumina.Core.Progress;
using Lumina.Core.Util;

namespace Lumina.Sim
{
    /// <summary>
    /// 사용법:
    ///   dotnet run --project tools/Lumina.Sim -- flow [저장폴더]     전체 흐름 헤드리스 실행(실패 시 종료코드 1)
    ///   dotnet run --project tools/Lumina.Sim -- sfx  [출력폴더]     무기별 효과음 WAV + 전투 이벤트 기반 쇼케이스
    ///   dotnet run --project tools/Lumina.Sim -- ttk                 무기별 처치 시간 표
    ///   dotnet run --project tools/Lumina.Sim -- feel                '이동하며 연타' 조작의 헛스윙/끊김 진단
    ///   dotnet run --project tools/Lumina.Sim -- slots [md]          실제 음원 교체 슬롯 목록 문서 생성
    ///   dotnet run --project tools/Lumina.Sim -- motion [json]       16무기 모션 관절 좌표(미리보기 영상용, tools/preview/render_motion.py)
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
                case "feel": Feel(db); break;
                case "slots": Slots(arg ?? Path.Combine("docs", "SFX_ASSET_SLOTS.md")); break;
                case "motion": Motion(db, arg ?? Path.Combine("sim_out", "motion.json")); break;
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

        // ───────────────────────── feel ─────────────────────────

        /// <summary>
        /// 사람이 '조이스틱을 적 쪽으로 계속 밀면서 공격을 연타'하는 조작을 흉내 내 일반공격만으로 15초간 싸운다(회피·스킬 없음).
        /// 헛스윙률 = 피격 없이도 명중하지 못한 일반공격 비율, 끊김률 = 명중 전에 피격으로 취소된 일반공격 비율(회피 미사용 조건).
        /// </summary>
        private static void Feel(GameDatabase db)
        {
            Console.WriteLine("\n[FEEL] 이동+연타(0.15초 간격) 조작, 일반공격만, vs 검증용 근접 환영 1체, 15초 × 시드 3");
            Console.WriteLine($"  {"무기",-5}{"헛스윙",8}{"피격끊김",9}{"명중/초",8}{"콤보 1회전 넉백",14}");
            float sumWhiff = 0f, sumInt = 0f;
            int n = 0;
            foreach (var c in db.Characters.OrderBy(c => (int)c.weapon))
            {
                int actions = 0, whiffs = 0, interrupted = 0, hits = 0;
                float time = 0f;
                foreach (ulong seed in new ulong[] { 1, 2, 3 })
                {
                    var stage = new StageDef
                    {
                        id = "feel", displayName = "feel", waves = new[] { new WaveDef { spawns = new[] { new SpawnDef { enemy = "test_melee", x = 0f, y = 4f } } } },
                        firstClearReward = new RewardDef(), repeatReward = new RewardDef()
                    };
                    var setup = new BattleSetup { stage = stage, seed = seed, keepLog = true };
                    setup.party.Add(new PartyMemberSnapshot { characterId = c.id });
                    var sim = new BattleSim(db, setup);
                    // 적 등장 유예(타깃 불가) 동안은 대기 — 측정 왜곡 방지
                    while (sim.FindNearestEnemy(sim.Body.pos, 99f) < 0 && sim.Tick < 120) sim.Step(PlayerInput.None);
                    int begin = sim.Tick;
                    for (int i = 0; i < 60 * 15 && sim.Outcome == BattleOutcome.InProgress; i++)
                    {
                        var input = PlayerInput.None;
                        int t = sim.FindNearestEnemy(sim.Body.pos, 99f);
                        if (t >= 0) input.move = (sim.FindEnemy(t).pos - sim.Body.pos).Normalized;
                        input.attackPressed = i % 9 == 0;
                        sim.Step(input);
                    }
                    time += sim.Time - begin * BattleSim.Dt;
                    var log = sim.Log;
                    var starts = log.Where(e => e.type == CombatEventType.ActionStart && e.action == ActionKind.Normal && e.tick > begin).ToList();
                    foreach (var st in starts)
                    {
                        int end = log.Where(e => e.tick > st.tick && (e.type == CombatEventType.ActionStart || e.type == CombatEventType.Dodge)).Select(e => e.tick).DefaultIfEmpty(int.MaxValue).First();
                        bool hit = log.Any(e => e.type == CombatEventType.Hit && e.action == ActionKind.Normal && e.tick >= st.tick && e.tick < end + 40 &&
                                                (WeaponInfo.Category(c.weapon) != WeaponCategory.Melee || e.tick < end));
                        bool cut = log.Any(e => e.type == CombatEventType.PlayerHit && e.tick >= st.tick && e.tick < end) && !hit;
                        actions++;
                        if (cut) interrupted++;
                        else if (!hit) whiffs++;
                    }
                    hits += log.Count(e => e.type == CombatEventType.Hit && e.tick > begin);
                }
                var combo = db.GetWeapon(c.weapon).combo;
                float kb = combo.Sum(sp => sp.knockback * sp.SafeHits);
                float w = actions > 0 ? whiffs * 100f / actions : 0f;
                float it = actions > 0 ? interrupted * 100f / actions : 0f;
                sumWhiff += w;
                sumInt += it;
                n++;
                Console.WriteLine($"  {WeaponInfo.Ko(c.weapon),-5}{w,7:0}%{it,8:0}%{hits / Math.Max(1f, time),8:0.0}{kb,12:0.00}m");
            }
            Console.WriteLine($"  평균 헛스윙 {sumWhiff / n:0}% · 평균 피격끊김 {sumInt / n:0}%");
        }

        // ───────────────────────── slots ─────────────────────────

        /// <summary>Resources/LuminaSfx/ 교체 음원 슬롯 목록(사운드 디자이너 전달용).</summary>
        private static void Slots(string outPath)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# 실제 음원 교체 슬롯 (자동 생성: `dotnet run --project tools/Lumina.Sim -- slots`)");
            sb.AppendLine();
            sb.AppendLine("`Assets/Lumina/Resources/LuminaSfx/` 아래에 **아래 경로 + 확장자(.wav/.ogg)** 로 파일을 넣으면, 그 소리가 절차 합성음 대신 재생됩니다. 코드 수정 불필요.");
            sb.AppendLine("- 등급(Tier)별 파일이 없으면 `종류` 공통 파일(예: `Longsword/Impact.wav`)을 찾고, 그것도 없으면 합성음을 씁니다.");
            sb.AppendLine("- 명중음(Impact)은 **피해가 들어간 그 틱**에 재생됩니다. 파일 앞부분에 무음이 있으면 타격감이 늦어지므로 **앞 무음 0ms** 로 잘라 주세요.");
            sb.AppendLine("- 휘두름(Swing)은 **명중 시각보다 선행 시간만큼 먼저** 재생됩니다. 소리의 가장 큰 지점이 파일 시작에서 선행 시간 위치에 오도록 만들어 주세요.");
            sb.AppendLine("- 같은 소리 반복감 방지를 위해 재생 시 피치가 ±3% 무작위로 흔들립니다.");
            sb.AppendLine();
            sb.AppendLine("| 무기 | 슬롯 경로(등급별 파일 우선 → 없으면 공통 파일) | 재생 시점 | 길이 가이드 | 휘두름 선행 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var w in WeaponInfo.All)
            {
                bool melee = WeaponInfo.Category(w) == WeaponCategory.Melee;
                var kinds = melee ? new[] { SfxKind.Swing, SfxKind.Impact, SfxKind.Tail } : new[] { SfxKind.Cast, SfxKind.Projectile, SfxKind.Impact, SfxKind.Tail };
                foreach (var k in kinds)
                {
                    string when = k == SfxKind.Swing ? "명중 − 선행" : k == SfxKind.Cast ? "공격 시작(당김·장전·충전)" : k == SfxKind.Projectile ? "발사·발동" : k == SfxKind.Impact ? "명중(피해 적용 틱)" : "강타 명중 잔향";
                    string len = k == SfxKind.Swing ? "0.1~0.45초" : k == SfxKind.Cast ? "선딜 길이(0.06~0.85초)" : k == SfxKind.Impact ? (w == WeaponType.Launcher ? "0.8~1.5초" : "0.1~0.5초") : k == SfxKind.Tail ? "0.3~1.5초" : "0.1~0.5초";
                    string lead = k == SfxKind.Swing ? $"{SfxTiming.SwingLead(w) * 1000:0}ms" : "-";
                    sb.AppendLine($"| {WeaponInfo.Ko(w)} | `{w}/{k}_<Light·Medium·Heavy·Finisher·Skill·Ultimate>` → `{w}/{k}` | {when} | {len} | {lead} |");
                }
            }
            sb.AppendLine();
            sb.AppendLine("| 대상 재질(피격 반응) | 슬롯 경로 |");
            sb.AppendLine("|---|---|");
            foreach (EnemyMaterial m in Enum.GetValues(typeof(EnemyMaterial))) sb.AppendLine($"| {m} | `Reaction/{m}_<Tier>` → `Reaction/{m}` |");
            sb.AppendLine();
            sb.AppendLine("| 시스템 | 슬롯 경로 |");
            sb.AppendLine("|---|---|");
            foreach (SystemSfx x in Enum.GetValues(typeof(SystemSfx))) sb.AppendLine($"| {x} | `System/{x}` |");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, sb.ToString());
            Console.WriteLine($"\n[슬롯] {Path.GetFullPath(outPath)}");
        }

        // ───────────────────────── motion ─────────────────────────

        /// <summary>16무기: 일반 콤보 전체 → 스킬 → 궁극기를 30fps 로 샘플링한 관절 좌표(Core MotionLibrary 그대로).</summary>
        private static void Motion(GameDatabase db, string outPath)
        {
            const float fps = 60f;
            var all = new List<object>();
            foreach (var wd in db.Weapons.Values.OrderBy(x => (int)x.id))
            {
                var frames = new List<object>();
                var segs = new List<(ActionKind kind, int step, AttackStepDef def, string label)>();
                for (int i = 0; i < wd.combo.Length; i++) segs.Add((ActionKind.Normal, i, wd.combo[i], $"Normal {i + 1}"));
                segs.Add((ActionKind.Skill, 0, wd.skill, "Skill"));
                segs.Add((ActionKind.Ultimate, 0, wd.ultimate, "Ultimate"));
                object Frame(Pose p, bool hit, string label)
                {
                    var j = MotionLibrary.Solve(wd.id, p);
                    var tl = MotionLibrary.TipLeft(wd.id, p);
                    float[] P(V3 v) => new[] { (float)Math.Round(v.x, 3), (float)Math.Round(v.y, 3), (float)Math.Round(v.z, 3) };
                    return new
                    {
                        footR = P(j.footR), footL = P(j.footL), hipR = P(j.hipR), hipL = P(j.hipL), waist = P(j.waist), neck = P(j.neck), head = P(j.head),
                        shR = P(j.shoulderR), shL = P(j.shoulderL), handR = P(j.handR), handL = P(j.handL), tip = P(j.tip), grip = P(j.grip), tipL = P(tl),
                        trail = p.trail > 0.5f, hit, label
                    };
                }
                void Add(Pose p, bool hit, string label) => frames.Add(Frame(p, hit, label));
                var c0 = wd.combo[0];
                object contact = Frame(MotionLibrary.Attack(wd.id, ActionKind.Normal, 0, c0, c0.startup, out _, out _), true, "Normal 1 @hit");
                for (int f = 0; f < 9; f++) Add(MotionLibrary.Rest(wd.id), false, "Idle");
                foreach (var sg in segs)
                {
                    int n = (int)MathF.Ceiling((sg.def.TotalDuration + 0.05f) * fps);
                    for (int f = 0; f < n; f++)
                    {
                        float t = f / fps;
                        bool hit = false;
                        for (int h = 0; h < sg.def.SafeHits; h++)
                        {
                            float H = sg.def.startup + h * sg.def.hitInterval;
                            if (H >= t - 0.5f / fps && H < t + 0.5f / fps) hit = true;
                        }
                        Add(MotionLibrary.Attack(wd.id, sg.kind, sg.step, sg.def, t, out _, out _), hit, sg.label);
                    }
                    for (int f = 0; f < 6; f++) Add(MotionLibrary.Rest(wd.id), false, sg.label);
                }
                all.Add(new { weapon = wd.id.ToString(), twoHanded = MotionLibrary.Profile(wd.id).twoHanded, dual = MotionLibrary.Profile(wd.id).dual, contact, frames });
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, Newtonsoft.Json.JsonConvert.SerializeObject(new { fps, weapons = all }));
            Console.WriteLine($"\n[모션] {Path.GetFullPath(outPath)}");
        }
    }
}
