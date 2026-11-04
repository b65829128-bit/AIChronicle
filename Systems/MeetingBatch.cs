using System;
using System.Collections.Generic;
using System.Linq;

namespace AIChronicle
{
    /// <summary>执行前绑定到具体对象的效果。资源键用来拒绝重复释放、同一家族两次换国等冲突。</summary>
    public sealed class MeetingEffect
    {
        public string Name { get; set; } = "";
        public int Order { get; set; }
        public IReadOnlyList<string> Resources { get; set; } = Array.Empty<string>();
        public Func<string?> Validate { get; set; } = () => null;
        public Action Apply { get; set; } = () => { };
        public Func<bool> IsApplied { get; set; } = () => false;
        public Func<bool> HasChanged { get; set; } = () => false;
        public Action? Undo { get; set; }
    }

    public sealed class MeetingPartialExecutionException : Exception
    {
        public MeetingPartialExecutionException(string message, Exception inner) : base(message, inner) { }
    }

    public static class MeetingBatch
    {
        public static string? Validate(IReadOnlyList<MeetingEffect> effects)
        {
            var resources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var effect in effects)
            {
                foreach (var key in effect.Resources)
                    if (!resources.Add(key)) return "条款有重复或互相冲突的行动：" + effect.Name + "。请合并或分开交涉。";
                var invalid = effect.Validate();
                if (invalid != null) return invalid;
            }
            return null;
        }

        /// <summary>先全量预检、再交付财物、按顺序执行。不可撤销效果已发生时保留实际履行记录，绝不假称完整回滚。</summary>
        public static IReadOnlyList<Exception> Apply(Action transfer, Action rollback, IReadOnlyList<MeetingEffect> effects)
        {
            var invalid = Validate(effects);
            if (invalid != null) throw new InvalidOperationException(invalid);
            var ordered = effects.OrderBy(e => e.Order).ToList();
            var completed = new List<MeetingEffect>();
            var recovered = new List<Exception>();
            var transferred = false;
            try
            {
                transfer();
                transferred = true;
                foreach (var effect in ordered)
                {
                    invalid = effect.Validate();
                    if (invalid != null) throw new InvalidOperationException(invalid);
                    var returned = false;
                    try
                    {
                        effect.Apply();
                        returned = true;
                    }
                    catch (Exception ex)
                    {
                        // 原版事件回调报错但效果已完成时，保留真实结果并继续剩余约定。
                        if (!Observe(effect)) throw;
                        recovered.Add(ex);
                    }
                    if (returned && !Observe(effect)) throw new InvalidOperationException("未能核实行动生效：" + effect.Name);
                    completed.Add(effect);
                }
                return recovered;
            }
            catch (MeetingPartialExecutionException) { throw; }
            catch (Exception ex)
            {
                var partial = ordered.Where(e => e.Undo == null && !completed.Contains(e) && Observe(e, changed: true)).ToList();
                if (completed.Any(e => e.Undo == null) || partial.Count > 0)
                {
                    var pending = ordered.Except(completed).Select(e => e.Name);
                    throw new MeetingPartialExecutionException("约定部分履行。已执行：" + string.Join("、", completed.Select(e => e.Name))
                        + (partial.Count > 0 ? "；仅部分生效：" + string.Join("、", partial.Select(e => e.Name)) : "")
                        + "；未完成：" + string.Join("、", pending) + (transferred ? "。财物已按约交付" : "。财物交付未完成")
                        + "，停止后续行动，不自动重试。原因：" + ex.Message, ex);
                }
                try
                {
                    foreach (var effect in completed.AsEnumerable().Reverse()) effect.Undo!();
                    rollback();
                }
                catch (Exception compensation) { throw new AggregateException("提交失败，恢复也失败，请查看日志。", ex, compensation); }
                throw;
            }
        }

        private static bool Observe(MeetingEffect effect, bool changed = false)
        {
            try { return changed ? effect.HasChanged() : effect.IsApplied(); }
            catch (Exception ex)
            {
                throw new MeetingPartialExecutionException("无法核实行动状态：" + effect.Name
                    + "。财物已交付，停止后续行动，不自动退款或重试。", ex);
            }
        }
    }
}
