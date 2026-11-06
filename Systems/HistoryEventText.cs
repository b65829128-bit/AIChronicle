using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AIChronicle
{
    internal static class HistoryEventText
    {
        // JSONL 每个事件必须恰占一行；保留引号、反斜杠和宣言中的换行原义。
        internal static string Serialize(int year, string season, int day, string type, string summary)
            => new JObject
            {
                ["year"] = year, ["season"] = season, ["day"] = day,
                ["type"] = type, ["summary"] = summary
            }.ToString(Formatting.None);
    }
}
