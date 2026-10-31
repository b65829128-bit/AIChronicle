using System;

// 纯逻辑测试的最小 TaleWorlds 桩：只服务 MainThreadExecutor 的编译/诊断调用，
// 不模拟任何游戏行为。真实游戏使用 TaleWorlds 的实现。
namespace TaleWorlds.Library
{
    public sealed class Color
    {
    }

    public sealed class InformationMessage
    {
        public InformationMessage(string message, Color color)
        {
        }
    }

    public static class Colors
    {
        public static Color Red { get; } = new Color();
    }

    public static class InformationManager
    {
        public static void DisplayMessage(InformationMessage message)
        {
        }
    }
}

namespace AIChronicle
{
    internal static class UiTrace
    {
        public static void Log(string message)
        {
        }
    }
}
