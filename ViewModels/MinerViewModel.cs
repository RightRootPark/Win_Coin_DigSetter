using System.IO;
using System.Text.RegularExpressions;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EncryptionMinerControl.Models;
using EncryptionMinerControl.Services;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Windows.Threading;

namespace EncryptionMinerControl.ViewModels;

public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

    public void Execute(object? parameter) => _execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}

public class MinerViewModel : INotifyPropertyChanged
{
    private readonly ProcessManager _processManager;
    private string _status = "Stopped";
    private string _copyButtonText = "COPY";
    private string _healthStatus = "Idle";
    private DateTime? _miningStartedAt;
    private DateTime? _lastAcceptedAt;
    private DateTime? _lastCrashAt;
    private int _consecutiveConnectionErrors;
    private bool _healthAlertRaised;
    private bool _isUpdatingArgsFromOption;
    private const long MaxXmrigLogBytes = 10L * 1024 * 1024; // 10 MB (bytes)

    public event Action<string, string>? AlertRaised;
    
    // [Korea] UI 최적화: 로그 버퍼링 (ConcurrentQueue & Timer)
    // 1. Process -> Queue (빠름)
    // 2. Queue -> UI (0.2초마다 한 번씩 렌더링)
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private readonly DispatcherTimer _logUpdateTimer;
    private readonly DispatcherTimer _copyButtonResetTimer;

    // UI log buffer (Observable for UI binding)
    public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();
    public ObservableCollection<MinerOptionItem> Options { get; } = new ObservableCollection<MinerOptionItem>();

