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
        private static string _baseDir = "";
        // 并发修复：活动 Agent 上下文改为 AsyncLocal，每个异步流程独立持有，互不覆盖。
        private static readonly AsyncLocal<string> _agentEntityId = new();
        private static readonly AsyncLocal<string> _targetEntityId = new();
        private static string _agentDir => Path.Combine(_baseDir, _agentEntityId.Value ?? "");
        // 同 AIChatClient：超时交给调用点 cts 控制，避免 30s 硬上限掐断慢请求（persona 生成等）。
        private static readonly HttpClient _http = new() { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        // 线程安全：persona 生成在后台线程并发执行（多个 Agent 任务同时掷性格维度），
        // System.Random 非线程安全，必须按线程隔离。
        private static readonly ThreadLocal<Random> _rng = new(() => new Random(Guid.NewGuid().GetHashCode()));

        private static string _cachedPersonaPrompt = "";
        private static DateTime _lastPersonaPromptCheck;  
        private static readonly HashSet<string> _readableDirs = new()
        {
            "", "knowledge", "relationships", "goals", "chat_logs", "decisions",
            "diplomacy"
        };

        private static readonly HashSet<string> _writableDirs = new()
        {
            "knowledge", "relationships", "goals", "chat_logs", "decisions"
        };

        private static readonly HashSet<string> _readableWorldFiles = new()
        {
            "factions.txt", "settlements.txt"
        };

        private static readonly HashSet<string> _readableWorldDirs = new()
        {
            "history", "history/chronicles"
        };

        public static string ActiveAgentId => _agentEntityId.Value ?? "";
        public static string ActiveTargetId => _targetEntityId.Value ?? "";

        public static void Initialize(string baseDir)
        {
            _baseDir = baseDir;
            Directory.CreateDirectory(_baseDir);
        }

        public static void Activate(string agentEntityId, string targetEntityId)
        {
            _agentEntityId.Value = SanitizeDir(agentEntityId);
            _targetEntityId.Value = SanitizeDir(targetEntityId);
            InitAgentDirectory();
        }

        /// <summary>仅切换活动上下文，不做目录初始化（用于主线程分发工具执行时临时套用/恢复上下文）。</summary>
        internal static void SetContextOnly(string agentEntityId, string targetEntityId)
        {
            _agentEntityId.Value = SanitizeDir(agentEntityId);
            _targetEntityId.Value = SanitizeDir(targetEntityId);
        }

        public static string? GetAgentDir()
        {
            if (string.IsNullOrEmpty(_agentEntityId.Value)) return null;
            return _agentDir;
        }

        public static string? GetChatLogPathFor(string agentEntityId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir) || string.IsNullOrEmpty(agentEntityId) || string.IsNullOrEmpty(targetEntityId))
                return null;
            return Path.Combine(_baseDir, agentEntityId, "chat_logs", SanitizeFile(targetEntityId) + ".txt");
        }

        /// <summary>私有密使线程路径（World/correspondence/{idA}_and_{idB}.txt，与 ToolExecutor 写入端同路径）。</summary>
        public static string? GetCorrespondencePathFor(string idA, string idB)
        {
            if (string.IsNullOrEmpty(_baseDir) || string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB))
                return null;
            var pair = string.CompareOrdinal(idA, idB) <= 0 ? idA + "_and_" + idB : idB + "_and_" + idA;
            return Path.Combine(_baseDir, "World", "correspondence", pair + ".txt");
        }

        /// <summary>解析密使线程行：[时间] 名字（标题）遣使/答复：内容。解析失败的行跳过。</summary>
        private static EnvoyEntry? ParseEnvoyLine(string line)
        {
            var tsEnd = line.IndexOf("] ", StringComparison.Ordinal);
            if (tsEnd < 1) return null;
            var time = line.Substring(1, tsEnd - 1);
            var rest = line.Substring(tsEnd + 2);
            var nameEnd = rest.IndexOf("（", StringComparison.Ordinal);
            if (nameEnd <= 0) return null;
            var sender = rest.Substring(0, nameEnd);
            var titleEnd = rest.IndexOf("）", nameEnd, StringComparison.Ordinal);
            if (titleEnd < 0) return null;
            var tail = rest.Substring(titleEnd + 1);
            var colon = tail.IndexOf('：');
            if (colon < 0) return null;
            return new EnvoyEntry { Time = time, Sender = sender, Content = tail.Substring(colon + 1).Trim() };
        }

        /// <summary>读取双方私有密使线程，按时间顺序返回。</summary>
        public static List<EnvoyEntry> ReadCorrespondenceThread(string idA, string idB)
        {
            var result = new List<EnvoyEntry>();
            var path = GetCorrespondencePathFor(idA, idB);
            if (path == null || !File.Exists(path)) return result;
            try
            {
                foreach (var line in SafeFileIO.ReadAllLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var entry = ParseEnvoyLine(line);
                    if (entry != null) result.Add(entry);
                }
            }
            catch { }
            return result;
        }

        /// <summary>密使未读数：自己最近一条消息之后由对方发来的条数（派生，无需水位文件）。</summary>
        public static int GetEnvoyUnreadCount(string npcId, string playerId)
        {
            var entries = ReadCorrespondenceThread(npcId, playerId);
            if (entries.Count == 0) return 0;
            var playerName = Hero.MainHero?.Name?.ToString() ?? "";
            var unread = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Sender == playerName) break;
                unread++;
            }
            return unread;
        }

        public static string? GetTargetKnowledgePath()
        {
            if (string.IsNullOrEmpty(_agentDir)) return null;
            return Path.Combine(_agentDir, "knowledge", SanitizeFile(_targetEntityId.Value ?? "") + ".txt");
        }

        private static void InitAgentDirectory()
        {
            var dir = _agentDir;
            if (Directory.Exists(dir)) return;

            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "knowledge"));
            Directory.CreateDirectory(Path.Combine(dir, "relationships"));
            Directory.CreateDirectory(Path.Combine(dir, "goals"));
            Directory.CreateDirectory(Path.Combine(dir, "chat_logs"));
            Directory.CreateDirectory(Path.Combine(dir, "decisions"));
        }

        private static string GetPersonaMetaPath()
        {
            return Path.Combine(_agentDir, "persona_meta.json");
        }

        private class PersonaMeta
        {
            public int Ambition { get; set; }
            public int LoyaltyType { get; set; }
            public int RiskTolerance { get; set; }
            public int MandateBelief { get; set; }
            public int WarLiking { get; set; }
            /// <summary>维度版本标记：当前为 v2（战争倾向好战 50/30/20 分布）。新文件写入当前版本，供未来分布调整参考。</summary>
            public int MetaVersion { get; set; }
        }

        private static PersonaMeta LoadOrCreatePersonaMetaFromPath(string path, PersonaMeta fallback)
        {
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path, Encoding.UTF8);
                    var meta = JsonConvert.DeserializeObject<PersonaMeta>(json);
                    if (meta != null)
                        return meta;
                }
                catch { }
            }

            var dir = Path.GetDirectoryName(path);
            if (dir != null) Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonConvert.SerializeObject(fallback, Formatting.Indented), Encoding.UTF8);
            return fallback;
        }

        private static int RollWeightedTrait(bool skewPositive)
        {
            var roll = _rng.Value!.Next(100);
            if (skewPositive)
            {
                if (roll < 5) return -2;
                if (roll < 25) return -1;
                if (roll < 63) return 0;
                if (roll < 88) return 1;
                return 2;
            }
            else
            {
                if (roll < 6) return -2;
                if (roll < 31) return -1;
                if (roll < 69) return 0;
                if (roll < 94) return 1;
                return 2;
            }
        }

        private static int RollLoyaltyType()
        {
            var roll = _rng.Value!.Next(100);
            if (roll < 10) return 0;
            if (roll < 50) return 1;
            if (roll < 85) return 2;
            return 3;
        }

        /// <summary>
        /// 天命信仰分布：不信 6% / 假托 20% / 平常 38% / 敬重 26% / 笃信 10%。
        /// 极端少、中间多，保证同一世界里的立场多元。
        /// </summary>
        private static int RollMandateBelief()
        {
            var roll = _rng.Value!.Next(100);
            if (roll < 6) return -2;
            if (roll < 26) return -1;
            if (roll < 64) return 0;
            if (roll < 90) return 1;
            return 2;
        }

        /// <summary>战争倾向分布（v2，有形的大手）：+2 占 50%、+1 占 30%、0 占 10%、-1 占 6%、-2 占 4%。
        /// 默认分布过于和平导致 AI 不开战，故意大幅拉高好战比例以激活战争循环——80% 的人好战。</summary>
        private static int RollWarLiking()
        {
            var roll = _rng.Value!.Next(100);
            if (roll < 50) return 2;
            if (roll < 80) return 1;
            if (roll < 90) return 0;
            if (roll < 96) return -1;
            return -2;
        }

        private static string BuildNativeTraitsText(Hero hero)
        {
            var sb = new StringBuilder();
            AppendTraitLine(sb, "胆气（Valor）", hero.GetTraitLevel(DefaultTraits.Valor), "勇敢无畏", "胆小怯懦");
            AppendTraitLine(sb, "仁慈（Mercy）", hero.GetTraitLevel(DefaultTraits.Mercy), "仁慈宽厚", "冷酷残忍");
            AppendTraitLine(sb, "荣誉（Honor）", hero.GetTraitLevel(DefaultTraits.Honor), "守信重诺", "狡诈无信");
            AppendTraitLine(sb, "慷慨（Generosity）", hero.GetTraitLevel(DefaultTraits.Generosity), "慷慨感恩", "自私忘恩");
            AppendTraitLine(sb, "谋略（Calculating）", hero.GetTraitLevel(DefaultTraits.Calculating), "深谋远虑", "冲动鲁莽");
            return sb.ToString().TrimEnd();
        }

        private static void AppendTraitLine(StringBuilder sb, string label, int value, string posDesc, string negDesc)
        {
            string desc;
            if (value > 0) desc = $"（{posDesc}）";
            else if (value < 0) desc = $"（{negDesc}）";
            else desc = "（中庸）";
            sb.AppendLine($"{label}：{value:+0;-0} {desc}");
        }

        private static string BuildCustomTraitsText(PersonaMeta meta)
        {
            var sb = new StringBuilder();

            var ambDesc = meta.Ambition switch
            {
                2 => "极度渴望权力与地位，不惜一切代价向上爬",
                1 => "有较强的进取心，希望在仕途上更进一步",
                0 => "对权力持平常心，有则有、无则安",
                -1 => "对权力敬而远之，更愿守好自己的一亩三分地",
                -2 => "厌恶权力斗争，只想过太平日子",
                _ => "?"
            };
            sb.AppendLine($"权力欲：{meta.Ambition:+0;-0} — {ambDesc}");

            var loyDesc = meta.LoyaltyType switch
            {
                0 => "忠于自己——利益至上，不受家族或王国束缚",
                1 => "忠于家族——家族利益高于一切",
                2 => "忠于王国——以王国和君主的利益为优先",
                3 => "忠于信念——坚持自己的理想和原则，超越世俗忠诚",
                _ => "?"
            };
            sb.AppendLine($"归属重心：类型{meta.LoyaltyType} — {loyDesc}");

            var riskDesc = meta.RiskTolerance switch
            {
                2 => "赌徒心态，愿意押上一切博取大收益",
                1 => "偏好适度的风险，善于权衡利弊",
                0 => "稳健行事，不冒不必要的风险",
                -1 => "谨慎保守，常常犹豫不决",
                -2 => "极度保守，惧怕任何冒险",
                _ => "?"
            };
            sb.AppendLine($"冒险倾向：{meta.RiskTolerance:+0;-0} — {riskDesc}");

            var mandateDesc = meta.MandateBelief switch
            {
                2 => "笃信天命，真心信奉天命与大一统，言行以此自处",
                1 => "敬重天命，大体相信，决策时会顾及名分",
                0 => "对天命之说平常心，随大流、不较真",
                -1 => "假托天命，嘴上说信、心里当权术工具",
                -2 => "不信天命，视之为欺人之谈，只信实力",
                _ => "?"
            };
            sb.AppendLine($"天命信仰：{meta.MandateBelief:+0;-0} — {mandateDesc}");

            var warDesc = meta.WarLiking switch
            {
                2 => "穷兵黩武、好战成性——不打仗就浑身难受，即便处于劣势、代价惨重也渴望开战，视征伐为乐趣与荣耀",
                1 => "主动求战——倾向用战争解决问题，能打就打，会积极寻找甚至制造开战理由，不太权衡代价",
                0 => "对战争无倾向——有利就打、不利则不战，完全看形势与利益",
                -1 => "不轻启战端——认为战争是万不得已的最后手段，只有被逼到非战不可时才动武",
                -2 => "极力避战——任何争端都倾向和平解决，宁可吃亏也不愿开战",
                _ => "?"
            };
            sb.AppendLine($"战争倾向：{meta.WarLiking:+0;-0} — {warDesc}");

            return sb.ToString().TrimEnd();
        }

        /// <summary>persona 完整性检查：标准三段标记缺一即视为被截断/损坏（生成时 max_tokens 不足可能切断输出）。
        /// 截断自愈：加载时检测到残缺 → 触发重新生成（重新生成失败会写回完整的 fallback，不会死循环）。</summary>
        private static bool IsCompletePersona(string text)
        {
            // 思考内容混入正文（如 MiniMax 内联 <think>）视为残缺，触发重新生成——否则 think 块内
            // 若恰好出现三段标记会被误判为完整，坏文件一直被复用。
            if (text.IndexOf("<think", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return text.Contains("[MOTIVATION]")
                && text.Contains("[TRAITS]")
                && text.Contains("[SPEECH_STYLE]");
        }

        private static async System.Threading.Tasks.Task<string> GeneratePersonaViaLLM(string info, string name, string nativeTraits, string customTraits)
        {
            var settings = MySettings.Instance;
            if (settings == null)
                return "";
            // persona 生成发生在首次对话流程，归「对话与书信」场景；未配置（本场景与兜底均空）则跳过
            var conn = ConnectionResolver.Resolve("conversation");
            if (string.IsNullOrWhiteSpace(conn.ApiKey))
                return "";

            var prompt = LoadPersonaGenerationPrompt()
                .Replace("{npc_name}", name)
                .Replace("{npc_info}", info)
                .Replace("{native_traits}", nativeTraits)
                .Replace("{custom_traits}", customTraits);

            var provider = LLMProviders.Create(settings.ProviderType);

            // persona 是模板跟随的机械任务，low 思考即可（DeepSeek 默认 high 会把正文挤掉）。
            // reasoning_effort 是否写入请求由 provider 能力声明决定——DeepSeek 发、OpenAI 兼容端点不发。
            var request = provider.BuildRequest(new LLMRequest
            {
                Url = conn.Url,
                Model = conn.Model,
                ApiKey = conn.ApiKey,
                Messages = new List<JToken> { new JObject { ["role"] = "user", ["content"] = prompt } },
                MaxTokens = settings.MaxTokens,
                Temperature = 0.7f,
                Stream = true,
                ReasoningEffort = "low"
            });

            var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
            try
            {
                // 流式读取：ResponseHeadersRead 只等响应头、body 边收边读，避免非流式"等完整 body"超时
                //（MiniMax 思考模型生成 persona 可能超过 60 秒）。
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);
                response.EnsureSuccessStatusCode();

                var text = new StringBuilder();
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (!line.StartsWith("data: ")) continue;
                    var data = line.Substring(6);
                    if (data == "[DONE]") break;
                    var chunk = provider.ParseStreamLine(data);
                    if (chunk != null && !string.IsNullOrEmpty(chunk.Text))
                        text.Append(chunk.Text);
                }

                var content = LLMText.StripThinkTags(text.ToString());
                if (string.IsNullOrEmpty(content))
                    DebugLogger.Log($"persona 生成返回空 agent={name}");
                return content;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"persona 生成失败 agent={name}：{ex.Message}");
                return "";
            }
        }

        private static string LoadPersonaGenerationPrompt()
        {
            var path = Path.Combine(PromptManager.CampaignDir, "persona_generation.txt");
            if (!File.Exists(path))
                path = Path.Combine(PromptManager.PromptsBaseDir, "persona_generation.txt");
            if (!File.Exists(path))
                return
                    "你正在为游戏角色生成性格描述。根据以下信息，为名为{npc_name}的NPC生成性格。\n\n"
                    + "严格按格式：\n[MOTIVATION]\n...\n[TRAITS]\n- ...\n[SPEECH_STYLE]\n...\n\n"
                    + "{npc_info}";

            var lastWrite = File.GetLastWriteTimeUtc(path);
            if (_cachedPersonaPrompt == "" || lastWrite > _lastPersonaPromptCheck)
            {
                _cachedPersonaPrompt = File.ReadAllText(path, Encoding.UTF8);
                _lastPersonaPromptCheck = lastWrite;
            }
            return _cachedPersonaPrompt;
        }

        private static string GetAgentDirPath(string agentId)
        {
            return Path.Combine(_baseDir, SanitizeDir(agentId));
        }

        public static string LoadPersonaFor(string agentId, Hero hero)
        {
            var agentDir = GetAgentDirPath(agentId);
            if (string.IsNullOrEmpty(_baseDir))
                return "名字：" + (hero.Name?.ToString() ?? "未知") + "\n性别：" + (hero.IsFemale ? "女" : "男") + "\n文化：" + (hero.Culture?.Name?.ToString() ?? "未知") + "\n说话风格：使用中世纪贵族的正式口吻。";

            var path = Path.Combine(agentDir, "persona.txt");
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path, Encoding.UTF8);
                if (hero != Hero.MainHero && !IsCompletePersona(existing))
                    return GeneratePersonaFor(agentId, hero);
                return existing;
            }

            if (hero == Hero.MainHero)
                return "[MOTIVATION]\n你是一位在卡拉迪亚大陆闯荡的冒险者。\n\n[TRAITS]\n- 待探索\n\n[SPEECH_STYLE]\n自由发挥。";

            return GeneratePersonaFor(agentId, hero);
        }

        public static string? ReadKnowledgeFor(string agentId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "knowledge", SanitizeFile(targetEntityId) + ".txt");
            if (File.Exists(path))
                return File.ReadAllText(path, Encoding.UTF8).Trim();

            var entity = EntityManager.GetEntityById(targetEntityId);
            if (entity != null)
            {
                var namePath = Path.Combine(agentDir, "knowledge", SanitizeFile(entity.Name) + ".txt");
                if (File.Exists(namePath))
                    return File.ReadAllText(namePath, Encoding.UTF8).Trim();
            }

            return null;
        }

        public static string? ReadRelationshipFor(string agentId, string targetEntityId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "relationships", SanitizeFile(targetEntityId) + ".txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static string? ReadGoalsFor(string agentId)
        {
            if (string.IsNullOrEmpty(_baseDir)) return null;
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "goals", "current.txt");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static void AppendDecisionFor(string agentId, string entry)
        {
            if (string.IsNullOrEmpty(_baseDir)) return;
            var agentDir = GetAgentDirPath(agentId);
            var decisionsDir = Path.Combine(agentDir, "decisions");
            Directory.CreateDirectory(decisionsDir);
            File.AppendAllText(Path.Combine(decisionsDir, "diplomacy.txt"), entry, Encoding.UTF8);
        }

        private static string GeneratePersonaFor(string agentId, Hero hero)
        {
            var agentDir = GetAgentDirPath(agentId);
            var name = hero.Name?.ToString() ?? "未知领主";
            var culture = hero.Culture?.Name?.ToString() ?? "未知";
            var isFemale = hero.IsFemale ? "女" : "男";
            var encyclopedia = hero.EncyclopediaText?.ToString() ?? "";

            var basicInfo = new StringBuilder();
            basicInfo.AppendLine($"姓名：{name}");
            basicInfo.AppendLine($"性别：{isFemale}");
            basicInfo.AppendLine($"文化：{culture}");
            // 身份克制：不注入家族/所属王国/头衔——身份在游戏中会随阵营变更、自立、禅让、婚嫁而变化，
            // 由 ContextBuilder 动态提供；百科描述保留作背景素材，但提示词要求不得锚定当前身份。
            if (!string.IsNullOrEmpty(encyclopedia))
                basicInfo.AppendLine($"百科描述：{encyclopedia}");

            var meta = LoadOrCreatePersonaMetaFor(agentId);
            var nativeTraits = BuildNativeTraitsText(hero);
            var customTraits = BuildCustomTraitsText(meta);

            string persona = "";
            for (int attempt = 0; attempt < 2 && string.IsNullOrEmpty(persona); attempt++)
            {
                try
                {
                    persona = GeneratePersonaViaLLM(basicInfo.ToString(), name, nativeTraits, customTraits).Result;
                }
                catch { }
            }
            if (!string.IsNullOrEmpty(persona))
            {
                var p = Path.Combine(agentDir, "persona.txt");
                Directory.CreateDirectory(agentDir);
                File.WriteAllText(p, persona, Encoding.UTF8);
                return persona;
            }

            var fallback = $"[MOTIVATION]\n{basicInfo}\n[TRAITS]\n- 未知\n\n[SPEECH_STYLE]\n使用中世纪贵族的正式口吻。";
            var fallbackPath = Path.Combine(agentDir, "persona.txt");
            Directory.CreateDirectory(agentDir);
            File.WriteAllText(fallbackPath, fallback, Encoding.UTF8);
            return fallback;
        }

        private static PersonaMeta LoadOrCreatePersonaMetaFor(string agentId)
        {
            var agentDir = GetAgentDirPath(agentId);
            var path = Path.Combine(agentDir, "persona_meta.json");
            return LoadOrCreatePersonaMetaFromPath(path, new PersonaMeta
            {
                Ambition = RollWeightedTrait(skewPositive: true),
                LoyaltyType = RollLoyaltyType(),
                RiskTolerance = RollWeightedTrait(skewPositive: false),
                MandateBelief = RollMandateBelief(),
                WarLiking = RollWarLiking(),
                MetaVersion = 2
            });
        }

        /// <summary>
        /// 统一线程模型：信件写入双方的书信往来线程（chat_logs），不再投递 mailbox。
        /// 线程始终存放在"agent 侧"：收信人是玩家时，线程在发信 NPC 目录下（玩家视角线程）；
        /// 收信人是 NPC 时，线程在收信 NPC 目录下。role 以存放目录所有者的视角为准。
        /// </summary>
        public static void StoreLetterInThread(string senderId, string recipientId, string content, bool recipientIsPlayer)
        {
            if (recipientIsPlayer)
                PromptManager.AppendChatLogFor(senderId, recipientId, "assistant", content, isLetter: true); // NPCs/{sender}/chat_logs/{player}.txt（NPC 的声音）
            else
                PromptManager.AppendChatLogFor(recipientId, senderId, "user", content, isLetter: true);      // NPCs/{recipient}/chat_logs/{sender}.txt（对方的声音）
        }

        // ============ 每线程已读/未读追踪（玩家端） ============

        private static readonly object _threadReadLock = new();
    }

    /// <summary>私有密使线程单条记录（UI 展示用）。</summary>
    public class EnvoyEntry
    {
        public string Time { get; set; } = "";
        public string Sender { get; set; } = "";
        public string Content { get; set; } = "";
    }
}
