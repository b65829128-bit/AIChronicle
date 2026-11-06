using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace AIChronicle
{
    public partial class HistoryRecorder
    {
        // 只比较实际归属，不按日去重：同日离开再返回仍是两件事。读档仅采基线，不回填。
        private readonly Dictionary<Clan, ClanAllegiance> _clanAllegiances = new();

        private sealed class ClanAllegiance
        {
            public readonly Kingdom? Kingdom;
            public readonly bool Mercenary;
            public ClanAllegiance(Kingdom? kingdom, bool mercenary) { Kingdom = kingdom; Mercenary = mercenary; }
            public bool Matches(ClanAllegiance other) => Kingdom == other.Kingdom && Mercenary == other.Mercenary;
            public static ClanAllegiance Read(Clan clan) => new(clan.Kingdom, clan.IsUnderMercenaryService);
        }

        private sealed class AllegianceNotice
        {
            public string Text = "";
            public string Date = "";
            public readonly HashSet<string> Recipients = new(StringComparer.Ordinal);
        }

        private void RegisterClanAllegianceEvents()
        {
            CaptureClanAllegianceBaseline();
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this,
                new Action<CampaignGameStarter>(_ => ObserveClanAllegiance(CaptureClanAllegianceBaseline)));
            CampaignEvents.OnClanChangedKingdomEvent.AddNonSerializedListener(this,
                new Action<Clan, Kingdom, Kingdom, ChangeKingdomAction.ChangeKingdomActionDetail, bool>(
                    (clan, oldRealm, newRealm, detail, show) => ObserveClanAllegiance(
                        () => OnClanChangedKingdom(clan, oldRealm, newRealm, detail, show))));
            CampaignEvents.OnMercenaryServiceStartedEvent.AddNonSerializedListener(this,
                new Action<Clan, StartMercenaryServiceAction.StartMercenaryServiceActionDetails>(
                    (clan, _) => ObserveClanAllegiance(() => OnClanMercenaryStarted(clan))));
            CampaignEvents.OnMercenaryServiceEndedEvent.AddNonSerializedListener(this,
                new Action<Clan, EndMercenaryServiceAction.EndMercenaryServiceActionDetails>(
                    (clan, detail) => ObserveClanAllegiance(() => OnClanMercenaryEnded(clan, detail))));
        }

        private void CaptureClanAllegianceBaseline()
        {
            _clanAllegiances.Clear();
            if (Campaign.Current == null) return;
            foreach (var clan in Clan.All) _clanAllegiances[clan] = ClanAllegiance.Read(clan);
        }

        private static void ObserveClanAllegiance(Action action)
        {
            if (Campaign.Current == null) return;
            try { action(); }
            catch (Exception e) { DebugLogger.Log("家族归属记录失败：" + e.Message); }
        }

        private void OnClanChangedKingdom(Clan clan, Kingdom? oldKingdom, Kingdom? newKingdom,
            ChangeKingdomAction.ChangeKingdomActionDetail detail, bool showNotifications)
        {
            if (clan == null) return;
            var clanName = clan.Name?.ToString() ?? "未知氏族";
            // 保留原始史料入口；原版的界面通知开关不影响史料或静默日记。
            RecordEvent("clan_changed_kingdom", $"{clanName}脱离{oldKingdom?.Name?.ToString() ?? "无"}，加入{newKingdom?.Name?.ToString() ?? "独立"}");
            if (detail == ChangeKingdomAction.ChangeKingdomActionDetail.CreateKingdom && newKingdom != null)
                RecordEvent("kingdom_created", $"{clanName}建立了新王国 {newKingdom.Name}");

            var before = _clanAllegiances.TryGetValue(clan, out var known) ? known
                : new ClanAllegiance(oldKingdom, detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveAsMercenary);
            RecordClanAllegiance(clan, before, new ClanAllegiance(newKingdom, clan.IsUnderMercenaryService), detail);
        }

        private void OnClanMercenaryStarted(Clan clan)
        {
            if (clan == null) return;
            var before = _clanAllegiances.TryGetValue(clan, out var known) ? known : new ClanAllegiance(null, false);
            RecordClanAllegiance(clan, before, ClanAllegiance.Read(clan));
        }

        private void OnClanMercenaryEnded(Clan clan, EndMercenaryServiceAction.EndMercenaryServiceActionDetails detail)
        {
            if (clan == null) return;
            if (!_clanAllegiances.TryGetValue(clan, out var before))
                _clanAllegiances[clan] = before = new ClanAllegiance(clan.Kingdom, true);
            if (!before.Mercenary) return;
            var after = ClanAllegiance.Read(clan);
            if (detail == EndMercenaryServiceAction.EndMercenaryServiceActionDetails.ApplyByBecomingVassal)
            {
                // 原版同国佣兵转封臣不一定发送 OnClanChangedKingdom，身份已最终生效。
                RecordClanAllegiance(clan, before, after);
                return;
            }

            // 其余结束事件可能嵌在换国中，彼时 Kingdom 尚未最终赋值。
            // 先保留当时日期/收件人；外层换国或开佣事件若已处理，引用检查会取消此中间通知。
            var notice = CaptureAllegianceNotice(clan, before, after);
            var campaign = Campaign.Current;
            MainThreadExecutor.Post(() => ObserveClanAllegiance(() =>
            {
                if (Campaign.Current != campaign || !_clanAllegiances.TryGetValue(clan, out var current)
                    || !ReferenceEquals(current, before) || !after.Matches(ClanAllegiance.Read(clan))) return;
                _clanAllegiances[clan] = after;
                if (!before.Matches(after) && !clan.IsEliminated) WriteAllegianceNotice(notice);
            }));
        }

        private void RecordClanAllegiance(Clan clan, ClanAllegiance before, ClanAllegiance after,
            ChangeKingdomAction.ChangeKingdomActionDetail? detail = null)
        {
            if (before.Matches(after)) return;
            // 开关关闭时也推进基线；再次开启不补记关闭期间的旧事。
            _clanAllegiances[clan] = after;
            if (clan.IsEliminated || detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByClanDestruction) return;
            WriteAllegianceNotice(CaptureAllegianceNotice(clan, before, after, detail));
        }

        private static AllegianceNotice CaptureAllegianceNotice(Clan clan, ClanAllegiance before, ClanAllegiance after,
            ChangeKingdomAction.ChangeKingdomActionDetail? detail = null)
        {
            var notice = new AllegianceNotice { Date = AgentManager.GetDiaryDatePrefix() };
            if (!SilentDiary.Enabled || clan.IsEliminated) return notice;
            notice.Text = DescribeClanAllegiance(clan, before, after, detail);
            AddAllegianceRecipient(notice, clan.Leader);
            if (before.Kingdom?.IsEliminated == false) AddAllegianceRecipient(notice, before.Kingdom.RulingClan?.Leader);
            if (after.Kingdom?.IsEliminated == false) AddAllegianceRecipient(notice, after.Kingdom.RulingClan?.Leader);
            return notice;
        }

        private static void AddAllegianceRecipient(AllegianceNotice notice, Hero? hero)
        {
            if (hero == null || !hero.IsAlive || hero == Hero.MainHero) return;
            var entity = EntityManager.GetOrCreateEntity(hero);
            if (entity.Controller == EntityController.Agent) notice.Recipients.Add(entity.Id);
        }

        private static void WriteAllegianceNotice(AllegianceNotice notice)
        {
            foreach (var id in notice.Recipients)
                AgentManager.AppendSystemDiary(id, "情报", notice.Text, notice.Date);
        }

        private static string DescribeClanAllegiance(Clan clan, ClanAllegiance before, ClanAllegiance after,
            ChangeKingdomAction.ChangeKingdomActionDetail? detail)
        {
            var clanName = clan.Name?.ToString() ?? "未知氏族";
            var actor = clan.Leader == null ? clanName : SilentDiaryText.Hero(null, clanName,
                clan.Leader.Name?.ToString() ?? "", "族长") + "率" + clanName;
            var oldName = before.Kingdom?.Name?.ToString() ?? "";
            var newName = after.Kingdom?.Name?.ToString() ?? "";
            if (before.Kingdom == after.Kingdom && after.Kingdom != null)
                return before.Mercenary
                    ? $"{actor}结束为{oldName}的佣兵效力，成为{newName}的封臣。"
                    : $"{actor}改以佣兵身份为{newName}效力。";

            var departure = before.Kingdom == null ? ""
                : detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveByKingdomDestruction ? $"因{oldName}覆亡而脱离{oldName}，"
                : before.Mercenary ? $"结束为{oldName}的佣兵效力，"
                : detail == ChangeKingdomAction.ChangeKingdomActionDetail.LeaveWithRebellion ? $"反叛并脱离{oldName}，"
                : $"脱离{oldName}，";
            var arrival = after.Kingdom == null ? "不再隶属任何王国。"
                : detail == ChangeKingdomAction.ChangeKingdomActionDetail.CreateKingdom ? $"建立{newName}。"
                : after.Mercenary ? $"以佣兵身份为{newName}效力。"
                : detail == ChangeKingdomAction.ChangeKingdomActionDetail.JoinKingdomByDefection ? $"转投{newName}，成为{newName}的封臣。"
                : $"加入{newName}，成为{newName}的封臣。";
            return actor + departure + arrival;
        }
    }
}
