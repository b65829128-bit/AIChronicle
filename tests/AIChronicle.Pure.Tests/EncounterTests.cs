using System;
using System.Collections.Generic;
using AIChronicle;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace AIChronicle.Tests
{
    internal static partial class Program
    {
        private static readonly DateTime EncounterTestTime = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

        private static void RunEncounterTests()
        {
            Run("encounter: mission/settlement/UI/battle protection", TestEncounterProtectedContexts);
            Run("encounter: stale world-map recovery after 8 seconds", TestEncounterStaleRecovery);
            Run("encounter: protected contexts restart countdown", TestEncounterCountdownReset);
            Run("encounter: new encounter/session restart countdown", TestEncounterReplacement);
            Run("encounter: manual repair shares protection", TestEncounterManualRepair);
            Run("encounter: let_go checks context at execution", TestEncounterLetGoProtection);
            Run("encounter: let_go cannot finish a replacement encounter", TestEncounterLetGoReplacement);
        }

        // 每个保护信号独立测试，避免 MissionState 掩盖 Mission.Current 或定居点保护的缺失。
        private static List<(string Name, Action Enter)> EncounterBlockedContexts() => new()
        {
            ("tournament", () =>
            {
                Game.Current!.GameStateManager!.ActiveState = new MissionState();
                Mission.Current = new Mission();
                var town = new Settlement();
                MobileParty.MainParty!.CurrentSettlement = town;
                PlayerEncounter.Current!.Settlement = town;
                Campaign.Current!.LocationEncounter = new object();
            }),
            ("mission state", () => Game.Current!.GameStateManager!.ActiveState = new MissionState()),
            ("mission with map state", () => Mission.Current = new Mission()),
            ("unknown state", () => Game.Current!.GameStateManager!.ActiveState = null),
            ("other state", () => Game.Current!.GameStateManager!.ActiveState = new OtherGameState()),
            ("missing game", () => Game.Current = null),
            ("missing campaign", () => Campaign.Current = null),
            ("missing party", () => MobileParty.MainParty = null),
            ("inactive party", () => MobileParty.MainParty!.IsActive = false),
            ("party settlement", () => MobileParty.MainParty!.CurrentSettlement = new Settlement()),
            ("encounter settlement", () => PlayerEncounter.Current!.Settlement = new Settlement()),
            ("location encounter", () => Campaign.Current!.LocationEncounter = new object()),
            ("menu", () => ((MapState)Game.Current!.GameStateManager!.ActiveState!).AtMenu = true),
            ("map conversation", () => ((MapState)Game.Current!.GameStateManager!.ActiveState!).MapConversationActive = true),
            ("conversation", () => Campaign.Current!.ConversationManager!.IsConversationInProgress = true),
            ("party map event", () => MobileParty.MainParty!.MapEvent = new object()),
            ("encounter battle", () => PlayerEncounter.Current!.MapEvent = new object()),
            ("battle simulation", () => ((MapState)Game.Current!.GameStateManager!.ActiveState!).IsSimulationActive = true),
            ("AI chat", () => AIChatScreen.IsOpen = true),
            ("letters", () => LetterListScreen.IsOpen = true),
            ("history", () => HistoryScreen.IsOpen = true),
            ("kit UI", () => KitScreen.AnyOpen = true),
        };

        private static void RestoreEncounterMap(PlayerEncounter encounter)
        {
            Campaign.Current ??= new Campaign();
            Campaign.Current.PlayerEncounter = encounter;
            Campaign.Current.LocationEncounter = null;
            Campaign.Current.ConversationManager = new ConversationManager();
            Game.Current = new Game { GameStateManager = new GameStateManager { ActiveState = new MapState() } };
            MobileParty.MainParty = new MobileParty();
            Mission.Current = null;
            encounter.Settlement = null;
            encounter.MapEvent = null;
            AIChatScreen.IsOpen = LetterListScreen.IsOpen = HistoryScreen.IsOpen = KitScreen.AnyOpen = false;
        }

        private static PlayerEncounter ResetEncounterTest()
        {
            MainThreadExecutor.BeginCampaign();
            PlayerEncounter.FinishCalls = 0;
            var encounter = new PlayerEncounter();
            RestoreEncounterMap(encounter);
            EncounterDiagnostics.OnSessionLaunched();
            return encounter;
        }

        private static void EncounterTick(double seconds) =>
            EncounterDiagnostics.TickCore(EncounterTestTime.AddSeconds(seconds));

        private static void TestEncounterProtectedContexts()
        {
            foreach (var context in EncounterBlockedContexts())
            {
                var encounter = ResetEncounterTest();
                context.Enter();
                EncounterTick(0);
                EncounterTick(30);
                AssertEqual(0, PlayerEncounter.FinishCalls, context.Name);
                if (Campaign.Current != null)
                    AssertTrue(ReferenceEquals(encounter, PlayerEncounter.Current), context.Name + " preserves encounter");
            }
        }

        private static void TestEncounterStaleRecovery()
        {
            ResetEncounterTest();
            EncounterTick(0);
            EncounterTick(7.999);
            AssertEqual(0, PlayerEncounter.FinishCalls, "full 8 seconds required");
            EncounterTick(8);
            AssertEqual(1, PlayerEncounter.FinishCalls);
            AssertTrue(PlayerEncounter.Current == null, "stale encounter cleared");
            EncounterTick(30);
            AssertEqual(1, PlayerEncounter.FinishCalls, "only finishes once");
        }

        private static void TestEncounterCountdownReset()
        {
            foreach (var context in EncounterBlockedContexts())
            {
                var encounter = ResetEncounterTest();
                EncounterTick(0);
                EncounterTick(7);
                context.Enter();
                EncounterTick(120);
                RestoreEncounterMap(encounter);
                EncounterTick(121);
                EncounterTick(128.999);
                AssertEqual(0, PlayerEncounter.FinishCalls, context.Name + " resets old countdown");
                EncounterTick(129);
                AssertEqual(1, PlayerEncounter.FinishCalls, context.Name + " allows later recovery");
            }
        }

        private static void TestEncounterReplacement()
        {
            ResetEncounterTest();
            EncounterTick(0);
            EncounterTick(7);
            var nextEncounter = new PlayerEncounter();
            Campaign.Current!.PlayerEncounter = nextEncounter;
            EncounterTick(7.5);
            EncounterTick(8);
            AssertEqual(0, PlayerEncounter.FinishCalls, "new encounter does not inherit previous timer");
            EncounterTick(15.5);
            AssertEqual(1, PlayerEncounter.FinishCalls);

            ResetEncounterTest();
            EncounterTick(0);
            EncounterDiagnostics.OnSessionLaunched();
            EncounterTick(50);
            EncounterTick(57.999);
            AssertEqual(0, PlayerEncounter.FinishCalls, "session resets timer");
            EncounterTick(58);
            AssertEqual(1, PlayerEncounter.FinishCalls);
        }

        private static void TestEncounterManualRepair()
        {
            foreach (var context in EncounterBlockedContexts())
            {
                ResetEncounterTest();
                context.Enter();
                EncounterDiagnostics.RepairFromSettings();
                AssertEqual(0, PlayerEncounter.FinishCalls, context.Name);
            }

            ResetEncounterTest();
            EncounterDiagnostics.RepairFromSettings();
            AssertEqual(1, PlayerEncounter.FinishCalls, "manual recovery on idle map");
            EncounterDiagnostics.RepairFromSettings();
            AssertEqual(1, PlayerEncounter.FinishCalls, "no encounter is a no-op");
        }

        private static void TestEncounterLetGoProtection()
        {
            foreach (var context in EncounterBlockedContexts())
            {
                if (context.Name == "menu") continue; // 放行允许主动结束当前遭遇菜单。
                var encounter = ResetEncounterTest();
                encounter.Leave = true;
                EncounterDiagnostics.FinishAfterLetGo();
                context.Enter(); // 投递后才进入场景，执行时必须重新判断。
                MainThreadExecutor.Tick();
                AssertEqual(0, PlayerEncounter.FinishCalls, context.Name);
            }

            var allowed = ResetEncounterTest();
            allowed.Leave = true;
            ((MapState)Game.Current!.GameStateManager!.ActiveState!).AtMenu = true;
            EncounterDiagnostics.FinishAfterLetGo();
            MainThreadExecutor.Tick();
            AssertEqual(1, PlayerEncounter.FinishCalls, "let_go can finish its own encounter menu");
        }

        private static void TestEncounterLetGoReplacement()
        {
            var previous = ResetEncounterTest();
            previous.Leave = true;
            EncounterDiagnostics.FinishAfterLetGo();
            var replacement = new PlayerEncounter { Leave = true };
            Campaign.Current!.PlayerEncounter = replacement;
            MainThreadExecutor.Tick();
            AssertEqual(0, PlayerEncounter.FinishCalls, "queued let_go must not affect replacement");
            AssertTrue(ReferenceEquals(replacement, PlayerEncounter.Current), "replacement preserved");

            ResetEncounterTest();
            EncounterDiagnostics.FinishAfterLetGo();
            MainThreadExecutor.Tick();
            AssertEqual(0, PlayerEncounter.FinishCalls, "no leave flag means no cleanup");
        }
    }
}

