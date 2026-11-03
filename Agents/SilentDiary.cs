using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;

namespace AIChronicle
{
    /// <summary>一条静默见闻的收件人批次：先 Add（越具体的文本越先加），最后 Publish。</summary>
    public sealed class SilentDiaryBatch
    {
        private readonly string _type;
        private readonly string _key;
        private readonly List<(Hero Hero, string Text)> _items = new();
        private readonly HashSet<Hero> _seen = new();

        internal SilentDiaryBatch(string type, string key)
        {
            _type = type;
            _key = key;
        }

        /// <summary>直接收件人（环 0）：文本用第二人称。</summary>
        public SilentDiaryBatch Add(Hero? hero, string text)
        {
            if (hero == null || !hero.IsAlive || hero == Hero.MainHero) return this;
            if (string.IsNullOrWhiteSpace(text)) return this;
            if (!_seen.Add(hero)) return this;
            _items.Add((hero, text));
            return this;
        }

        /// <summary>氏族领袖（环 1）。</summary>
        public SilentDiaryBatch AddClanLeader(Clan? clan, string text)
        {
            if (clan?.Leader != null) Add(clan.Leader, text);
            return this;
        }

        /// <summary>王国统治者（环 2）。</summary>
        public SilentDiaryBatch AddKingdomRuler(Kingdom? kingdom, string text)
        {
            if (kingdom?.RulingClan?.Leader != null) Add(kingdom.RulingClan.Leader, text);
            return this;
        }

        public void Publish()
        {
            if (_items.Count > 0) SilentDiary.Submit(_type, _key, _items);
        }
    }

    /// <summary>
    /// 静默见闻：把"与你有关、但不值得叫醒你"的世界事件，以零 LLM 的方式写进 Agent 日记。
    ///
    /// 原则（见 docs/plan-silent-memory.md）：
    /// - 只写「情报」，永不代写决定/评价/结果；Agent 读到后自己决定是否升格。
    /// - 不唤醒、不弹窗、不写 chat_logs、不注入上下文。
    /// - 受众在事件钩子处用游戏对象计算，环越外文本越短；不在消费端解析摘要字符串。
    /// - 按 (Agent, 事件日) 缓冲，激活前 / 跨日 / 退出时 flush；同日多条合并为一行。
    /// </summary>
    public static class SilentDiary
    {
        private sealed class PendingDay
        {
            public int Day;
            public string Prefix = "";
            public List<string> Items = new();
            /// <summary>本日已写进日记的条数（上限见 MCM「每日见闻上限」）</summary>
            public int Flushed;
        }

        private static readonly object _lock = new();
        private static readonly Dictionary<string, List<PendingDay>> _pending = new();
        private static readonly HashSet<string> _published = new(StringComparer.Ordinal);
        private static int _publishedDay = -1;
        private static int _lastFlushDay = -1;

        public static bool Enabled => MySettings.Instance?.SilentDiaryEnabled != false;

        private static int DailyCap
        {
            get
            {
                var cap = MySettings.Instance?.SilentDiaryDailyCap ?? 3;
                return Math.Max(1, Math.Min(5, cap));
            }
        }

        public static SilentDiaryBatch Begin(string type, string key) => new(type, key);

        /// <summary>开始新战役/切档：清空缓冲与去重表。</summary>
        public static void ResetForNewCampaign()
        {
            lock (_lock)
            {
                _pending.Clear();
                _published.Clear();
                _publishedDay = -1;
                _lastFlushDay = -1;
            }
        }

        internal static void Submit(string type, string key, List<(Hero Hero, string Text)> items)
        {
            if (!Enabled || Campaign.Current == null || items == null || items.Count == 0) return;

            try
            {
                var now = CampaignTime.Now;
                var day = (int)now.ToDays;
                var prefix = AgentManager.GetDiaryDatePrefix();

                lock (_lock)
                {
                    if (day != _publishedDay)
                    {
                        _published.Clear();
                        _publishedDay = day;
                    }

                    var dedupe = $"{type}|{day}|{key}";
                    if (!_published.Add(dedupe)) return;

                    foreach (var (hero, text) in items)
                    {
                        if (hero == null || !hero.IsAlive || hero == Hero.MainHero) continue;

                        var entity = EntityManager.GetOrCreateEntity(hero);
                        if (entity == null || entity.Controller != EntityController.Agent) continue;

                        var clean = SilentDiaryText.Clean(text);
                        if (clean.Length == 0) continue;

                        if (!_pending.TryGetValue(entity.Id, out var days))
                        {
                            days = new List<PendingDay>();
                            _pending[entity.Id] = days;
                        }

                        var pd = days.FirstOrDefault(d => d.Day == day);
                        if (pd == null)
                        {
                            pd = new PendingDay { Day = day, Prefix = prefix };
                            days.Add(pd);
                        }

                        // 硬上限：单日最多缓存 12 条，防止异常事件风暴撑爆内存
                        if (pd.Items.Count < 12) pd.Items.Add(clean);
                    }
                }
            }
            catch (Exception e)
            {
                DebugLogger.Log($"静默见闻入账失败（{type}）：{e.Message}");
            }
        }

