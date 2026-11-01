using System.Collections.Generic;

namespace AIChronicle
{
    /// <summary>
    /// 朝议计票的纯逻辑（不依赖 TaleWorlds）：权重汇总、quorum、支持率与门槛。
    /// Stance 编码：1=支持，-1=反对，0=弃权，-2=未表态（仍计入应召权重）。
    /// </summary>
    public static class CuriaVoteMath
    {
        public static (float Support, float Oppose, float Abstain, float Summoned) Tally(
            IEnumerable<(float Weight, int Stance)> votes)
        {
            float support = 0f, oppose = 0f, abstain = 0f, summoned = 0f;
            foreach (var v in votes)
            {
                var w = v.Weight < 0f ? 0f : v.Weight;
                summoned += w;
                if (v.Stance == 1) support += w;
                else if (v.Stance == -1) oppose += w;
                else if (v.Stance == 0) abstain += w;
            }
            return (support, oppose, abstain, summoned);
        }

        public static float SupportRatio(float support, float oppose)
        {
            var decided = support + oppose;
            return decided <= 0.01f ? 0f : support / decided;
        }

        public static bool MeetsQuorum(float summonedWeight, float quorumWeight)
        {
            return summonedWeight + 0.01f >= quorumWeight;
        }

        public static bool Passes(float support, float oppose, float threshold)
        {
            var decided = support + oppose;
            if (decided <= 0.01f) return false;
            return support / decided + 0.0001f >= threshold;
        }
    }
}
