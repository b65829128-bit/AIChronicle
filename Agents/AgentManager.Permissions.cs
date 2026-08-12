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
        private static string? NormalizeRelPath(string relPath)
        {
            if (string.IsNullOrEmpty(relPath)) return null;
            var normalized = relPath.Replace('\\', '/').Trim('/');
            if (normalized.Length == 0) return null;
            if (normalized.Split('/').Any(seg => seg == "..")) return null;
            if (normalized.Contains(':')) return null;
            if (normalized.StartsWith("/")) return null;
            return normalized;
        }

        private static bool IsPathAllowed(string relPath, bool read, bool write = false)
        {
            relPath = NormalizeRelPath(relPath) ?? "";
            if (relPath.Length == 0) return false;

            var dirPart = Path.GetDirectoryName(relPath)?.Replace('\\', '/') ?? "";

            var isWorldPath = _readableWorldFiles.Contains(relPath)
                || _readableWorldDirs.Any(d => relPath.StartsWith(d + "/") || relPath == d);

            if (read && !write)
            {
                if (_readableDirs.Contains(dirPart)) return true;
                if (IsConsultAllowed(relPath)) return true;        // 外交问询：史官任何国家、参与双方国王
                if (IsCorrespondenceAllowed(relPath)) return true; // 私有密使线程：仅参与者双方（史官不可读）
                if (IsPublicDocAllowed(relPath)) return true;      // 公开诏令/谏言：史官任何国家、其他仅本国
                if (IsSecretAdvisoryAllowed(relPath)) return true; // 秘密谏言：仅本国国王
                if (isWorldPath) return true;                      // 其余世界只读文件（史料/编年史/世界名册）
            }

            if (write && _writableDirs.Contains(dirPart))
                return true;

            if (write && (_agentEntityId.Value == "__historian__" || _agentEntityId.Value == "__fate__"))
            {
                if (relPath.StartsWith("history/chronicles/") || relPath == "history/chronicles")
                    return true;
            }

            return false;
        }

        private static string? ResolvePath(string relPath)
        {
            relPath = NormalizeRelPath(relPath) ?? "";
            // 空路径 = Agent 自己的根目录（如 list_dir("")）
            if (relPath.Length == 0)
            {
                if (string.IsNullOrEmpty(_agentDir)) return null;
                return Path.GetFullPath(_agentDir);
            }

            if (_readableWorldFiles.Contains(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (IsConsultAllowed(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (IsCorrespondenceAllowed(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (IsPublicDocAllowed(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (IsSecretAdvisoryAllowed(relPath))
                return Path.Combine(_baseDir, "World", relPath);

            if (_readableWorldDirs.Any(d => relPath.StartsWith(d + "/") || relPath == d))
                return Path.Combine(_baseDir, "World", relPath);

            if (string.IsNullOrEmpty(_agentDir))
                return null;

            var full = Path.GetFullPath(Path.Combine(_agentDir, relPath));
            var agentRoot = Path.GetFullPath(_agentDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(agentRoot, StringComparison.Ordinal) ? full : null;
        }

        /// <summary>从 {王国}_{年} 文件名提取王国名（去掉末尾 _年份）。</summary>
        private static string? KingdomNameFromFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return null;
            var idx = fileName.LastIndexOf('_');
            if (idx > 0) fileName = fileName.Substring(0, idx);
            return string.IsNullOrEmpty(fileName) ? null : fileName;
        }

        /// <summary>
        /// 公开诏令/谏言（World/advisory|edict/）的读取授权：史官可读任何国家；其他 agent 仅本国
        /// （按文件名王国名与自身王国匹配）。非史官不可列整个目录，只能读本国文件。
        /// </summary>
        private static bool IsPublicDocAllowed(string relPath)
        {
            var isPublicDoc = relPath == "advisory" || relPath.StartsWith("advisory/", StringComparison.Ordinal)
                           || relPath == "edict" || relPath.StartsWith("edict/", StringComparison.Ordinal);
            if (!isPublicDoc) return false;

            if (_agentEntityId.Value == "__historian__")
                return true; // 史官：任何国家的公开诏令/谏言

            if (relPath == "advisory" || relPath == "edict")
                return false; // 非史官不可列整个目录，只读本国文件

            var hero = EntityManager.GetEntityById(_agentEntityId.Value ?? "")?.HeroRef;
            var kingdom = hero?.MapFaction as Kingdom;
            if (kingdom == null) return false;
            var kingdomName = KingdomNameFromFile(Path.GetFileNameWithoutExtension(relPath));
            return kingdomName != null
                && string.Equals(kingdom.Name?.ToString(), kingdomName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 秘密谏言（World/secret_advisory/{王国}_{年}.txt）的读取授权：只有本国国王可读。
        /// 封臣与史官（__historian__，HeroRef 为 null）及异国人员天然无权。
        /// </summary>
        private static bool IsSecretAdvisoryAllowed(string relPath)
        {
            if (!relPath.StartsWith("secret_advisory/", StringComparison.Ordinal)) return false;
            var fileName = Path.GetFileNameWithoutExtension(relPath); // 北帝国_1089
            if (string.IsNullOrEmpty(fileName)) return false;
            var kingdomName = KingdomNameFromFile(fileName);

            var hero = EntityManager.GetEntityById(_agentEntityId.Value ?? "")?.HeroRef;
            var kingdom = hero?.MapFaction as Kingdom;
            // 只有本国国王可读密陈——连本国的封臣也不可
            return kingdom != null
                && kingdom.RulingClan?.Leader == hero
                && kingdomName != null
                && string.Equals(kingdom.Name?.ToString(), kingdomName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 外交问询线程（World/diplomacy/consults/{X}_and_{Y}.txt）的读取授权：
        /// 史官可读任何国家的公开外交问询；参与双方国王可读自己的线程；第三方不可读。
        /// </summary>
        private static bool IsConsultAllowed(string relPath)
        {
            if (!relPath.StartsWith("diplomacy/consults/", StringComparison.Ordinal)) return false;
            if (_agentEntityId.Value == "__historian__")
                return true; // 史官：任何国家的公开外交问询

            var fileName = Path.GetFileNameWithoutExtension(relPath); // {X}_and_{Y}
            if (string.IsNullOrEmpty(fileName)) return false;

            var hero = EntityManager.GetEntityById(_agentEntityId.Value ?? "")?.HeroRef;
            var kingdom = hero?.MapFaction as Kingdom;
            if (kingdom == null || kingdom.RulingClan?.Leader != hero) return false; // 仅国王可读

            var ownName = kingdom.Name?.ToString();
            if (string.IsNullOrEmpty(ownName)) return false;

            var sep = "_and_";
            var idx = fileName.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0) return false;
            var nameA = fileName.Substring(0, idx);
            var nameB = fileName.Substring(idx + sep.Length);
            return string.Equals(ownName, nameA, StringComparison.Ordinal)
                || string.Equals(ownName, nameB, StringComparison.Ordinal);
        }

        /// <summary>
        /// 私有密使线程（World/correspondence/{idA}_and_{idB}.txt）的读取授权：
        /// 仅参与双方可读（实体 ID 匹配）；史官（__historian__）与其他任何第三方均不可读。
        /// 文件名中的实体 ID 均为 SanitizeDir 后的安全形式，与 _agentEntityId.Value 可直接比较。
        /// </summary>
        private static bool IsCorrespondenceAllowed(string relPath)
        {
            if (!relPath.StartsWith("correspondence/", StringComparison.Ordinal)) return false;
            var fileName = Path.GetFileNameWithoutExtension(relPath); // {idA}_and_{idB}
            if (string.IsNullOrEmpty(fileName)) return false;

            var sep = "_and_";
            var idx = fileName.IndexOf(sep, StringComparison.Ordinal);
            if (idx <= 0) return false;
            var idA = fileName.Substring(0, idx);
            var idB = fileName.Substring(idx + sep.Length);
            if (idA.Length == 0 || idB.Length == 0) return false;

            var self = _agentEntityId.Value;
            if (string.IsNullOrEmpty(self)) return false;
            return string.Equals(self, idA, StringComparison.Ordinal)
                || string.Equals(self, idB, StringComparison.Ordinal);
        }

        private static string SanitizeDir(string name)
        {
            foreach (var c in Path.GetInvalidPathChars())
                name = name.Replace(c, '_');
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string SanitizeFile(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
