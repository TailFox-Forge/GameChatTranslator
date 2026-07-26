using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GameTranslator
{
    /// <summary>
    /// 로그 뷰어 화면에 올릴 최근 로그 줄만 유지하는 순수 helper입니다.
    /// 실제 로그 파일은 전체를 보존하고, UI TextBox 메모리만 제한합니다.
    /// </summary>
    public static class LogViewerBuffer
    {
        public const int DefaultMaxDisplayLines = 2000;

        /// <summary>
        /// 로그 파일을 줄 단위로 읽으면서 마지막 N줄만 반환합니다.
        /// File.ReadAllText를 피해서 큰 세션 로그도 UI 메모리에 한 번에 올리지 않습니다.
        /// </summary>
        public static IReadOnlyList<string> ReadRecentLines(string filePath, int maxLines, Encoding encoding)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return Array.Empty<string>();

            int limit = NormalizeMaxLines(maxLines);
            Queue<string> lines = new Queue<string>(limit);
            using FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new StreamReader(stream, encoding ?? Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                AppendLine(lines, line, limit);
            }

            return lines.ToArray();
        }

        /// <summary>
        /// 새 로그 entry를 화면 버퍼에 추가하고 상한 초과분을 앞에서 제거합니다.
        /// </summary>
        public static bool AppendEntryLines(Queue<string> lines, string logEntry, int maxLines)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            if (string.IsNullOrEmpty(logEntry)) return false;

            int limit = NormalizeMaxLines(maxLines);
            int before = lines.Count;
            IReadOnlyList<string> entryLines = SplitEntryLines(logEntry);
            foreach (string line in entryLines)
            {
                AppendLine(lines, line, limit);
            }

            return lines.Count < before + entryLines.Count;
        }

        /// <summary>
        /// TextBox.Text에 넣을 문자열로 변환합니다.
        /// </summary>
        public static string ToDisplayText(IEnumerable<string> lines)
        {
            if (lines == null) return string.Empty;

            string text = string.Join(Environment.NewLine, lines);
            return text.Length == 0 ? string.Empty : text + Environment.NewLine;
        }

        internal static IReadOnlyList<string> SplitEntryLines(string logEntry)
        {
            if (string.IsNullOrEmpty(logEntry)) return Array.Empty<string>();

            string normalized = logEntry.Replace("\r\n", "\n").Replace('\r', '\n');
            string[] parts = normalized.Split('\n');
            int count = parts.Length;
            if (count > 0 && parts[count - 1].Length == 0) count -= 1;

            List<string> lines = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                lines.Add(parts[i]);
            }

            return lines;
        }

        private static int NormalizeMaxLines(int maxLines)
        {
            return maxLines > 0 ? maxLines : DefaultMaxDisplayLines;
        }

        private static void AppendLine(Queue<string> lines, string line, int maxLines)
        {
            lines.Enqueue(line ?? string.Empty);
            while (lines.Count > maxLines)
            {
                lines.Dequeue();
            }
        }
    }
}
