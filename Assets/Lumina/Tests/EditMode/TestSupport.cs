using System;
using System.Collections.Generic;
using System.IO;
using Lumina.Core.Combat;
using Lumina.Core.Data;
using Newtonsoft.Json.Linq;

namespace Lumina.Tests
{
    /// <summary>테스트 공용: 저장소 루트/데이터 위치 탐색, 테스트 전용 더미 적·스테이지 주입.</summary>
    public static class TestData
    {
        private static GameDatabase _db;
        private static GameDatabase _dummyDb;
        private static string _root;

        public static string RepoRoot
        {
            get
            {
                if (_root != null) return _root;
                foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
                {
                    var d = new DirectoryInfo(start);
                    while (d != null)
                    {
                        if (Directory.Exists(Path.Combine(d.FullName, "Assets", "Lumina", "Resources", "LuminaData")))
                        {
                            _root = d.FullName;
                            return _root;
                        }
                        d = d.Parent;
                    }
                }
                throw new DirectoryNotFoundException("Assets/Lumina/Resources/LuminaData 를 찾을 수 없습니다.");
            }
        }

        public static string DataDir => Path.Combine(RepoRoot, "Assets", "Lumina", "Resources", "LuminaData");

        public static JObject MachineRules => JObject.Parse(File.ReadAllText(Path.Combine(RepoRoot, "docs", "handoff", "04_MACHINE_RULES.json")));

        public static GameDatabase Db => _db ?? (_db = GameDatabase.Load(new DirectoryDataSource(DataDir)));

        /// <summary>
        /// 실데이터 + 테스트 전용 적/스테이지:
        /// dummy(공격 안 함, HP 큼), striker(제자리 원형 공격만, 주기적), stage "t_dummy"/"t_striker".
        /// </summary>
        public static GameDatabase DummyDb => _dummyDb ?? (_dummyDb = GameDatabase.Load(new OverrideSource()));

        private sealed class OverrideSource : IDataSource
        {
            private readonly DirectoryDataSource _inner = new DirectoryDataSource(DataDir);

            public string ReadText(string name)
            {
                string text = _inner.ReadText(name);
                if (name == GameDatabase.EnemiesFile)
                {
                    var o = JObject.Parse(text);
                    var arr = (JArray)o["enemies"];
                    arr.Add(JObject.Parse(@"{ ""id"": ""t_dummy"", ""displayName"": ""테스트 더미"", ""level"": 1, ""hp"": 1000000, ""atk"": 100,
                        ""physDef"": 100, ""magDef"": 100, ""breakTier"": ""Normal"", ""moveSpeed"": 0, ""radius"": 0.6, ""preferredRange"": 99,
                        ""material"": ""Flesh"", ""attacks"": [ { ""id"": ""none"", ""name"": ""없음"", ""shape"": ""Circle"", ""telegraph"": 1, ""recovery"": 1,
                        ""cooldown"": 9999, ""coef"": 0, ""radius"": 0.1, ""minRange"": 999, ""maxRange"": 999 } ] }"));
                    arr.Add(JObject.Parse(@"{ ""id"": ""t_striker"", ""displayName"": ""테스트 타격수"", ""level"": 1, ""hp"": 1000000, ""atk"": 300,
                        ""physDef"": 100, ""magDef"": 100, ""breakTier"": ""Normal"", ""moveSpeed"": 0, ""radius"": 0.6, ""preferredRange"": 99,
                        ""material"": ""Armor"", ""attacks"": [ { ""id"": ""slam"", ""name"": ""테스트 강타"", ""shape"": ""SelfRing"", ""telegraph"": 0.5, ""recovery"": 0.5,
                        ""cooldown"": 3, ""coef"": 1.0, ""radius"": 5, ""minRange"": 0, ""maxRange"": 99 } ] }"));
                    arr.Add(JObject.Parse(@"{ ""id"": ""t_weak"", ""displayName"": ""테스트 약체"", ""level"": 1, ""hp"": 1, ""atk"": 100,
                        ""physDef"": 0, ""magDef"": 0, ""breakTier"": ""Normal"", ""moveSpeed"": 0, ""radius"": 0.6, ""preferredRange"": 99,
                        ""material"": ""Flesh"", ""attacks"": [ { ""id"": ""none"", ""name"": ""없음"", ""shape"": ""Circle"", ""telegraph"": 1, ""recovery"": 1,
                        ""cooldown"": 9999, ""coef"": 0, ""radius"": 0.1, ""minRange"": 999, ""maxRange"": 999 } ] }"));
                    return o.ToString();
                }
                if (name == GameDatabase.StagesFile)
                {
                    var o = JObject.Parse(text);
                    var arr = (JArray)o["stages"];
                    arr.Add(Stage("t_dummy", "t_dummy", 0f, -2.2f));
                    arr.Add(Stage("t_striker", "t_striker", 0f, -1.0f));
                    arr.Add(Stage("t_weak", "t_weak", 0f, -2.2f));
                    return o.ToString();
                }
                return text;
            }

            private static JObject Stage(string id, string enemy, float x, float y)
            {
                return JObject.Parse($@"{{ ""id"": ""{id}"", ""displayName"": ""{id}"", ""order"": 900, ""unlockAfter"": null, ""staminaCost"": 0,
                    ""difficulty"": ""Normal"", ""waves"": [ {{ ""spawns"": [ {{ ""enemy"": ""{enemy}"", ""x"": {x}, ""y"": {y} }} ] }} ],
                    ""firstClearReward"": {{ ""crystals"": 0, ""gold"": 0, ""expBooks"": 0, ""accountExp"": 0 }},
                    ""repeatReward"": {{ ""crystals"": 0, ""gold"": 0, ""expBooks"": 0, ""accountExp"": 0 }} }}");
            }
        }

        public static BattleSim Sim(GameDatabase db, string stageId, string[] party, bool auto = false, ulong seed = 1)
        {
            var setup = new BattleSetup { stage = db.GetStage(stageId), autoBattle = auto, seed = seed, keepLog = true };
            foreach (var id in party) setup.party.Add(new PartyMemberSnapshot { characterId = id });
            return new BattleSim(db, setup);
        }

        /// <summary>적이 등장 유예를 마칠 때까지 대기.</summary>
        public static void WaitSpawn(BattleSim sim)
        {
            for (int i = 0; i < 70; i++) sim.Step(PlayerInput.None);
        }

        public static void Idle(BattleSim sim, float seconds)
        {
            int n = (int)Math.Round(seconds * BattleSim.TicksPerSecond);
            for (int i = 0; i < n; i++) sim.Step(PlayerInput.None);
        }

        public static PlayerInput Press(Func<PlayerInput, PlayerInput> f) => f(PlayerInput.None);

        public static List<CombatEvent> EventsOf(BattleSim sim, CombatEventType type)
        {
            return sim.Log.FindAll(e => e.type == type);
        }
    }
}