    public MinerConfig Config { get; }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string HealthStatus
    {
        get => _healthStatus;
        private set
        {
            if (_healthStatus != value)
            {
                _healthStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasHealthWarning));
            }
        }
    }

    public bool HasHealthWarning => HealthStatus is "Pool Error" or "No Share" or "Crashed";

    // Prevent tight crash-restart loops / 크래시 직후 무한 재시작 방지를 위한 30초 쿨다운
    public bool CanAutoRestart => _lastCrashAt == null || (DateTime.UtcNow - _lastCrashAt.Value) >= TimeSpan.FromSeconds(30);

    public string CopyButtonText
    {
        get => _copyButtonText;
        set { _copyButtonText = value; OnPropertyChanged(); }
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CopyLogsCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand OpenLogFolderCommand { get; }

    public MinerViewModel(MinerConfig config)
    {
        Config = config;
        _processManager = new ProcessManager(OnLogReceived);
        _processManager.ProcessExited += OnProcessUnexpectedExit;

        // [Korea] Log Throttling Timer (5 FPS)
        _logUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200) // 0.2초
        };
        _logUpdateTimer.Tick += ProcessLogBuffer;
        _logUpdateTimer.Start();

        _copyButtonResetTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _copyButtonResetTimer.Tick += (_, _) =>
        {
            _copyButtonResetTimer.Stop();
            CopyButtonText = "COPY";
        };

        StartCommand = new RelayCommand(_ => Start(), _ => !_processManager.IsRunning && Config.Enabled);
        StopCommand = new RelayCommand(_ => Stop(), _ => _processManager.IsRunning);
        CopyLogsCommand = new RelayCommand(_ => CopyLogs());
        ClearLogsCommand = new RelayCommand(_ => Logs.Clear());
        OpenLogFolderCommand = new RelayCommand(_ => OpenLogFolder());

        InitializeOptions();
        SyncOptionsFromArguments();

        // Sync option checkboxes when ExtraArguments is edited directly / ExtraArguments 직접 수정 시 옵션 목록 동기화
        Config.PropertyChanged += (_, e) =>
        {
            if (!_isUpdatingArgsFromOption && e.PropertyName == nameof(MinerConfig.ExtraArguments))
            {
                SyncOptionsFromArguments();
            }
        };
    }

    private void InitializeOptions()
    {
        IEnumerable<MinerOptionItem> defs = Config.Type == MinerType.XMRig
            ? CreateXmrigOptionDefs()
            : CreateRigelOptionDefs();

        foreach (var opt in defs)
        {
            opt.OptionChanged += OnOptionItemChanged;
            Options.Add(opt);
        }
    }

    private void OnOptionItemChanged(MinerOptionItem item)
    {
        _isUpdatingArgsFromOption = true;
        try
        {
            Config.ExtraArguments = item.ApplyToArguments(Config.ExtraArguments);
        }
        finally
        {
            _isUpdatingArgsFromOption = false;
        }
    }

    private void SyncOptionsFromArguments()
    {
        foreach (var opt in Options)
        {
            opt.SyncFromArguments(Config.ExtraArguments);
        }
    }

    private static MinerOptionItem MakeValueOption(
        string label,
        string description,
        MinerOptionKind kind,
        string flagToken,
        string flagRegex,
        string defaultValue,
        string placeholder)
    {
        return new MinerOptionItem
        {
            Label = label,
            Description = description,
            Kind = kind,
            FlagToken = flagToken,
            ParsePattern = $@"(?:^|\s){flagRegex}(?:=|\s+)(?:""([^""]+)""|(\S+))",
            RemovePattern = $@"(?:^|\s){flagRegex}(?:=|\s+)(?:""[^""]+""|\S+)",
            DefaultValue = defaultValue,
            Placeholder = placeholder
        };
    }

    private static MinerOptionItem MakeBoolOption(string label, string description, string flagToken, string? customPattern = null)
    {
        string pattern = customPattern ?? $@"(?:^|\s){Regex.Escape(flagToken)}(?=\s|$)";
        return new MinerOptionItem
        {
            Label = label,
            Description = description,
            Kind = MinerOptionKind.BoolFlag,
            FlagToken = flagToken,
            ParsePattern = pattern,
            RemovePattern = pattern
        };
    }

    private static IEnumerable<MinerOptionItem> CreateXmrigOptionDefs()
    {
        return
        [
            MakeValueOption("Pool Password (-p)", "풀 접속 비밀번호 (unMineable 기본: x)", MinerOptionKind.SpaceValue, "-p", @"(?:-p|--pass)", "x", "x"),
            MakeValueOption("CPU Max Threads %", "최대 CPU 사용 비중 백분율 (예: 30, 50, 75)", MinerOptionKind.EqualsValue, "--cpu-max-threads-hint", @"--cpu-max-threads-hint", "30", "30 ~ 75"),
            MakeValueOption("CPU Priority (0~5)", "프로세스 우선순위 (0=유휴, 2=보통, 5=최고)", MinerOptionKind.SpaceValue, "--cpu-priority", @"--cpu-priority", "0", "0 ~ 5"),
            MakeValueOption("RandomX Mode", "RandomX 메모리 모드 (fast=8GB+ 권장, light=4GB 저사양, auto)", MinerOptionKind.EqualsValue, "--randomx-mode", @"--randomx-mode", "fast", "fast / light / auto"),
            MakeBoolOption("Huge Pages JIT", "RandomX JIT 코드에 대용량 페이지 사용 (--huge-pages-jit)", "--huge-pages-jit"),
            new MinerOptionItem
            {
                Label = "Enable MSR Mod (Admin)",
                Description = "CPU MSR 최적화 활성화 (관리자 권한 필요, 해시 +10~15%)",
                Kind = MinerOptionKind.PairFlag,
                FlagToken = "--randomx-wrmsr --randomx-rdmsr",
                ParsePattern = @"(?:^|\s)--randomx-(?:wr|rd)msr(?=\s|=|$)",
                RemovePattern = @"(?:^|\s)--randomx-(?:wr|rd)msr(?:=\S+)?(?=\s|$)"
            },
            new MinerOptionItem
            {
                Label = "Keepalive (--keepalive)",
                Description = "풀 연결 유지용 keepalive 패킷 전송",
                Kind = MinerOptionKind.BoolFlag,
                FlagToken = "--keepalive=true",
                ParsePattern = @"(?:^|\s)(?:--keepalive(?:=true)?|-k)(?=\s|$)",
                RemovePattern = @"(?:^|\s)(?:--keepalive(?:=(?:true|false))?|-k)(?=\s|$)"
            },
            MakeBoolOption("Pause on Battery", "배터리 전원 사용 시 채굴 일시정지 (--pause-on-battery)", "--pause-on-battery"),
            MakeBoolOption("RandomX No NUMA", "RandomX NUMA 지원 비활성화 (--randomx-no-numa)", "--randomx-no-numa"),
            MakeValueOption("Print Stats Interval (s)", "해시레이트 로그 출력 주기(초)", MinerOptionKind.EqualsValue, "--print-time", @"--print-time", "600", "60 ~ 600"),
            MakeValueOption("Donate Level (%)", "마이너 개발자 기부 비율(%)", MinerOptionKind.EqualsValue, "--donate-level", @"--donate-level", "1", "1"),
            MakeValueOption("Log File (--log-file)", "XMRig 로그 저장 파일 경로 (Miners 폴더 기준)", MinerOptionKind.EqualsValue, "--log-file", @"(?:--log-file|-l)", "Loging.txt", "Loging.txt")
        ];
    }

    private static IEnumerable<MinerOptionItem> CreateRigelOptionDefs()
    {
        return
        [
            MakeValueOption("Temp Limit (--temp-limit)", "코어/메모리 온도 상하한 (단일: tc[80-90]tm[105-115], 멀티GPU: 쉼표 구분)", MinerOptionKind.SpaceValue, "--temp-limit", @"--temp-limit", "tc[80-90]tm[105-115]", "tc[80-90]tm[105-115]"),
            MakeValueOption("Fan Control (Admin)", "팬 속도/자동 온도 제어 (관리자 필요, 멀티GPU 쉼표 구분 예: 100,80)", MinerOptionKind.SpaceValue, "--fan-control", @"--fan-control", "100", "100 또는 t:[65;_][50;45-75]"),
            MakeValueOption("NexaPow Small LUT", "3~4GB GPU 필수 (단일: on / 멀티GPU 이기종 예: on,off)", MinerOptionKind.SpaceValue, "--nexapow-small-lut", @"--nexapow-small-lut", "on", "on 또는 멀티GPU: on,off"),
            MakeValueOption("Multi-GPU Arg Mode", "멀티 GPU 인자 개수 불일치 처리 (first: 첫 값 일괄 적용, match, last)", MinerOptionKind.SpaceValue, "--multi-device-arg-mode", @"--multi-device-arg-mode", "first", "first / match / last"),
            MakeValueOption("Power Limit W (Admin)", "GPU 소비전력 상한(Watt, 관리자 필요, 멀티GPU 쉼표 구분 예: 70,120)", MinerOptionKind.SpaceValue, "--pl", @"--pl", "70", "예: 70 또는 70,120"),
            MakeValueOption("Lock Core Clock MHz (Admin)", "GPU 코어 클럭 고정(MHz, 관리자 필요, 멀티GPU 쉼표 구분)", MinerOptionKind.SpaceValue, "--lock-cclock", @"--lock-cclock", "1500", "예: 1500"),
            MakeValueOption("Core Clock Offset (Admin)", "GPU 코어 클럭 오프셋(MHz, 관리자 필요, 멀티GPU 쉼표 구분)", MinerOptionKind.SpaceValue, "--cclock", @"--cclock", "0", "예: +100"),
            MakeValueOption("Mem Clock Offset (Admin)", "GPU 메모리 클럭 오프셋(MHz, 관리자 필요, 멀티GPU 쉼표 구분)", MinerOptionKind.SpaceValue, "--mclock", @"--mclock", "0", "예: +500"),
            MakeValueOption("GPU Devices (-d)", "채굴에 사용할 GPU 번호 (쉼표 구분)", MinerOptionKind.SpaceValue, "-d", @"(?:-d|--devices)", "0", "예: 0 또는 0,1"),
            MakeValueOption("Stats Interval (s)", "GPU 상태 및 해시 출력 주기(초)", MinerOptionKind.SpaceValue, "--stats-interval", @"--stats-interval", "600", "60 ~ 600"),
            MakeValueOption("Log File (--log-file)", "Rigel 로그 저장 파일명 (5MB 자동 로테이션)", MinerOptionKind.QuotedSpaceValue, "--log-file", @"(?:--log-file|-l)", "rigel_1060_nexapow_log.txt", "rigel_1060_nexapow_log.txt"),
            MakeBoolOption("Disable Strict SSL", "SSL 인증서 엄격 검증 비활성화 (--no-strict-ssl)", "--no-strict-ssl"),
            MakeBoolOption("No TUI (--no-tui)", "터미널 TUI 비활성화 (GUI 로그 캡처용 권장)", "--no-tui"),
            MakeBoolOption("No Colour (--no-colour)", "ANSI 색상 코드 출력 비활성화 (--no-colour)", "--no-colour"),
            MakeBoolOption("CPU Share Check", "풀 제출 전 CPU에서 share 사전 검증 (--cpu-check)", "--cpu-check"),
            MakeBoolOption("Disable Watchdog", "Rigel 내장 워치독 프로세스 비활성화 (--no-watchdog)", "--no-watchdog")
        ];
    }

    // Copy all buffered miner logs to clipboard / 클립보드로 현재 로그 전체 복사
    private void CopyLogs()
    {
        if (Logs.Count == 0) return;

        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, Logs));
            CopyButtonText = "COPIED!";
            _copyButtonResetTimer.Stop();
            _copyButtonResetTimer.Start();
        }
        catch (Exception ex)
        {
            _logQueue.Enqueue($"[Warning] Failed to copy logs: {ex.Message}");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            string? dir = Path.GetDirectoryName(Config.ExecutablePath);
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
            {
                dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Miners");
            }
            if (Directory.Exists(dir))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", dir)
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            _logQueue.Enqueue($"[Warning] Failed to open log folder: {ex.Message}");
        }
    }

    private void Start()
    {
        Status = "Running";
        _miningStartedAt = DateTime.UtcNow;
        _lastAcceptedAt = null;
        _lastCrashAt = null;
        _consecutiveConnectionErrors = 0;
        _healthAlertRaised = false;
        HealthStatus = "Starting";

        string args = BuildArguments();
        if (Config.Type == MinerType.XMRig)
        {
            RotateXmrigLogIfNeeded(args);
        }
        _processManager.Start(Config.ExecutablePath, args);
    }

    // XMRig has no built-in log rotation / XMRig 자체 로테이션 미지원으로 10MB 초과 시 .1 백업 교체
    private void RotateXmrigLogIfNeeded(string arguments)
    {
        try
        {
            var match = Regex.Match(arguments, @"(?:^|\s)(?:--log-file|-l)(?:=|\s+)(?:""([^""]+)""|(\S+))", RegexOptions.IgnoreCase);
            if (!match.Success) return;

            string logFile = !string.IsNullOrEmpty(match.Groups[1].Value)
                ? match.Groups[1].Value
                : match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(logFile)) return;

            string baseDir = Path.GetDirectoryName(Config.ExecutablePath) ?? string.Empty;
            string logPath = Path.IsPathRooted(logFile) ? logFile : Path.Combine(baseDir, logFile);

            var fileInfo = new FileInfo(logPath);
            if (!fileInfo.Exists || fileInfo.Length <= MaxXmrigLogBytes) return;

            double sizeMb = fileInfo.Length / (1024.0 * 1024.0);
            string backupPath = logPath + ".1";
            File.Move(logPath, backupPath, overwrite: true);
            _logQueue.Enqueue($"[System] Rotated XMRig log ({sizeMb:F1} MB -> {Path.GetFileName(backupPath)})");
        }
        catch (Exception ex)
        {
            _logQueue.Enqueue($"[Warning] Failed to rotate XMRig log: {ex.Message}");
        }
    }

    private void Stop()
    {
        _processManager.Stop();
        
        // [Korea] 1. Cleanup Zombies
        // 프로세스 매니저가 닫았더라도, 혹시 모를 좀비 프로세스를 위해 이름으로 한 번 더 확인 사살
        try
        {
            string exeName = System.IO.Path.GetFileNameWithoutExtension(Config.ExecutablePath);
            string dirPath = System.IO.Path.GetDirectoryName(Config.ExecutablePath) ?? "";
            
            // 유효한 경로일 때만 수행
            if (!string.IsNullOrEmpty(exeName) && !string.IsNullOrEmpty(dirPath))
            {
                ProcessManager.KillProcessByPath(exeName, dirPath);
            }
        }
        catch { /* Ignore */ }

        _miningStartedAt = null;
        _lastAcceptedAt = null;
        _consecutiveConnectionErrors = 0;
        _healthAlertRaised = false;
        HealthStatus = "Idle";
        Status = "Stopped";
    }

    private string BuildArguments()
    {
        // Basic argument builder - to be improved with specific logic per miner
        string args = Config.ExtraArguments;
        
        if (Config.Type == MinerType.XMRig) 
        {
            if (!string.IsNullOrEmpty(Config.PoolUrl)) args += $" -o {Config.PoolUrl}";
            if (!string.IsNullOrEmpty(Config.WalletAddress)) args += $" -u {Config.WalletAddress}";
            if (!string.IsNullOrEmpty(Config.Algorithm)) args += $" -a {Config.Algorithm}"; // XMRig usually auto-detects, but option is good
        }
        else if (Config.Type == MinerType.Rigel)
        {
             // Rigel syntax: -a [algo] -o [stratum] -u [wallet]
            if (!string.IsNullOrEmpty(Config.Algorithm)) args += $" -a {Config.Algorithm}";
            if (!string.IsNullOrEmpty(Config.PoolUrl)) args += $" -o {Config.PoolUrl}";
            if (!string.IsNullOrEmpty(Config.WalletAddress)) args += $" -u {Config.WalletAddress}";
        }
        
        return args;
    }

    private void OnProcessUnexpectedExit(int exitCode)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;

        app.Dispatcher.BeginInvoke(() =>
        {
            Status = "Stopped";
            HealthStatus = "Crashed";
            _lastCrashAt = DateTime.UtcNow;
            CommandManager.InvalidateRequerySuggested();
            AlertRaised?.Invoke($"{Config.Name} Crashed", $"ExitCode {exitCode}로 비정상 종료됨. 30초 후 자동 복구를 시도함.");
        });
    }

    private void OnLogReceived(string message)
    {
        // 최적화: 즉시 UI 갱신하지 않고 큐에 적재 (매우 빠름)
        _logQueue.Enqueue(message);
    }

    private void ProcessLogBuffer(object? sender, EventArgs e)
    {
        if (_logQueue.IsEmpty) return;

        // 한 번에 최대 50줄까지만 처리 (UI 멈춤 방지)
        int processedCount = 0;
        
        while (!_logQueue.IsEmpty && processedCount < 50)
        {
            if (_logQueue.TryDequeue(out string? log) && log != null)
            {
                if (Logs.Count > 1000) Logs.RemoveAt(0);
                Logs.Add(log);
                InspectLogLine(log);
                processedCount++;
            }
        }
    }

    private void InspectLogLine(string line)
    {
        if (line.Contains("accepted", StringComparison.OrdinalIgnoreCase))
        {
            _lastAcceptedAt = DateTime.UtcNow;
            _consecutiveConnectionErrors = 0;
            _healthAlertRaised = false;
            if (Status == "Running") HealthStatus = "Healthy";
            return;
        }

        if (line.Contains("connection error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("connect error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("read error", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("DNS error", StringComparison.OrdinalIgnoreCase))
        {
            _consecutiveConnectionErrors++;
            if (_consecutiveConnectionErrors >= 10)
            {
                HealthStatus = "Pool Error";
                if (!_healthAlertRaised)
                {
                    _healthAlertRaised = true;
                    AlertRaised?.Invoke($"{Config.Name} Pool Error", "풀 연결 오류가 10회 이상 반복되고 있음. 풀/알고리즘 상태를 확인하세요.");
                }
            }
        }
    }

    public void EvaluateHealth()
    {
        if (Status != "Running" || HealthStatus == "Pool Error") return;

        DateTime? baseline = _lastAcceptedAt ?? _miningStartedAt;
        if (baseline != null && (DateTime.UtcNow - baseline.Value) >= TimeSpan.FromMinutes(30))
        {
            HealthStatus = "No Share";
            if (!_healthAlertRaised)
            {
                _healthAlertRaised = true;
                AlertRaised?.Invoke($"{Config.Name} No Share", "30분 이상 Accepted Share가 없음. 풀 상태를 확인하세요.");
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? platformName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(platformName));
    }
}