// 最小状态桩：只提供生产闸门读取的属性，并记录是否调用 Finish；不模拟游戏比赛流程。
namespace TaleWorlds.CampaignSystem
{
    public sealed class Campaign
    {
        public static Campaign? Current { get; set; }
        public Encounters.PlayerEncounter? PlayerEncounter { get; set; }
        public object? LocationEncounter { get; set; }
        public ConversationManager? ConversationManager { get; set; }
        public string TimeControlMode => "Stop";
    }

    public sealed class ConversationManager
    {
        public bool IsConversationInProgress { get; set; }
    }
}

namespace TaleWorlds.CampaignSystem.Settlements
{
    public sealed class Settlement
    {
        public string Name => "测试城镇";
        public string StringId => "test_town";
    }
}

namespace TaleWorlds.CampaignSystem.Party
{
    public sealed class MobileParty
    {
        public static MobileParty? MainParty { get; set; }
        public bool IsActive { get; set; } = true;
        public object? MapEvent { get; set; }
        public Settlements.Settlement? CurrentSettlement { get; set; }
        public string Name => "测试队伍";
        public string StringId => "test_party";
    }
}

namespace TaleWorlds.CampaignSystem.Encounters
{
    public sealed class PlayerEncounter
    {
        public static PlayerEncounter? Current => Campaign.Current!.PlayerEncounter;
        public static object? LocationEncounter => Campaign.Current!.LocationEncounter;
        public static bool LeaveEncounter => Current!.Leave;
        public static Party.MobileParty? EncounteredMobileParty => null;
        public static Settlements.Settlement? EncounterSettlement => Current?.Settlement;
        public static object? Battle => Current?.MapEvent;
        public static int FinishCalls { get; set; }
        public bool Leave { get; set; }
        public Settlements.Settlement? Settlement { get; set; }
        public object? MapEvent { get; set; }
        public string EncounterState => "Begin";

