using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Library;

namespace AIChronicle
{
    public static partial class AgentManager
{
        private static string GetThreadReadStatePath(string playerId)
        {
            return Path.Combine(_baseDir, SanitizeDir(playerId), "thread_read_state.json");
        }

        /// <summary>玩家与某 NPC 的往来线程恒在 NPC 侧：NPCs/{npcId}/chat_logs/{playerId}.txt。</summary>
        private static string GetPlayerThreadPath(string npcId, string playerId)
        {
            return GetChatLogPathFor(npcId, playerId) ?? "";
        }

        /// <summary>线程文件中的消息行数（与 LoadChatLogFor 解析一致：以 [ 开头且含 ": " 的行）。</summary>
        private static int CountThreadMessages(string threadPath)
        {
            if (!File.Exists(threadPath)) return 0;
            try
            {
                return SafeFileIO.ReadAllLines(threadPath).Count(l => l.StartsWith("[") && l.Contains(": "));
            }
            catch { return 0; }
        }

        private static Dictionary<string, int> LoadThreadReadState(string playerId)
        {
            var path = GetThreadReadStatePath(playerId);
            if (!File.Exists(path)) return new Dictionary<string, int>();
            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, int>>(SafeFileIO.ReadAllText(path))
                       ?? new Dictionary<string, int>();
            }
            catch { return new Dictionary<string, int>(); }
        }

        private static void SaveThreadReadState(string playerId, Dictionary<string, int> state)
        {
            var path = GetThreadReadStatePath(playerId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            SafeFileIO.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented));
        }

        /// <summary>某 NPC 与玩家的线程中的未读消息数（= 总消息行数 - 已读水位）。</summary>
        public static int GetThreadUnreadCount(string npcId, string playerId)
        {
            var total = CountThreadMessages(GetPlayerThreadPath(npcId, playerId));
            int stored;
            lock (_threadReadLock)
            {
                var state = LoadThreadReadState(playerId);
                stored = state.TryGetValue(npcId, out var v) ? v : 0;
            }
            return Math.Max(0, total - stored);
        }

        /// <summary>把某线程的已读水位推进到当前行数（玩家打开线程或自己发信时调用）。</summary>
        public static void MarkThreadRead(string npcId, string playerId)
        {
            var total = CountThreadMessages(GetPlayerThreadPath(npcId, playerId));
            lock (_threadReadLock)
            {
                var state = LoadThreadReadState(playerId);
                state[npcId] = total;
                SaveThreadReadState(playerId, state);
            }
        }
    }
}