        /// <summary>把某 Agent 的缓冲写进日记（激活前调用）。</summary>
        public static void FlushPending(string agentId)
        {
            if (string.IsNullOrEmpty(agentId) || string.IsNullOrEmpty(PromptManager.CampaignDir)) return;

            List<(string Prefix, List<string> Items)>? batches = null;
            var cap = DailyCap;
            lock (_lock)
            {
                if (!_pending.TryGetValue(agentId, out var days)) return;
                batches = new List<(string Prefix, List<string> Items)>();
                foreach (var day in days)
                {
                    // 同一个游戏日可能被多次激活：只写"尚未写过且未超日上限"的部分，缓冲保留到跨日
                    var remaining = cap - day.Flushed;
                    if (remaining <= 0) continue;
                    var take = day.Items.Skip(day.Flushed).Take(remaining).ToList();
                    if (take.Count == 0) continue;
                    day.Flushed += take.Count;
                    batches.Add((day.Prefix, take));
                }
            }

            foreach (var (prefix, items) in batches)
            {
                if (items.Count == 0) continue;
                var text = Compose(items);
                AgentManager.AppendSystemDiary(agentId, "情报", text, prefix);
                DebugLogger.Log($"静默见闻 → {agentId}：{text}");
            }
        }

        /// <summary>跨日/退出时把所有人的缓冲写盘。</summary>
        public static void FlushAll()
        {
            List<KeyValuePair<string, List<PendingDay>>> copy;
            var cap = DailyCap;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                copy = _pending.ToList();
                _pending.Clear();
            }

            foreach (var kv in copy)
            {
                foreach (var pd in kv.Value)
                {
                    var remaining = cap - pd.Flushed;
                    if (remaining <= 0) continue;
                    var take = pd.Items.Skip(pd.Flushed).Take(remaining).ToList();
                    if (take.Count == 0) continue;
                    var text = Compose(take);
                    AgentManager.AppendSystemDiary(kv.Key, "情报", text, pd.Prefix);
                    DebugLogger.Log($"静默见闻 → {kv.Key}：{text}");
                }
            }
        }

        /// <summary>由 Tick 每帧调用：跨日时 flush 全部，保证日期与事件日一致。</summary>
        public static void FlushIfDayChanged()
        {
            if (Campaign.Current == null) return;
            var day = (int)CampaignTime.Now.ToDays;
            if (_lastFlushDay < 0)
            {
                _lastFlushDay = day;
                return;
            }
            if (day == _lastFlushDay) return;
            _lastFlushDay = day;
            FlushAll();
        }

        private static string Compose(List<string> items)
        {
            var cap = DailyCap;
            var picked = items.Take(cap).ToList();
            var extra = items.Count - picked.Count;
            var body = picked.Count == 1
                ? picked[0]
                : "今日见闻——" + string.Join("；", picked);
            if (extra > 0) body += $"等{extra}事";
            return body;
        }

        // ---------- 事件入口 ----------

        public static void OnWarDeclared(Kingdom? attacker, Kingdom? defender)
        {
            if (attacker == null || defender == null || attacker == defender) return;
            var an = attacker.Name?.ToString() ?? "?";
            var bn = defender.Name?.ToString() ?? "?";
            var key = "war|" + string.Join(">", new[] { attacker.StringId ?? an, defender.StringId ?? bn }.OrderBy(x => x, StringComparer.Ordinal));

            var batch = Begin("war_declared", key);
            batch.Add(attacker.RulingClan?.Leader, $"你向{bn}宣战。");
            batch.Add(defender.RulingClan?.Leader, $"{an}向你宣战。");
            foreach (var clan in attacker.Clans)
                batch.AddClanLeader(clan, $"你的君主向{bn}宣战。");
            foreach (var clan in defender.Clans)
                batch.AddClanLeader(clan, $"{an}向你的君主宣战。");
            batch.Publish();
        }