        public static void Finish()
        {
            FinishCalls++;
            Campaign.Current!.PlayerEncounter = null;
            Campaign.Current.LocationEncounter = null;
            Party.MobileParty.MainParty!.CurrentSettlement = null;
        }
    }
}

namespace TaleWorlds.Core
{
    public abstract class GameState { }
    public sealed class OtherGameState : GameState { }
    public sealed class GameStateManager { public GameState? ActiveState { get; set; } }
    public sealed class Game
    {
        public static Game? Current { get; set; }
        public GameStateManager? GameStateManager { get; set; }
    }
}

namespace TaleWorlds.CampaignSystem.GameState
{
    public sealed class MapState : TaleWorlds.Core.GameState
    {
        public bool AtMenu { get; set; }
        public bool MapConversationActive { get; set; }
        public bool IsSimulationActive { get; set; }
    }
}

namespace TaleWorlds.MountAndBlade
{
    public sealed class Mission { public static Mission? Current { get; set; } }
    public sealed class MissionState : TaleWorlds.Core.GameState { }
}

namespace AIChronicle
{
    internal static class AIChatScreen { public static bool IsOpen { get; set; } }
    internal static class LetterListScreen { public static bool IsOpen { get; set; } }
    internal static class HistoryScreen { public static bool IsOpen { get; set; } }
    internal static class KitScreen { public static bool AnyOpen { get; set; } }
}
