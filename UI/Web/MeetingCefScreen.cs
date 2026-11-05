#if NET472
using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;

namespace AIChronicle
{
    /// <summary>会晤三栏：事项、现有聊天、版本化交涉卡。网页无游戏对象，文字/数字输入用原生 inquiry。</summary>
    public static class MeetingCefScreen
    {
        private static CefWebScreen? _screen;
        private static AIChatScreenVM? _chat;
        private static string? _sessionId;
        public static bool IsOpen => _screen?.IsOpen == true;

        public static bool Open(Hero hero)
        {
            if (IsOpen) return true;
            if (!hero.IsLord || !CefWebHost.EnsureInitialized()) return false;
            try
            {
                var session = MeetingService.Open(hero);
                _sessionId = session.Id;
                _chat = new AIChatScreenVM(hero, "conversation", session.Id);
                _chat.OnClose = Close;
                _chat.Changed += Refresh;
                MeetingService.Changed += Refresh;
                MeetingService.Recorded += OnRecorded;
                var screen = new CefWebScreen("meeting.html", "会晤 · " + hero.Name,
                    "交谈 · 交易 · 家族婚配 · 决斗", PageData(), 30, 1360, 800, windowed: true, contentPadding: 0f, showStatus: false,
                    wheelPixelsPerNotch: 96);
                // 捕获本次 session，旧页面已经入队的事件不能落进下一次会晤。
                screen.PageAction += (id, payload) => OnPageAction(session.Id, id, payload);
                screen.Closed += Cleanup;
                _screen = screen;
                screen.Open();
                if (!screen.IsOpen) { Cleanup(); return false; }
                return true;
            }
            catch (Exception ex)
            {
                UiTrace.Log("MeetingCefScreen.Open failed: " + ex);
                Close();
                return false;
            }
        }

        public static void Tick()
        {
            if (IsOpen && !MeetingService.IsActive(_sessionId)) Close();
        }

        public static void Close()
        {
            var screen = _screen;
            if (screen?.IsOpen == true) screen.Close();
            else Cleanup();
        }

        private static void Cleanup()
        {
            MeetingService.Close(); // VM 尚在时把未成交的撤回结果写入同一往来线程。
            MeetingService.Changed -= Refresh;
            MeetingService.Recorded -= OnRecorded;
            if (_chat != null)
            {
                _chat.Changed -= Refresh;
                try { _chat.MarkThreadReadIfPlayerThread(); }
                catch (Exception ex) { UiTrace.Log("Meeting close read marker failed: " + ex); }
                _chat.OnFinalize();
            }
            TtsService.Stop();
            _chat = null;
            _screen = null;
            _sessionId = null;
        }

        private static void OnRecorded(string text) => _chat?.AddMeetingRecord(text);

        private static JObject PageData()
        {
            var data = MeetingPageBuilder.BuildData();
            data["busy"] = _chat?.IsLoading == true;
            data["messages"] = new JArray((_chat?.Messages ?? new MBBindingList<ChatMessageVM>()).Select(m => new JObject
            {
                ["sender"] = m.SenderText, ["text"] = m.ContentText, ["role"] = m.Role, ["time"] = m.TimeText
            }));
            return data;
        }

        private static void Refresh()
        {
            if (_screen?.IsOpen == true) _screen.PushJson("render", PageData());
        }

        private static void OnPageAction(string expectedSession, string id, string payload)
        {
            MainThreadExecutor.Post(() =>
            {
                if (!IsOpen || expectedSession != _sessionId || !MeetingService.IsActive(expectedSession)) return;
                try
                {
                    var args = JObject.Parse(payload);
                    if (args["sessionId"]?.ToString() != expectedSession) return;
                    if (id == "refresh") { Refresh(); return; }
                    if (_chat?.IsLoading != false) return;
                    HandleAction(id, args, expectedSession);
                }
                catch (Exception ex)
                {
                    UiTrace.Log("Meeting page action failed " + id + ": " + ex);
                    ShowError("操作未完成：" + ex.Message);
                }
            });
        }

