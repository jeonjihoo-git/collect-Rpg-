using System;
using System.IO;
using Lumina.Core.Data;

namespace Lumina.Core.Progress
{
    public interface ISaveStore
    {
        /// <summary>저장 데이터가 없으면 null.</summary>
        SaveData Load();
        void Save(SaveData data);
        void Delete();
    }

    public sealed class MemorySaveStore : ISaveStore
    {
        private string _json;
        public int SaveCount { get; private set; }

        public SaveData Load() => _json == null ? null : SaveSerializer.FromJson(_json);

        public void Save(SaveData data)
        {
            _json = SaveSerializer.ToJson(data);
            SaveCount++;
        }

        public void Delete() => _json = null;
    }

    /// <summary>
    /// 파일 저장. 임시 파일에 쓴 뒤 교체해 쓰기 도중 강제 종료돼도 직전 저장본(.bak)으로 복구한다.
    /// </summary>
    public sealed class FileSaveStore : ISaveStore
    {
        public string PathMain { get; }
        public string PathTemp => PathMain + ".tmp";
        public string PathBackup => PathMain + ".bak";

        public FileSaveStore(string directory, string fileName = "lumina_save.json")
        {
            Directory.CreateDirectory(directory);
            PathMain = Path.Combine(directory, fileName);
        }

        public SaveData Load()
        {
            var data = TryLoad(PathMain);
            if (data != null) return data;
            return TryLoad(PathBackup);
        }

        private static SaveData TryLoad(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                return SaveSerializer.FromJson(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Save(SaveData data)
        {
            string json = SaveSerializer.ToJson(data);
            File.WriteAllText(PathTemp, json);
            if (File.Exists(PathMain))
            {
                if (File.Exists(PathBackup)) File.Delete(PathBackup);
                File.Move(PathMain, PathBackup);
            }
            File.Move(PathTemp, PathMain);
        }

        public void Delete()
        {
            foreach (var p in new[] { PathMain, PathTemp, PathBackup })
            {
                if (File.Exists(p)) File.Delete(p);
            }
        }
    }

    public static class SaveSerializer
    {
        public static string ToJson(SaveData d) => LuminaJson.Serialize(d);

        public static SaveData FromJson(string json)
        {
            var d = LuminaJson.Deserialize<SaveData>(json);
            if (d == null) throw new InvalidDataException("저장 데이터가 비어 있습니다.");
            if (d.version > SaveData.CurrentVersion) throw new InvalidDataException("더 새로운 버전의 저장 데이터입니다: v" + d.version);
            // v1 이후 마이그레이션 지점
            return d;
        }
    }
}
