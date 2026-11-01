#if NET472
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>
    /// M 键参政页（CEF）：朝议 / 外交 / 封地 / 密陈 / 秘书。
    /// 页面只负责展示与选择；所有游戏状态修改经 PageAction 回主线程。
    /// </summary>
    public static class CuriaCefScreen
    {
        private static CefWebScreen? _screen;

        public static bool IsOpen => _screen != null && _screen.IsOpen;

        public static bool Open()
        {
            if (IsOpen) return true;
            if (!CefWebHost.EnsureInitialized()) return false;
            CleanupClosed();

            try
            {
                var data = CuriaService.BuildPageData(Hero.MainHero);
                var screen = new CefWebScreen(
                    "curia.html",
                    "朝议 · 参政",
                    "M · 朝议 / 外交 / 封地 / 密陈 / 秘书",
                    data,
                    30,
                    1280,
                    760,
                    windowed: true,
                    contentPadding: 0f,
                    showStatus: false);
                screen.PageAction += OnPageAction;
                screen.Closed += OnClosed;
                _screen = screen;
                screen.Open();
                if (!screen.IsOpen)
                {
                    Cleanup();
                    return false;
                }
                UiTrace.Log("CuriaCefScreen.Open ok");
                return true;
            }
            catch (Exception ex)
            {
                UiTrace.Log("CuriaCefScreen.Open failed: " + ex);
                Cleanup();
                return false;
            }
        }

        public static void Close()
        {
            _screen?.Close();
        }

        private static void CleanupClosed()
        {
            if (_screen != null && !_screen.IsOpen)
                _screen = null;
        }

        private static void Cleanup()
        {
            try { _screen?.Close(); } catch { }
            _screen = null;
        }

        private static void OnClosed()
        {
            _screen = null;
        }

        private static void Refresh()
        {
            try
            {
                _screen?.PushJson("render", CuriaService.BuildPageData(Hero.MainHero));
            }
            catch (Exception ex)
            {
                UiTrace.Log("CuriaCefScreen.Refresh failed: " + ex);
            }
        }

        private static void OnPageAction(string id, string payload)
        {
            UiTrace.Log($"CuriaCefScreen.PageAction id={id} payload={payload}");
            MainThreadExecutor.Post(() =>
            {
                if (_screen == null || !_screen.IsOpen) return;
                try
                {
                    HandleAction(id, payload);
                }
                catch (Exception ex)
                {
                    UiTrace.Log($"CuriaCefScreen.HandleAction failed id={id}: {ex}");
                    CuriaStore.Notify("[AI编年史] 参政操作失败：" + ex.Message, Colors.Red);
                }
            });
        }

        private static void HandleAction(string id, string payload)
        {
            JObject args;
            try { args = string.IsNullOrWhiteSpace(payload) ? new JObject() : JObject.Parse(payload); }
            catch { args = new JObject(); }

            switch (id)
            {
                case "vote":
                    {
                        var stanceText = args["stance"]?.ToString() ?? "";
                        var reason = args["reason"]?.ToString() ?? "";
                        if (!CuriaText.TryParseStance(stanceText, out var stance))
                            return;
                        if (CuriaService.SubmitVote(Hero.MainHero, args["agendaId"]?.ToString() ?? "", stance, reason, out var msg))
                            CuriaStore.Notify("[朝议] " + msg, Colors.Green);
                        else
                            CuriaStore.Notify("[朝议] " + msg, Colors.Yellow);
                        Refresh();
                        break;
                    }
                case "voteReason":
                    {
                        var stanceText = args["stance"]?.ToString() ?? "";
                        if (!CuriaText.TryParseStance(stanceText, out var stance))
                            return;
                        var agendaId = args["agendaId"]?.ToString() ?? "";
                        PromptText("附上投票理由（可选）", "一句话说明你的家族为何支持/反对/弃权。留空则只投票。", reason =>
                        {
                            if (CuriaService.SubmitVote(Hero.MainHero, agendaId, stance, reason, out var msg))
                                CuriaStore.Notify("[朝议] " + msg, Colors.Green);
                            else
                                CuriaStore.Notify("[朝议] " + msg, Colors.Yellow);
                            Refresh();
                        });
                        break;
                    }
                case "propose":
                    {
                        var type = args["type"]?.ToString() ?? "";
                        var targetKingdom = args["targetKingdom"]?.ToString() ?? "";
                        var settlement = args["settlementName"]?.ToString() ?? "";
                        var newOwner = args["newOwnerEntityId"]?.ToString() ?? "";
                        var targetClan = args["targetClanName"]?.ToString() ?? "";
                        var targetPolicy = args["policyName"]?.ToString() ?? "";
                        PromptText("提出朝议 · 理由", "写下这项议题的政治理由（会公开给所有封臣与史官）。", reason =>
                        {
                            if (CuriaService.Propose(Hero.MainHero, type, targetKingdom, settlement, newOwner, targetClan, targetPolicy, reason, out var msg))
                                CuriaStore.Notify("[朝议] " + msg, Colors.Green);
                            else
                                CuriaStore.Notify("[朝议] " + msg, Colors.Red);
                            Refresh();
                        });
                        break;
                    }
                case "transferOwnFief":
                    {
                        var settlement = args["settlementName"]?.ToString() ?? "";
                        var newOwner = args["newOwnerEntityId"]?.ToString() ?? "";
                        PromptText("转让自己的封地", "统治者可自由转让自己家族的封地。可写一句册封理由。", reason =>
                        {
                            var prev = AIChatClient.CurrentHero;
                            AIChatClient.CurrentHero = Hero.MainHero;
                            try
                            {
                                var msg = DiplomacyService.ExecuteTransferFief(settlement, newOwner, reason);
                                CuriaStore.Notify("[封地] " + msg, msg.StartsWith("[错误]") ? Colors.Red : Colors.Green);
                            }
                            finally { AIChatClient.CurrentHero = prev; }
                            Refresh();
                        });
                        break;
                    }
                case "defend":
                    {
                        PromptText("陈情", "写下你家族对这项处置的辩护。陈情会公开给所有投票者。", reason =>
                        {
                            if (CuriaService.SubmitDefense(Hero.MainHero, args["agendaId"]?.ToString() ?? "", reason, out var msg))
                                CuriaStore.Notify("[朝议] " + msg, Colors.Green);
                            else
                                CuriaStore.Notify("[朝议] " + msg, Colors.Yellow);
                            Refresh();
                        });
                        break;
                    }
                case "respondProposal":
                    {
                        var proposalId = args["proposalId"]?.ToString() ?? "";
                        var accepted = args["accepted"]?.ToObject<bool>() ?? false;
                        var prev = AIChatClient.CurrentHero;
                        AIChatClient.CurrentHero = Hero.MainHero;
                        try
                        {
                            var msg = DiplomacyService.ExecuteRespondToProposal(proposalId, accepted);
                            CuriaStore.Notify("[外交] " + msg, accepted ? Colors.Green : Colors.Yellow);
                        }
                        finally { AIChatClient.CurrentHero = prev; }
                        Refresh();
                        break;
                    }
                case "secretAdvisory":
                    {
                        PromptText("密陈", "写下只给国王一人看的密陈（不入史册）。", content =>
                        {
                            CuriaStore.AppendSecretAdvisory(Hero.MainHero, content);
                            CuriaStore.Notify("密陈已呈交国王。", Colors.Green);
                            Refresh();
                        });
                        break;
                    }
                case "consultSend":
                    {
                        var target = args["targetKingdom"]?.ToString() ?? "";
                        PromptText("遣使问询 · " + target, "写下你要问询对方国王的话。", message =>
                        {
                            var prev = AIChatClient.CurrentHero;
                            AIChatClient.CurrentHero = Hero.MainHero;
                            try
                            {
                                var msg = ToolExecutor.ExecuteConsultKing(target, message);
                                CuriaStore.Notify("[外交] " + msg, msg.StartsWith("[错误]") || msg.StartsWith("[冷却]") ? Colors.Yellow : Colors.Green);
                            }
                            finally { AIChatClient.CurrentHero = prev; }
                            Refresh();
                        });
                        break;
                    }
                case "consultReply":
                    {
                        var target = args["targetKingdom"]?.ToString() ?? "";
                        PromptText("回复问询 · " + target, "写下你要回复对方国王的话。", message =>
                        {
                            var prev = AIChatClient.CurrentHero;
                            AIChatClient.CurrentHero = Hero.MainHero;
                            try
                            {
                                var msg = ToolExecutor.ExecuteReplyConsult(target, message);
                                CuriaStore.Notify("[外交] " + msg, msg.StartsWith("[错误]") ? Colors.Red : Colors.Green);
                            }
                            finally { AIChatClient.CurrentHero = prev; }
                            Refresh();
                        });
                        break;
                    }
                case "openSecretary":
                    {
                        Close();
                        AIChatScreen.OpenChancery();
                        break;
                    }
                case "openLetters":
                    {
                        Close();
                        LetterListScreen.Open();
                        break;
                    }
                case "refresh":
                    Refresh();
                    break;
            }
        }

        private static void PromptText(string title, string hint, Action<string> onOk)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(
                title,
                hint,
                true,
                true,
                "确认",
                "取消",
                text => MainThreadExecutor.Post(() => onOk(text ?? "")),
                () => { },
                shouldInputBeObfuscated: false,
                textCondition: null), pauseGameActiveState: true, prioritize: true);
        }
    }
}
#endif
