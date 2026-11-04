using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Lumina.Core.Data
{
    /// <summary>데이터/저장 공통 JSON 설정. enum은 문자열로 저장해 사람이 읽고 검수할 수 있게 한다.</summary>
    public static class LuminaJson
    {
        public static readonly JsonSerializerSettings Settings = CreateSettings();

        private static JsonSerializerSettings CreateSettings()
        {
            var s = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Include,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                Formatting = Formatting.Indented
            };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Settings);
    }
}
