using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Lumina.Core.Data
{
    /// <summary>데이터 파일 공급원. Unity는 Resources, dotnet 테스트/도구는 파일 시스템을 사용한다.</summary>
    public interface IDataSource
    {
        /// <summary>확장자 없는 이름(예: "weapons")의 텍스트를 반환. 없으면 null.</summary>
        string ReadText(string name);
    }

    public sealed class DirectoryDataSource : IDataSource
    {
        private readonly string _dir;

        public DirectoryDataSource(string dir)
        {
            _dir = dir;
        }

        public string ReadText(string name)
        {
            string path = Path.Combine(_dir, name + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    // JSON 역직렬화로만 채워지는 파일 래퍼(CS0649 경고 억제)
#pragma warning disable 0649
    [Serializable]
    internal class WeaponFile { public WeaponTypeDef[] weapons; }

    [Serializable]
    internal class CharacterFile { public string note; public CharacterDef[] characters; }

    [Serializable]
    internal class RosterFile { public string note; public RosterEntry[] roster; }

    [Serializable]
    internal class EnemyFile { public EnemyDef[] enemies; }

    [Serializable]
    internal class StageFile { public string mapName; public string mapNote; public StageDef[] stages; }

    [Serializable]
    internal class ReactionFile { public string note; public ElementReactionDef[] reactions; }
#pragma warning restore 0649

    /// <summary>
    /// 모든 정의 데이터의 단일 진입점. 로드 후 Validate()로 참조 누락/중복/규칙 위반을 검사한다.
    /// </summary>
    public sealed class GameDatabase
    {
        public const string BalanceFile = "balance";
        public const string ReactionsFile = "reactions";
        public const string WeaponsFile = "weapons";
        public const string CharactersFile = "characters_test";
        public const string RosterFile = "roster_canon";
        public const string EnemiesFile = "enemies";
        public const string StagesFile = "stages";

        public BalanceDef Balance { get; private set; }
        public IReadOnlyList<ElementReactionDef> Reactions { get; private set; }
        public IReadOnlyDictionary<WeaponType, WeaponTypeDef> Weapons { get; private set; }
        public IReadOnlyList<CharacterDef> Characters { get; private set; }
        public IReadOnlyList<RosterEntry> RosterCanon { get; private set; }
        public IReadOnlyList<EnemyDef> Enemies { get; private set; }
        public IReadOnlyList<StageDef> Stages { get; private set; }
        public string MapName { get; private set; }

        private Dictionary<string, CharacterDef> _charById;
        private Dictionary<string, EnemyDef> _enemyById;
        private Dictionary<string, StageDef> _stageById;
        private readonly List<string> _loadErrors = new List<string>();

        public static GameDatabase Load(IDataSource src)
        {
            var db = new GameDatabase();
            db.Balance = db.Read<BalanceDef>(src, BalanceFile) ?? new BalanceDef();
            db.Reactions = db.Read<ReactionFile>(src, ReactionsFile)?.reactions ?? Array.Empty<ElementReactionDef>();

            var weapons = db.Read<WeaponFile>(src, WeaponsFile)?.weapons ?? Array.Empty<WeaponTypeDef>();
            var wdict = new Dictionary<WeaponType, WeaponTypeDef>();
            foreach (var w in weapons)
            {
                if (wdict.ContainsKey(w.id)) db._loadErrors.Add("무기 중복 정의: " + w.id);
                else wdict[w.id] = w;
            }
            db.Weapons = wdict;

            db.Characters = db.Read<CharacterFile>(src, CharactersFile)?.characters ?? Array.Empty<CharacterDef>();
            db.RosterCanon = db.Read<RosterFile>(src, RosterFile)?.roster ?? Array.Empty<RosterEntry>();
            db.Enemies = db.Read<EnemyFile>(src, EnemiesFile)?.enemies ?? Array.Empty<EnemyDef>();
            var stageFile = db.Read<StageFile>(src, StagesFile);
            db.Stages = (stageFile?.stages ?? Array.Empty<StageDef>()).OrderBy(s => s.order).ToList();
            db.MapName = stageFile?.mapName ?? "";

            db._charById = new Dictionary<string, CharacterDef>();
            foreach (var c in db.Characters)
            {
                if (c.id == null || db._charById.ContainsKey(c.id)) db._loadErrors.Add("캐릭터 ID 중복/누락: " + c.id);
                else db._charById[c.id] = c;
            }
            db._enemyById = new Dictionary<string, EnemyDef>();
            foreach (var e in db.Enemies)
            {
                if (e.id == null || db._enemyById.ContainsKey(e.id)) db._loadErrors.Add("적 ID 중복/누락: " + e.id);
                else db._enemyById[e.id] = e;
            }
            db._stageById = new Dictionary<string, StageDef>();
            foreach (var s in db.Stages)
            {
                if (s.id == null || db._stageById.ContainsKey(s.id)) db._loadErrors.Add("스테이지 ID 중복/누락: " + s.id);
                else db._stageById[s.id] = s;
            }
            return db;
        }

        private T Read<T>(IDataSource src, string name) where T : class
        {
            string text = src.ReadText(name);
            if (text == null)
            {
                _loadErrors.Add("데이터 파일 없음: " + name + ".json");
                return null;
            }
            try
            {
                return LuminaJson.Deserialize<T>(text);
            }
            catch (Exception ex)
            {
                _loadErrors.Add("데이터 파싱 실패: " + name + ".json — " + ex.Message);
                return null;
            }
        }

        public CharacterDef GetCharacter(string id) => id != null && _charById.TryGetValue(id, out var c) ? c : null;
        public EnemyDef GetEnemy(string id) => id != null && _enemyById.TryGetValue(id, out var e) ? e : null;
        public StageDef GetStage(string id) => id != null && _stageById.TryGetValue(id, out var s) ? s : null;
        public WeaponTypeDef GetWeapon(WeaponType w) => Weapons.TryGetValue(w, out var d) ? d : null;

        /// <summary>순서 무관 반응 조회. 같은 속성/무반응 조합은 null.</summary>
        public ElementReactionDef FindReaction(Element x, Element y)
        {
            if (x == y || x == Element.None || y == Element.None) return null;
            foreach (var r in Reactions)
            {
                if (r.Matches(x, y)) return r;
            }
            return null;
        }

        /// <summary>데이터 정합성 검사. 비어 있으면 통과.</summary>
        public List<string> Validate()
        {
            var errors = new List<string>(_loadErrors);

            // 무기 16종 전부 정의 + 분류 일치
            foreach (WeaponType w in WeaponInfo.All)
            {
                if (!Weapons.TryGetValue(w, out var def))
                {
                    errors.Add("무기 정의 누락: " + WeaponInfo.Ko(w));
                    continue;
                }
                if (def.category != WeaponInfo.Category(w)) errors.Add("무기 분류 불일치: " + w);
                if (def.koName != WeaponInfo.Ko(w)) errors.Add("무기 한글명 불일치: " + w + " / " + def.koName);
                if (def.combo == null || def.combo.Length == 0) errors.Add("일반공격 콤보 없음: " + w);
                if (def.skill == null) errors.Add("스킬 모션 없음: " + w);
                if (def.ultimate == null) errors.Add("궁극기 모션 없음: " + w);
                foreach (var step in AllSteps(def))
                {
                    if (step.startup < 0 || step.recovery < 0) errors.Add("음수 타이밍: " + w + "/" + step.name);
                    if (step.SafeHits > 1 && step.hitInterval <= 0) errors.Add("다단히트 간격 없음: " + w + "/" + step.name);
                    if ((step.shape == HitShape.Projectile || step.shape == HitShape.Ring) && step.speed <= 0)
                        errors.Add("투사체/링 속도 없음: " + w + "/" + step.name);
                }
            }

            // 반응 13종, 서로 다른 속성 쌍, 중복 없음
            if (Reactions.Count != 13) errors.Add("속성 반응 수가 13이 아님: " + Reactions.Count);
            for (int i = 0; i < Reactions.Count; i++)
            {
                var r = Reactions[i];
                if (r.a == r.b || r.a == Element.None || r.b == Element.None) errors.Add("잘못된 반응 쌍: " + r.name);
                for (int j = i + 1; j < Reactions.Count; j++)
                {
                    if (Reactions[j].Matches(r.a, r.b)) errors.Add("반응 쌍 중복: " + r.name + " / " + Reactions[j].name);
                }
            }

            // 캐릭터: 무기 참조, 스킬 정의
            foreach (var c in Characters)
            {
                if (!Weapons.ContainsKey(c.weapon)) errors.Add("캐릭터 무기 참조 누락: " + c.id);
                if (c.element == Element.None) errors.Add("캐릭터 속성 없음: " + c.id);
                if (c.skill == null || c.ultimate == null) errors.Add("캐릭터 스킬/궁극기 누락: " + c.id);
                if (c.baseHp <= 0 || c.baseAtk <= 0) errors.Add("캐릭터 기본 스탯 오류: " + c.id);
            }

            // 정식 로스터 16명: 전투 데이터와 이름 혼용 금지
            if (RosterCanon.Count != 16) errors.Add("정식 로스터 수가 16이 아님: " + RosterCanon.Count);
            var canonNames = new HashSet<string>(RosterCanon.Select(r => r.name));
            foreach (var c in Characters)
            {
                if (c.isTestProxy && canonNames.Contains(c.displayName))
                    errors.Add("검증용 캐릭터가 정식 로스터 이름을 사용함: " + c.displayName);
            }

            // 적: 공격 정의
            foreach (var e in Enemies)
            {
                if (e.attacks == null || e.attacks.Length == 0) errors.Add("적 공격 없음: " + e.id);
                if (e.hp <= 0) errors.Add("적 HP 오류: " + e.id);
            }

            // 스테이지: 적 참조, 해금 체인
            foreach (var s in Stages)
            {
                if (s.waves == null || s.waves.Length == 0) errors.Add("스테이지 웨이브 없음: " + s.id);
                else
                {
                    foreach (var wv in s.waves)
                    {
                        if (wv.spawns == null || wv.spawns.Length == 0) errors.Add("빈 웨이브: " + s.id);
                        else
                        {
                            foreach (var sp in wv.spawns)
                            {
                                if (GetEnemy(sp.enemy) == null) errors.Add("스테이지 적 참조 누락: " + s.id + " → " + sp.enemy);
                            }
                        }
                    }
                }
                if (s.unlockAfter != null && GetStage(s.unlockAfter) == null) errors.Add("해금 선행 스테이지 누락: " + s.id);
                if (s.firstClearReward == null || s.repeatReward == null) errors.Add("보상 정의 누락: " + s.id);
                if (s.staminaCost < 0) errors.Add("행동력 비용 음수: " + s.id);
            }
            return errors;
        }

        public static IEnumerable<AttackStepDef> AllSteps(WeaponTypeDef def)
        {
            if (def.combo != null)
            {
                foreach (var s in def.combo) yield return s;
            }
            if (def.skill != null) yield return def.skill;
            if (def.ultimate != null) yield return def.ultimate;
        }
    }
}
