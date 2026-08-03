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
        public static string GetDiplomacyDir()
        {
            var dir = Path.Combine(_baseDir, "World", "diplomacy");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static void StoreDiplomacyProposal(string proposerId, string targetId, string proposalType, string? tributeArg = null, string? message = null)
        {
            var dir = GetDiplomacyDir();
            var fileName = $"{SanitizeDir(proposerId)}_to_{SanitizeDir(targetId)}_{proposalType}.proposal";
            var path = Path.Combine(dir, fileName);
            var content = $"proposer={proposerId}\ntarget={targetId}\ntype={proposalType}";
            if (tributeArg != null)
                content += $"\ntribute={tributeArg}";
            if (!string.IsNullOrEmpty(message))
                content += $"\nmessage={message}";
            File.WriteAllText(path, content, Encoding.UTF8);
        }

        public static List<string> ListPendingProposals(string entityId)
        {
            var dir = GetDiplomacyDir();
            var results = new List<string>();
            if (!Directory.Exists(dir)) return results;
            var sanitizedId = SanitizeDir(entityId);
            foreach (var file in Directory.GetFiles(dir, $"*_to_{sanitizedId}_*.proposal"))
            {
                results.Add(Path.GetFileNameWithoutExtension(file));
            }
            return results;
        }

        public static string? ReadDiplomacyProposal(string proposalFileName)
        {
            var dir = GetDiplomacyDir();
            var path = Path.Combine(dir, SanitizeFile(proposalFileName) + ".proposal");
            if (!File.Exists(path)) return null;
            return File.ReadAllText(path, Encoding.UTF8).Trim();
        }

        public static void DeleteDiplomacyProposal(string proposalFileName)
        {
            var dir = GetDiplomacyDir();
            var path = Path.Combine(dir, SanitizeFile(proposalFileName) + ".proposal");
            if (File.Exists(path)) File.Delete(path);
        }

        public static string? FuzzyFindProposal(string fuzzyId, string targetEntityId)
        {
            var content = ReadDiplomacyProposal(fuzzyId);
            if (content != null) return fuzzyId;

            var pending = ListPendingProposals(targetEntityId);
            if (pending.Count == 0) return null;

            var lowerFuzzy = fuzzyId.ToLowerInvariant();

            string? bestMatch = null;
            int bestScore = 0;

            foreach (var p in pending)
            {
                var pContent = ReadDiplomacyProposal(p);
                if (pContent == null) continue;

                var lowerP = p.ToLowerInvariant();
                var score = 0;

                var typeProposer = ParseProposalMeta(pContent);

                if (lowerFuzzy.Contains(typeProposer.Type)) score += 10;

                var proposerParts = typeProposer.ProposerId.ToLowerInvariant().Split('_');
                foreach (var part in proposerParts)
                {
                    if (part.Length >= 2 && lowerFuzzy.Contains(part))
                        score += 5;
                }

                if (lowerP.Contains(lowerFuzzy)) score += 20;
                var commonChars = lowerFuzzy.Intersect(lowerP).Count();
                score += commonChars / 2;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestMatch = p;
                }
            }

            return bestScore >= 10 ? bestMatch : null;
        }

        public static (string ProposerId, string TargetId, string Type) ParseProposalMeta(string content)
        {
            var proposerId = "";
            var targetId = "";
            var type = "";
            foreach (var line in content.Split('\n'))
            {
                if (line.StartsWith("proposer=")) proposerId = line.Substring(9);
                else if (line.StartsWith("target=")) targetId = line.Substring(7);
                else if (line.StartsWith("type=")) type = line.Substring(5);
            }
            return (proposerId, targetId, type);
        }

        public static List<(string Id, string Type)> GetProposalsBetween(string entityA, string entityB)
        {
            var result = new List<(string Id, string Type)>();
            var sanitizedA = SanitizeDir(entityA);
            var sanitizedB = SanitizeDir(entityB);

            AddProposalsFromTargetList("A→B");
            AddProposalsFromTargetList("B→A");
            return result;

            void AddProposalsFromTargetList(string direction)
            {
                var targetId = direction == "A→B" ? entityB : entityA;
                var proposerSanitized = direction == "A→B" ? sanitizedA : sanitizedB;
                foreach (var p in ListPendingProposals(targetId))
                {
                    if (p.StartsWith(proposerSanitized + "_to_"))
                    {
                        var parts = p.Split('_');
                        var type = parts.Length >= 2 ? parts[parts.Length - 1] : "?";
                        result.Add((p, type));
                    }
                }
            }
        }

        /// <summary>
        /// 规范化相对路径：统一分隔符、去首尾斜杠。
        /// 安全修复：拒绝任何 `..` 路径穿越段，拒绝盘符/根路径等绝对路径。
        /// </summary>
    }
}
