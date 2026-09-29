using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Dafeiyv
{
    /// <summary>
    /// 长期记忆文档管理：读取 / 追加 / 生成提示词片段。
    /// 文档为纯文本（Memory.txt），每行一条记忆，用户可手动增删；
    /// AI 通过回复末尾的「【记忆】内容」行来新增记忆。
    /// </summary>
    public class MemoryStore
    {
        private const string MemoryMarker = "【记忆】";
        private const string CommentPrefix = "#";   // 文档中以 # 开头的行是注释，不算记忆
        private const string OldHeader = "（长期记忆文档：每行一条，AI 会在对话中自动追加，你也可以手动增删）";   // 旧版生成的说明行，自动忽略

        // 新文档自带的说明（# 注释行，只给人看，不会发给 AI）
        private static readonly string HeaderText =
            "# 长期记忆文档：每行一条，AI 会在对话中自动追加，你也可以手动增删" + Environment.NewLine +
            "# 以 # 开头的行是注释，不会发给 AI" + Environment.NewLine;

        public string FilePath { get; }

        public MemoryStore(string filePath)
        {
            FilePath = filePath;
        }

        /// <summary>读取全部记忆（非空行、非注释行，去除首尾空白）</summary>
        public List<string> LoadEntries()
        {
            if (!File.Exists(FilePath))
                return new List<string>();
            return File.ReadAllLines(FilePath)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0 && !l.StartsWith(CommentPrefix) && l != OldHeader)
                .ToList();
        }

        /// <summary>构建随提示词发送的长期记忆片段（含写入指令）</summary>
        public string BuildPromptBlock(int maxChars)
        {
            var entries = LoadEntries();
            string instruction =
                $"长期记忆（只有对话中出现【重要且长期有用】的信息才值得记录，如:用户的喜好、习惯、重要个人信息、长期目标、重要约定;会频繁变化的小事，不要记录，少记不乱记。需要记录时，请在回复末尾另起一行以「{MemoryMarker}」开头输出，每条不超过{maxChars}字，要简洁完整、不要重复已有内容；没有重要信息就不要输出）:";
            if (entries.Count == 0)
                return instruction + "\n（目前还没有任何长期记忆）";
            return instruction + "\n" + string.Join("\n", entries.Select(e => "- " + e));
        }

        /// <summary>从 AI 回复中提取「【记忆】」行并追加到文档，返回新增条数</summary>
        public int AppendFromReply(string reply, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(reply)) return 0;

            var existing = new HashSet<string>(LoadEntries(), StringComparer.OrdinalIgnoreCase);
            var toAdd = new List<string>();

            foreach (var line in reply.Split('\n'))
            {
                string t = line.Trim();
                if (!t.StartsWith(MemoryMarker)) continue;

                string entry = t.Substring(MemoryMarker.Length).Trim();
                if (entry.Length > maxChars)
                {
                    entry = entry.Substring(0, maxChars);
                    // 避免把 emoji 之类的代理对截成两半产生乱码
                    if (entry.Length > 0 && char.IsHighSurrogate(entry[^1]))
                        entry = entry.Substring(0, entry.Length - 1);
                }
                if (entry.Length == 0) continue;
                if (existing.Add(entry))
                    toAdd.Add(entry);
            }

            if (toAdd.Count == 0) return 0;

            EnsureFileExists();
            File.AppendAllLines(FilePath, toAdd);
            return toAdd.Count;
        }

        /// <summary>文档不存在时创建，并写入说明注释（无论哪种方式创建都会带上）</summary>
        private void EnsureFileExists()
        {
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, HeaderText);
        }

        /// <summary>把回复里的「【记忆】」行去掉，只留正常内容</summary>
        public static string StripMemoryLines(string reply)
        {
            if (string.IsNullOrWhiteSpace(reply)) return reply ?? "";
            var kept = reply.Split('\n')
                .Where(l => !l.TrimStart().StartsWith(MemoryMarker))
                .ToList();
            return string.Join('\n', kept).Trim();
        }

        /// <summary>创建文档（若不存在）并用默认编辑器打开，方便用户手动增删</summary>
        public void OpenInEditor()
        {
            EnsureFileExists();
            Process.Start(new ProcessStartInfo(FilePath) { UseShellExecute = true });
        }
    }
}