        public static void OnPeace(Kingdom? a, Kingdom? b)
        {
            if (a == null || b == null || a == b) return;
            var an = a.Name?.ToString() ?? "?";
            var bn = b.Name?.ToString() ?? "?";
            var key = "peace|" + string.Join(">", new[] { a.StringId ?? an, b.StringId ?? bn }.OrderBy(x => x, StringComparer.Ordinal));

            var batch = Begin("peace_made", key);
            batch.Add(a.RulingClan?.Leader, $"你与{bn}议和。");
            batch.Add(b.RulingClan?.Leader, $"你与{an}议和。");
            foreach (var clan in a.Clans)
                batch.AddClanLeader(clan, $"你的君主与{bn}议和。");
            foreach (var clan in b.Clans)
                batch.AddClanLeader(clan, $"你的君主与{an}议和。");
            batch.Publish();
        }

        /// <summary>封地易主：isGrant=true 为册封/转让，false 为攻取/其他易主。</summary>
        public static void OnSettlementChanged(Settlement? settlement, Hero? oldOwner, Hero? newOwner, Hero? capturer, bool isGrant)
        {
            // 开局/建国时会有 oldOwner 为空的大批归属变更，那不是"见闻"，跳过以免刷屏
            if (settlement == null || oldOwner == null || newOwner == null) return;

            var sn = settlement.Name?.ToString() ?? "某地";
            var oldH = Describe(oldOwner);
            var newH = Describe(newOwner);
            var capH = capturer != null ? Describe(capturer) : newH;

            var oldClan = oldOwner?.Clan;
            var newClan = newOwner?.Clan;

            var batch = Begin(isGrant ? "fief_granted" : "settlement_captured", "s|" + (settlement.StringId ?? sn));

            if (oldOwner != null)
            {
                batch.Add(oldOwner, isGrant
                    ? $"君主将你的封地{sn}转予{newH}。"
                    : $"你的封地{sn}被{capH}夺去。");
            }
            if (newOwner != null)
            {
                batch.Add(newOwner, isGrant ? $"你获得了{sn}。" : $"你得到了{sn}。");
            }
            if (oldClan != null && oldOwner != null)
            {
                batch.AddClanLeader(oldClan, isGrant
                    ? $"{oldH}的封地{sn}被转予{newH}。"
                    : $"{oldH}的封地{sn}被{capH}夺去。");
            }
            if (newClan != null)
            {
                batch.AddClanLeader(newClan, $"你的家族获得了{sn}。");
            }
            if (oldClan?.Kingdom != null)
            {
                batch.AddKingdomRuler(oldClan.Kingdom, $"{oldH}的{sn}被{capH}夺去。");
            }
            if (newClan?.Kingdom != null)
            {
                batch.AddKingdomRuler(newClan.Kingdom, $"{newH}取得了{sn}。");
            }

            batch.Publish();
        }

        public static void OnSiegeStarted(Settlement? settlement, Hero? attacker)
        {
            if (settlement == null) return;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan == null || ownerClan.Leader == attacker) return;

            var attackerH = attacker != null ? Describe(attacker) : "敌军";
            var place = settlement.IsCastle ? "城堡" : "城镇";
            var sn = settlement.Name?.ToString() ?? "某地";

