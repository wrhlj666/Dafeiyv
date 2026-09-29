using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace Dafeiyv
{
    /// <summary>DSH 当前状态（对应 dsh-dafeiyu 插件协议里的 CompanionState）</summary>
    public enum DshState
    {
        Unknown,
        Idle,
        Thinking,
        Working,
        Waiting,
        Success,
        Error,
        Disconnected,
    }

    /// <summary>一次 DSH 状态快照</summary>
    public sealed class DshStatus
    {
        public DshState State { get; init; } = DshState.Unknown;
        public string Message { get; init; } = "";
        public string Detail { get; init; } = "";
        public string Project { get; init; } = "";
        public string Task { get; init; } = "";
        public string ToolName { get; init; } = "";
        public string Activity { get; init; } = "";
        public int ProgressCompleted { get; init; } = -1;
        public int ProgressTotal { get; init; } = -1;

        /// <summary>气泡文案（优先 detail，其次 message / 工具名 / 阶段）</summary>
        public string BuildBubbleText()
        {
            string head = State switch
            {
                DshState.Thinking => "🤔 思考中",
                DshState.Working => "🔧 干活中",
                DshState.Waiting => "✋ 等你确认",
                DshState.Success => "🎉 完成啦",
                DshState.Error => "😢 出错了",
                DshState.Idle => "🐋 空闲中",
                _ => "🐋 DSH",
            };

            string body = !string.IsNullOrWhiteSpace(Detail) ? Detail
                : !string.IsNullOrWhiteSpace(Message) ? Message
                : !string.IsNullOrWhiteSpace(Activity) ? Activity
                : !string.IsNullOrWhiteSpace(ToolName) ? $"正在使用 {ToolName}"
                : "";

            // 进度（若插件提供了待办清单）
            string progress = "";
            if (ProgressTotal > 0 && ProgressCompleted >= 0)
                progress = $"（{ProgressCompleted}/{ProgressTotal}）";

            if (!string.IsNullOrWhiteSpace(Project))
                body = string.IsNullOrWhiteSpace(body) ? Project : $"{Project} · {body}";

            if (string.IsNullOrWhiteSpace(body)) return head + progress;
            return $"{head}{progress}\n{body}";
        }
    }

    /// <summary>
    /// 读取 DSH 侧写出的状态 JSONL（由 dsh-pet-status 插件产出），解析出真实工作状态供桌宠显示。
    ///
    /// 状态源是自带的 dsh-pet-status 插件（订阅 DSH 会话事件后写文件），
    /// 不依赖任何第三方桌宠插件。文件位置可用环境变量 DSH_PET_STATUS_LOG 覆盖。
    /// </summary>
    public sealed class DshCompanion : IDisposable
    {
        /// <summary>多久没收到新状态就认为 DSH 不在了（毫秒）</summary>
        private const int StaleMilliseconds = 90000;

        private readonly string _path;
        private readonly DispatcherTimer _pollTimer;
        private readonly Dispatcher _dispatcher;

        private long _offset;                 // 已消费到的字节位置
        private long _lastMessageTicks;        // 最后一条 state 消息的时间（本地时钟）
        private DshState _lastPublished = DshState.Unknown;
        private bool _disposed;

        /// <summary>状态变化时触发（可能在任何线程；订阅方自行切回 UI 线程）</summary>
        public event Action<DshStatus>? StatusChanged;

        /// <summary>最近一次解析到的状态（供读取当前值）</summary>
        public DshStatus Current { get; private set; } = new DshStatus();

        public bool Enabled { get; set; } = true;

        public string Path => _path;

        public DshCompanion(string? eventLogPath = null, Dispatcher? dispatcher = null)
        {
            _path = ResolvePath(eventLogPath);
            _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;

            _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
            _pollTimer.Tick += (_, _) => Poll();
        }

        /// <summary>默认状态文件位置：%LOCALAPPDATA%\dsh-pet-status\status.jsonl</summary>
        public static string ResolvePath(string? configured)
        {
            if (!string.IsNullOrWhiteSpace(configured)) return configured!.Trim();
            string env = Environment.GetEnvironmentVariable("DSH_PET_STATUS_LOG") ?? "";
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            string dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "dsh-pet-status");
            return System.IO.Path.Combine(dir, "status.jsonl");
        }

        public void Start()
        {
            if (_disposed) return;
            if (File.Exists(_path))
            {
                // 只回看尾部一段，既能立即反映当前状态，又不用扫全文件
                long length = new FileInfo(_path).Length;
                _offset = Math.Max(0, length - 8192);
            }
            _pollTimer.Start();
            Poll();
        }

        public void Stop() => _pollTimer.Stop();

        private void Poll()
        {
            if (!Enabled || _disposed) return;

            try
            {
                if (!File.Exists(_path))
                {
                    Publish(new DshStatus { State = DshState.Disconnected });
                    return;
                }

                using (var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < _offset) _offset = 0;      // 文件被轮转/截断
                    if (fs.Length > _offset)
                    {
                        fs.Seek(_offset, SeekOrigin.Begin);
                        using var reader = new StreamReader(fs, Encoding.UTF8);
                        string text = reader.ReadToEnd();

                        // 只处理完整行（末尾可能正在写入半行）
                        int lastNewline = text.LastIndexOf('\n');
                        if (lastNewline >= 0)
                        {
                            string complete = text.Substring(0, lastNewline);
                            foreach (string line in complete.Split('\n'))
                                ParseLine(line);
                            _offset += Encoding.UTF8.GetByteCount(text.Substring(0, lastNewline + 1));
                        }
                    }
                }

                // 超时未更新 → 认为 DSH 已退出
                if (_lastMessageTicks > 0)
                {
                    long idleMs = (DateTime.UtcNow.Ticks - _lastMessageTicks) / TimeSpan.TicksPerMillisecond;
                    if (idleMs > StaleMilliseconds)
                        Publish(new DshStatus { State = DshState.Disconnected });
                }
            }
            catch
            {
                // 读文件失败（占用/权限等）不打断桌宠，下次轮询再试
            }
        }

        private void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return;

                if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String) return;
                string kind = kindEl.GetString() ?? "";
                if (kind == "ping") return;                 // 心跳噪声
                if (kind != "state") return;                // 只关心状态消息

                var status = new DshStatus
                {
                    State = ParseState(GetString(root, "state")),
                    Message = GetString(root, "message"),
                    Detail = GetString(root, "detail"),
                    Project = GetString(root, "project"),
                    Task = GetString(root, "task"),
                    ToolName = GetString(root, "toolName"),
                    Activity = GetString(root, "activity"),
                };

                if (root.TryGetProperty("progress", out var progressEl) && progressEl.ValueKind == JsonValueKind.Object)
                {
                    if (progressEl.TryGetProperty("completed", out var cEl) && cEl.TryGetInt32(out int c)) status = WithProgress(status, c, status.ProgressTotal);
                    if (progressEl.TryGetProperty("total", out var tEl) && tEl.TryGetInt32(out int t)) status = WithProgress(status, status.ProgressCompleted, t);
                }

                _lastMessageTicks = DateTime.UtcNow.Ticks;
                Publish(status);
            }
            catch
            {
                // 半行/坏行忽略
            }
        }

        private static DshStatus WithProgress(DshStatus s, int completed, int total) => new DshStatus
        {
            State = s.State,
            Message = s.Message,
            Detail = s.Detail,
            Project = s.Project,
            Task = s.Task,
            ToolName = s.ToolName,
            Activity = s.Activity,
            ProgressCompleted = completed,
            ProgressTotal = total,
        };

        private static string GetString(JsonElement obj, string name)
        {
            if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "";
            return "";
        }

        private static DshState ParseState(string raw) => raw switch
        {
            "IDLE" => DshState.Idle,
            "THINKING" => DshState.Thinking,
            "WORKING" => DshState.Working,
            "WAITING" => DshState.Waiting,
            "SUCCESS" => DshState.Success,
            "ERROR" => DshState.Error,
            "DISCONNECTED" => DshState.Disconnected,
            _ => DshState.Unknown,
        };

        /// <summary>同状态不重复广播，避免无谓的动画重启</summary>
        private void Publish(DshStatus status)
        {
            Current = status;
            if (status.State == _lastPublished) return;
            _lastPublished = status.State;
            StatusChanged?.Invoke(status);
        }

        public void Dispose()
        {
            _disposed = true;
            _pollTimer.Stop();
            StatusChanged = null;
        }
    }
}
