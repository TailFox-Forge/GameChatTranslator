using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class LogViewerBufferTests
    {
        [Fact]
        public void ReadRecentLines_ReturnsOnlyTailLines()
        {
            string directory = Path.Combine(Path.GetTempPath(), "gct-log-buffer-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            string logPath = Path.Combine(directory, "session.log");
            File.WriteAllText(logPath, "one\ntwo\nthree\nfour\nfive\n", Encoding.UTF8);

            try
            {
                IReadOnlyList<string> lines = LogViewerBuffer.ReadRecentLines(logPath, 3, Encoding.UTF8);

                Assert.Equal(new[] { "three", "four", "five" }, lines);
                Assert.Equal($"three{Environment.NewLine}four{Environment.NewLine}five{Environment.NewLine}", LogViewerBuffer.ToDisplayText(lines));
            }
            finally
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Fact]
        public void AppendEntryLines_TrimsOldLinesWhenLimitExceeded()
        {
            Queue<string> lines = new Queue<string>(new[] { "one", "two" });

            bool trimmed = LogViewerBuffer.AppendEntryLines(lines, "three\nfour\n", 3);

            Assert.True(trimmed);
            Assert.Equal(new[] { "two", "three", "four" }, lines.ToArray());
        }

        [Fact]
        public void AppendEntryLines_DoesNotAddTrailingEmptyLine()
        {
            Queue<string> lines = new Queue<string>();

            bool trimmed = LogViewerBuffer.AppendEntryLines(lines, "one\r\ntwo\r\n", 10);

            Assert.False(trimmed);
            Assert.Equal(new[] { "one", "two" }, lines.ToArray());
        }
    }
}
