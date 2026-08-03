using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// 记忆巩固——diary 权威化的保底机制。
    ///
    /// 背景：chat_logs 是系统自动写入的客观往来记录（Agent 只读、不会漏）；decisions/diary.txt 是
    /// LLM 自写的记忆索引，可能漏记或滞后。若自我审视（国王政务/封地审视/外交问询回应）只读陈旧日记，
    /// 就会照旧思想行事（如"上次还要请和库赛特"，实则在最新聊天里已改为专攻库赛特）。
    ///
    /// 机制：在自我审视类激活前，比较日记最新条目日期与 chat_logs 各文件最新消息日期；若存在"比日记
    /// 更新的往来"，先跑一次巩固 pass——让 agent 读取较新的往来、比对日记、把值得记住的决定/承诺/计策/
    /// 结果/战略补记进 diary（旧决定被推翻则补记「结果」条目，不删旧条目）。只追加、不改写，静默执行，
    /// 不写 chat_logs、不弹玩家消息。触发与否由 MCM「启用记忆巩固」开关控制（默认开）。
    /// </summary>
    public static class MemoryConsolidator
    {
        // diary 条目日期格式：[1090春3] / [1090年冬第9日] / [1089冬第8日] / [1090冬12]（两种写法混用，统一解析）
        private static readonly Regex DiaryDateRegex =
            new(@"\[(\d{3,4})年?([春夏秋冬])(?:第)?(\d{1,2})(?:日)?\]", RegexOptions.Compiled);

        // chat_logs 消息日期格式：[第1090年，春季第15日，上午] user: ...
        private static readonly Regex ChatDateRegex =
            new(@"\[第(\d{3,4})年，([春夏秋冬])季第(\d{1,2})日", RegexOptions.Compiled);

        private static readonly Dictionary<char, int> SeasonIndex = new()
        {
            ['春'] = 0, ['夏'] = 1, ['秋'] = 2, ['冬'] = 3
        };

        private static int DateKey(int year, int season, int day) => year * 400 + season * 32 + day;

        private static bool TryParseDiaryDate(string line, out int key)
        {
            key = -1;
            var m = DiaryDateRegex.Match(line);
            if (!m.Success) return false;
            key = DateKey(int.Parse(m.Groups[1].Value), SeasonIndex[m.Groups[2].Value[0]], int.Parse(m.Groups[3].Value));
            return true;
        }

        private static bool TryParseChatDate(string line, out int key)
        {
            key = -1;
            var m = ChatDateRegex.Match(line);
            if (!m.Success) return false;
            key = DateKey(int.Parse(m.Groups[1].Value), SeasonIndex[m.Groups[2].Value[0]], int.Parse(m.Groups[3].Value));
            return true;
        }

        /// <summary>日记最新条目日期键；无有效条目返回 -1。</summary>
        private static int DiaryLatest(string diaryPath)
        {
            try
            {
                if (!File.Exists(diaryPath)) return -1;
                var lines = SafeFileIO.ReadAllLines(diaryPath);
                var max = -1;
                foreach (var line in lines)
                {
                    if (TryParseDiaryDate(line, out var k) && k > max) max = k;
                }
                return max;
            }
            catch { return -1; }
        }

        /// <summary>某 chat_logs 文件中最新消息的日期键；无有效日期返回 -1。</summary>
        private static int ChatLatest(string chatLogPath)
        {
            try
            {
                var lines = SafeFileIO.ReadAllLines(chatLogPath);
                var max = -1;
                foreach (var line in lines)
                {
                    if (TryParseChatDate(line, out var k) && k > max) max = k;
                }
                return max;
            }
            catch { return -1; }
        }

        /// <summary>diary 是否落后于某些 chat_logs（存在"比日记更新的往来"）。日记不存在时不视为落后（由正常流程建档）。</summary>
        public static bool IsDiaryStale(string agentDir)
        {
            var diaryPath = Path.Combine(agentDir, "decisions", "diary.txt");
            var diaryLatest = DiaryLatest(diaryPath);
            if (diaryLatest < 0) return false;

            var chatLogsDir = Path.Combine(agentDir, "chat_logs");
            if (!Directory.Exists(chatLogsDir)) return false;
            foreach (var file in Directory.GetFiles(chatLogsDir, "*.txt"))
            {
                if (ChatLatest(file) > diaryLatest) return true;
            }
            return false;
        }

        /// <summary>列出比日记更新的 chat_logs 文件名（供巩固 pass 定向读取）。</summary>
        public static List<string> ListNewerChatLogs(string agentDir)
        {
            var result = new List<string>();
            var diaryPath = Path.Combine(agentDir, "decisions", "diary.txt");
            var diaryLatest = DiaryLatest(diaryPath);
            if (diaryLatest < 0) return result;

            var chatLogsDir = Path.Combine(agentDir, "chat_logs");
            if (!Directory.Exists(chatLogsDir)) return result;
            foreach (var file in Directory.GetFiles(chatLogsDir, "*.txt"))
            {
                if (ChatLatest(file) > diaryLatest)
                    result.Add(Path.GetFileName(file));
            }
            return result;
        }

        /// <summary>
        /// 若日记落后于聊天记录，跑一次巩固 pass 补记 diary。静默执行（不写 chat_logs、不弹玩家消息）。
        /// 须在 AgentManager 活动上下文已指向该 agent 时调用（自我审视激活流程内）。
        /// </summary>
        public static async Task EnsureDiaryCurrentAsync(string agentId)
        {
            if (MySettings.Instance?.MemoryConsolidationEnabled == false) return;

            var agentDir = AgentManager.GetAgentDir();
            if (string.IsNullOrEmpty(agentDir)) return;
            if (!IsDiaryStale(agentDir!)) return;

            var newerFiles = ListNewerChatLogs(agentDir!);
            if (newerFiles.Count == 0) return;

            var agentEntity = EntityManager.GetEntityById(agentId);
            if (agentEntity?.HeroRef == null) return;

            var content = PromptManager.LoadMemoryConsolidationPrompt()
                .Replace("{newer_files}", string.Join("\n", newerFiles.Select(f => "- chat_logs/" + f)));

            var charPrompt = new CharacterPrompt
            {
                HeroId = agentId,
                HeroName = agentEntity.Name,
                ChatHistory = new List<ChatHistoryEntry> { new() { Role = "user", Content = content } }
            };

            try
            {
                await AIChatClient.SendMessage(charPrompt, agentEntity.HeroRef, includeTools: true, intent: "consolidation");
            }
            catch (Exception ex)
            {
                MainThreadExecutor.DisplayMessage(new InformationMessage(
                    $"[AI编年史] 记忆巩固失败：{ex.Message}", Colors.Red));
            }
        }
    }
}
