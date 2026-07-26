using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GameTranslator
{
    /// <summary>
    /// 현재 세션 로그 파일을 별도 창에서 실시간으로 보여주는 창입니다.
    /// 기존 로그는 최근 줄만 파일에서 읽고, 새 로그는 MainWindow.AppendLog에서 직접 전달받습니다.
    /// </summary>
    public partial class LogViewerWindow : Window
    {
        private const int MaxDisplayLogLines = LogViewerBuffer.DefaultMaxDisplayLines;
        private readonly DispatcherTimer resourceTimer;
        private readonly string logFilePath;
        private readonly Queue<string> displayLines = new Queue<string>(MaxDisplayLogLines);
        private bool waitingMessageShown;
        private bool allowClose;
        private TimeSpan lastCpuTime;
        private DateTime lastCpuSampleAt;

        /// <summary>
        /// 로그 뷰어 창을 생성합니다.
        /// <paramref name="logFilePath"/>는 MainWindow가 이번 실행에서 사용하는 logs/log_*.txt 파일 경로입니다.
        /// </summary>
        public LogViewerWindow(string logFilePath)
        {
            InitializeComponent();

            this.logFilePath = logFilePath;
            resourceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            resourceTimer.Tick += (s, e) => UpdateResourceUsage();

            Loaded += (s, e) =>
            {
                ReloadFromStart();
                ResetResourceUsageBaseline();
                UpdateResourceUsage();
                resourceTimer.Start();
            };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible)
                {
                    ResetResourceUsageBaseline();
                    UpdateResourceUsage();
                    resourceTimer.Start();
                }
                else
                {
                    resourceTimer.Stop();
                }
            };
        }

        /// <summary>
        /// 창 닫기 버튼을 눌렀을 때 실제로 닫지 않고 숨깁니다.
        /// 다시 Ctrl+= 또는 환경설정창 버튼으로 열면 기존 위치와 크기를 유지합니다.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (allowClose)
            {
                resourceTimer.Stop();
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// 메인 프로그램 종료 시 로그창을 실제로 닫습니다.
        /// 일반 사용자의 X 버튼은 숨김이지만, 애플리케이션 종료 중에는 창을 남기지 않아야 합니다.
        /// </summary>
        public void CloseForShutdown()
        {
            allowClose = true;
            Close();
        }

        /// <summary>
        /// 로그 파일의 최근 줄만 다시 읽어 화면에 표시합니다.
        /// 실제 로그 파일은 수정하지 않고 표시 내용과 읽기 위치만 갱신합니다.
        /// </summary>
        private void ReloadFromStart()
        {
            ClearDisplayBuffer();
            waitingMessageShown = false;
            SetLogStatus();

            if (!File.Exists(logFilePath))
            {
                TxtLog.Text = "로그 파일 생성 대기 중...";
                waitingMessageShown = true;
                return;
            }

            try
            {
                foreach (string line in LogViewerBuffer.ReadRecentLines(logFilePath, MaxDisplayLogLines, Encoding.UTF8))
                {
                    displayLines.Enqueue(line);
                }

                RenderDisplayBuffer();
                waitingMessageShown = false;
                ScrollToEndIfNeeded();
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"로그 읽기 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// MainWindow.AppendLog에서 새 로그 한 줄을 직접 전달받아 화면에 즉시 붙입니다.
        /// <paramref name="logEntry"/>는 파일에 저장한 것과 동일한 완성된 로그 문자열입니다.
        /// </summary>
        public void AppendLogEntry(string logEntry)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLogEntry(logEntry)));
                return;
            }

            if (waitingMessageShown)
            {
                ClearDisplayBuffer();
                waitingMessageShown = false;
            }

            SetLogStatus();
            bool trimmed = LogViewerBuffer.AppendEntryLines(displayLines, logEntry, MaxDisplayLogLines);
            if (trimmed)
            {
                RenderDisplayBuffer();
            }
            else
            {
                TxtLog.AppendText(logEntry);
            }
            ScrollToEndIfNeeded();
        }

        /// <summary>
        /// MainWindow가 집계한 OCR 모드별 평균 처리 시간을 로그창 상단에 표시합니다.
        /// <paramref name="summary"/>는 빠름/자동/정확 모드별 평균 Total/OCR/Translate 시간을 담은 문자열입니다.
        /// </summary>
        public void UpdateOcrPerformanceSummary(string summary)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => UpdateOcrPerformanceSummary(summary)));
                return;
            }

            TxtOcrPerformanceSummary.Text = string.IsNullOrWhiteSpace(summary)
                ? "OCR 평균: 아직 번역 성능 기록 없음"
                : summary;
        }

        /// <summary>
        /// [새로고침] 버튼 클릭 시 로그 파일 전체를 다시 읽습니다.
        /// </summary>
        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            ReloadFromStart();
        }

        /// <summary>
        /// [화면 지우기] 버튼 클릭 시 TextBox만 비우고 실제 로그 파일은 보존합니다.
        /// 이후 새로 추가되는 로그부터 다시 화면에 표시됩니다.
        /// </summary>
        private void BtnClearView_Click(object sender, RoutedEventArgs e)
        {
            ClearDisplayBuffer();
            waitingMessageShown = false;
        }

        /// <summary>
        /// 로그창 UI TextBox와 내부 최근 줄 버퍼를 함께 비웁니다.
        /// </summary>
        private void ClearDisplayBuffer()
        {
            displayLines.Clear();
            TxtLog.Clear();
        }

        /// <summary>
        /// 내부 최근 줄 버퍼를 TextBox에 다시 반영합니다.
        /// 오래된 줄이 제거된 경우에만 전체 문자열을 재구성합니다.
        /// </summary>
        private void RenderDisplayBuffer()
        {
            TxtLog.Text = LogViewerBuffer.ToDisplayText(displayLines);
        }

        /// <summary>
        /// 로그 파일 위치와 UI 표시 상한을 상태 영역에 보여줍니다.
        /// </summary>
        private void SetLogStatus()
        {
            TxtStatus.Text = $"{logFilePath} · 최근 최대 {MaxDisplayLogLines:N0}줄 표시";
        }

        /// <summary>
        /// 자동 스크롤이 켜져 있으면 로그 끝으로 이동합니다.
        /// </summary>
        private void ScrollToEndIfNeeded()
        {
            if (CheckAutoScroll.IsChecked == true)
            {
                TxtLog.ScrollToEnd();
            }
        }

        /// <summary>
        /// CPU 사용률 계산을 위한 기준 시각과 누적 CPU 시간을 갱신합니다.
        /// </summary>
        private void ResetResourceUsageBaseline()
        {
            using Process process = Process.GetCurrentProcess();
            process.Refresh();
            lastCpuTime = process.TotalProcessorTime;
            lastCpuSampleAt = DateTime.UtcNow;
        }

        /// <summary>
        /// 현재 GameChatTranslator 프로세스의 CPU 사용률과 메모리 사용량을 상단에 표시합니다.
        /// CPU는 전체 논리 프로세서 기준 백분율, 메모리는 Working Set 기준 MB입니다.
        /// </summary>
        private void UpdateResourceUsage()
        {
            try
            {
                using Process process = Process.GetCurrentProcess();
                process.Refresh();

                DateTime now = DateTime.UtcNow;
                double elapsedMs = (now - lastCpuSampleAt).TotalMilliseconds;
                double cpuMs = (process.TotalProcessorTime - lastCpuTime).TotalMilliseconds;
                double cpuPercent = 0;
                if (elapsedMs > 0)
                {
                    cpuPercent = cpuMs / elapsedMs / Environment.ProcessorCount * 100.0;
                }

                double memoryMb = process.WorkingSet64 / 1024.0 / 1024.0;
                TxtResourceUsage.Text = $"CPU {cpuPercent:0.0}% / MEM {memoryMb:0} MB";

                lastCpuTime = process.TotalProcessorTime;
                lastCpuSampleAt = now;
            }
            catch (Exception ex)
            {
                TxtResourceUsage.Text = $"리소스 확인 실패: {ex.Message}";
            }
        }
    }
}