            var batch = Begin("siege_started", "siege|" + (settlement.StringId ?? sn));
            batch.AddClanLeader(ownerClan, $"{attackerH}正围攻你的{place}{sn}。");
            batch.AddKingdomRuler(ownerClan.Kingdom, $"{attackerH}正围攻{ownerClan.Name}的{sn}。");
            batch.Publish();
        }

        public static void OnSiegeLifted(Settlement? settlement, string reason)
        {
            if (settlement == null) return;
            var ownerClan = settlement.OwnerClan;
            if (ownerClan == null) return;

            var sn = settlement.Name?.ToString() ?? "某地";
            var batch = Begin("siege_end", "siege_end|" + (settlement.StringId ?? sn));
            batch.AddClanLeader(ownerClan, $"{sn}之围已解（{reason}）。");
            batch.AddKingdomRuler(ownerClan.Kingdom, $"{sn}之围已解。");
            batch.Publish();
        }

        public static void OnHeroKilled(Hero? victim, Hero? killer, Clan? clan, Hero? clanLeader, Kingdom? kingdom, string title, string cause)
        {
            if (victim == null) return;

            var vn = Describe(victim) ?? "某人";
            var batch = Begin("hero_killed", "h|" + (victim.StringId ?? vn));

            if (!string.IsNullOrEmpty(title) && title.EndsWith("统治者", StringComparison.Ordinal) && kingdom != null)
            {
                foreach (var c in kingdom.Clans)
                    batch.AddClanLeader(c, $"你们的国王{vn}{cause}。");
            }
            else
            {
                if (clanLeader != null && clanLeader != victim)
                    batch.Add(clanLeader, $"你的家族成员{victim.Name}{cause}。");
                if (kingdom != null)
                {
                    var clanPart = clan != null ? $"{clan.Name}的" : "";
                    batch.AddKingdomRuler(kingdom, $"你麾下{clanPart}{vn}{cause}。");
                }
            }

            batch.Publish();
        }

        public static void OnBattle(MapEvent? mapEvent, Hero? attackerLeader, Hero? defenderLeader, string? location)
        {
            if (mapEvent?.Winner == null) return;

            var winnerSide = mapEvent.Winner.MissionSide;
            if (winnerSide != BattleSideEnum.Attacker && winnerSide != BattleSideEnum.Defender) return;
            var winnerHero = winnerSide == BattleSideEnum.Attacker ? attackerLeader : defenderLeader;
            var loserHero = winnerSide == BattleSideEnum.Attacker ? defenderLeader : attackerLeader;
            if (winnerHero == null || loserHero == null || winnerHero == loserHero) return;

            var where = string.IsNullOrEmpty(location) ? "" : $"在{location}附近";
            var key = $"b|{mapEvent.MapEventSettlement?.StringId}|{attackerLeader?.StringId}|{defenderLeader?.StringId}";

            var batch = Begin("battle_fought", key);
            batch.Add(loserHero, $"你{where}被{Describe(winnerHero)}击败。");
            batch.Add(winnerHero, $"你{where}击败了{Describe(loserHero)}。");
            batch.Publish();
        }

        public static void OnGift(Hero? from, Hero? to, string what)
        {
            if (from == null || to == null || from == to) return;

            var key = $"gift|{from.StringId}|{to.StringId}|{what}";
            var batch = Begin("gift", key);
            batch.Add(to, $"{Describe(from)}赠予你{what}。");
            batch.Publish();
        }

        /// <summary>索取：被索要的一方记住"谁向我要了什么、我给了"。</summary>
        public static void OnRequest(Hero? requester, Hero? provider, string what)
        {
            if (requester == null || provider == null || requester == provider) return;

            var batch = Begin("request", $"req|{requester.StringId}|{provider.StringId}|{what}");
            batch.Add(provider, $"{Describe(requester)}向你索要{what}，你给了。");
            batch.Publish();
        }

        public static void OnRaid(Hero? attacker, Settlement? village)
        {
            if (attacker == null || village == null || !village.IsVillage) return;
            var ownerClan = village.OwnerClan;
            if (ownerClan == null || ownerClan.Leader == attacker) return;

            var attackerH = Describe(attacker);
            var vn = village.Name?.ToString() ?? "某村";
            var batch = Begin("raid", $"raid|{village.StringId}|{attacker.StringId}");
            batch.AddClanLeader(ownerClan, $"{attackerH}的部队正开往你的村庄{vn}，意图劫掠。");
            batch.AddKingdomRuler(ownerClan.Kingdom, $"{attackerH}的部队正开往{ownerClan.Name}的村庄{vn}，意图劫掠。");
            batch.Publish();
        }

        // ---------- 文本工具 ----------

        private static string Describe(Hero? hero)
        {
            if (hero == null) return "";
            var kingdom = hero.Clan?.Kingdom?.Name?.ToString()
                          ?? (hero.MapFaction as Kingdom)?.Name?.ToString();
            return SilentDiaryText.Hero(kingdom, hero.Clan?.Name?.ToString(), hero.Name?.ToString() ?? "某人");
        }
    }
}