        private static void HandleAction(string id, JObject args, string sessionId)
        {
            switch (id)
            {
                case "speak":
                    Prompt("与" + MeetingService.Current!.Npc.Name + "交谈", "写下你想说的话。", sessionId, text => Send(text));
                    break;
                case "note":
                case "fieldText":
                    var fieldToken = args["editorToken"]?.ToString() ?? "";
                    var key = id == "note" ? "__note" : args["key"]?.ToString() ?? "";
                    Prompt(id == "note" ? "附上一句话" : args["label"]?.ToString() ?? "填写条件", "写下你想提出的内容。", sessionId,
                        text => _screen?.PushJson("setField", new { editorToken = fieldToken, key, text }));
                    break;
                case "preview":
                    var preview = MeetingTerms.Parse((JObject)args["terms"]!);
                    var s = MeetingService.Current!;
                    _screen?.PushJson("preview", new
                    {
                        editorToken = args["editorToken"]?.ToString(), revision = args["revision"]?.ToObject<int>(),
                        text = MeetingService.Validate(s, preview) ?? MeetingService.Describe(s, preview)
                    });
                    break;
                case "number":
                    var token = args["editorToken"]?.ToString() ?? "";
                    var path = args["path"]?.ToString() ?? "";
                    var maximum = Math.Max(0, args["maximum"]?.ToObject<int>() ?? int.MaxValue);
                    var minimum = Math.Min(maximum, args["minimum"]?.ToObject<int>() ?? 0);
                    Prompt("填写数量", "请输入 " + minimum + " 到 " + maximum + " 之间的整数。", sessionId, text =>
                    {
                        if (!int.TryParse(text, out var value) || value < minimum || value > maximum) { ShowError("请输入有效范围内的整数。"); return; }
                        _screen?.PushJson("setNumber", new { editorToken = token, path, value });
                    });
                    break;
                case "propose":
                    if (!(args["terms"] is JObject json)) { ShowError("请填写完整条件。"); return; }
                    var terms = MeetingTerms.Parse(json);
                    var reason = args["reason"]?.ToString() ?? "";
                    var result = MeetingService.Propose(sessionId, MeetingSide.Other, terms,
                        args["requestId"]?.ToString() ?? "", args["version"]?.ToObject<int>() ?? 0, reason);
                    if (result.StartsWith("[错误]")) ShowError(result);
                    else Send(string.IsNullOrWhiteSpace(reason) ? "请考虑我刚提出的条件，我们可以商量。" : reason);
                    Refresh();
                    break;
                case "respond":
                    var response = MeetingService.Respond(sessionId, MeetingSide.Other,
                        args["requestId"]?.ToString() ?? "", args["version"]?.ToObject<int>() ?? 0,
                        args["decision"]?.ToString() ?? "", "");
                    if (response.StartsWith("[错误]")) ShowError(response);
                    Refresh();
                    break;
                case "startDeferred":
                    var started = MeetingService.StartDeferred(sessionId, args["requestId"]?.ToString() ?? "", args["version"]?.ToObject<int>() ?? 0);
                    if (started.StartsWith("[错误]")) { ShowError(started); Refresh(); }
                    else Close();
                    break;
            }
        }

        private static void Send(string text)
        {
            if (_chat == null || string.IsNullOrWhiteSpace(text) || !MeetingService.IsActive(_sessionId)) return;
            _chat.InputText = text;
            _chat.ExecuteSend();
            Refresh();
        }

        private static void Prompt(string title, string hint, string sessionId, Action<string> onOk)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(title, hint, true, true, "确认", "取消",
                text => MainThreadExecutor.Post(() =>
                {
                    if (_sessionId == sessionId && MeetingService.IsActive(sessionId) && _chat?.IsLoading == false) onOk(text ?? "");
                }), () => { }, shouldInputBeObfuscated: false, textCondition: null), pauseGameActiveState: true, prioritize: true);
        }

        private static void ShowError(string text)
        {
            _screen?.PushJson("showError", new { message = text });
            InformationManager.DisplayMessage(new InformationMessage("[会晤] " + text, Colors.Yellow));
        }
    }
}
#endif
