using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using System.Text;
using EncryptionMinerControl.Models;
using EncryptionMinerControl.Services;

namespace EncryptionMinerControl.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    // [Korea] 설정 파일 경로를 절대 경로로 지정하여 스케줄러 실행 시 경로 문제 해결
    private static string ConfigFile => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
    
    public MinerViewModel XmrigMiner { get; private set; }
    public MinerViewModel RigelMiner { get; private set; }

    public ICommand SaveConfigCommand { get; }
    public ICommand AutoConfigCommand { get; }
    public ICommand ResetConfigCommand { get; }

    private System.Windows.Threading.DispatcherTimer _idleTimer;
    private bool _isIdleMiningEnabled;

    public bool IsIdleMiningEnabled
    {
        get => _isIdleMiningEnabled;
        set
        {
             if (_isIdleMiningEnabled != value)
             {
                 _isIdleMiningEnabled = value;
                 OnPropertyChanged();
             }
        }
    }

    // Keep Awake Settings
    private bool _isKeepAwakeEnabled;
    public bool IsKeepAwakeEnabled
    {
        get => _isKeepAwakeEnabled;
        set { _isKeepAwakeEnabled = value; OnPropertyChanged(); }
    }

    private bool _isStartInTrayEnabled;
    public bool IsStartInTrayEnabled
    {
        get => _isStartInTrayEnabled;
        set { _isStartInTrayEnabled = value; OnPropertyChanged(); }
    }

    private bool _isStartInStealthEnabled;
    public bool IsStartInStealthEnabled
    {
        get => _isStartInStealthEnabled;
        set { _isStartInStealthEnabled = value; OnPropertyChanged(); }
    }

    private int _keepAwakeInterval = 60;
    public int KeepAwakeInterval
    {
        get => _keepAwakeInterval;
        set 
        {
            // Enforce minimum 5 seconds
            int val = value < 5 ? 5 : value;
            _keepAwakeInterval = val; 
            OnPropertyChanged();
            OnPropertyChanged(nameof(KeepAwakeProgress));
        }
    }

    // Admin & Elevated Startup / 관리자 권한 확인 및 작업 스케줄러 자동 시작
    public bool IsAdmin { get; } = AdminHelper.IsRunningAsAdmin();
    public bool IsNotAdmin => !IsAdmin;
    public string AdminStatusText => IsAdmin
        ? "Running as Administrator (MSR / GPU Fan Control Active)"
        : "Standard User (Restart as Admin for MSR & GPU Fan Control)";

    private bool _isAutoStartAdminEnabled;
    private bool _isAutoStartAdminTouched;
    public bool IsAutoStartAdminEnabled
    {
        get => _isAutoStartAdminEnabled;
        set
        {
            if (_isAutoStartAdminEnabled == value) return;
            _isAutoStartAdminTouched = true;
            var (success, message) = AdminHelper.SetStartupTask(value);
            _isAutoStartAdminEnabled = AdminHelper.IsStartupTaskRegistered();
            OnPropertyChanged();
            if (!success)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to update Windows Startup Task: {message}",
                    "Admin Startup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    public ICommand RestartAsAdminCommand { get; }

    // unMineable Balance Monitoring / unMineable 잔고 및 출금 진행률 조회
    private readonly System.Windows.Threading.DispatcherTimer _unmineableTimer;
    private int _healthTickCounter;
    private bool _isRefreshingUnmineable;
    private string _unmineableBalanceText = "Loading...";
    public string UnmineableBalanceText
    {
        get => _unmineableBalanceText;
        private set { _unmineableBalanceText = value; OnPropertyChanged(); }
    }

    private string _unmineableMetaText = "Checking unMineable API...";
    public string UnmineableMetaText
    {
        get => _unmineableMetaText;
        private set { _unmineableMetaText = value; OnPropertyChanged(); }
    }

    private double _unmineableProgress;
    public double UnmineableProgress
    {
        get => _unmineableProgress;
        private set { _unmineableProgress = value; OnPropertyChanged(); }
    }

    public ICommand RefreshUnmineableCommand { get; }

    // Configurable Idle Delay
    private int _idleMiningStartDelay = 60;
    public int IdleMiningStartDelay
    {
        get => _idleMiningStartDelay;
        set 
        { 
            _idleMiningStartDelay = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(IdleMiningStartDelayString));
            OnPropertyChanged(nameof(IdleProgress)); 
        }
    }

    public string IdleMiningStartDelayString
    {
        get => _idleMiningStartDelay.ToString();
        set
        {
            if (int.TryParse(value, out int result))
            {
                // Validate Min 5s
                if (result < 5) result = 5;
                IdleMiningStartDelay = result;
            }
            else
            {
                // Invalid input fallback
                IdleMiningStartDelay = 60;
            }
            OnPropertyChanged(); // Refresh UI if value was coerced
        }
    }

    // Stealth Mode
    private bool _isStealthMode;
    public bool IsStealthMode
    {
        get => _isStealthMode;
        set 
        { 
            _isStealthMode = value; 
            OnPropertyChanged(); 
            OnPropertyChanged(nameof(IsNotStealthMode)); 
        }
    }
    public bool IsNotStealthMode => !IsStealthMode;

    public ICommand EnterStealthModeCommand { get; }

    // Virtual Idle Logic State
    private double _lastSystemIdleTime;
    private double _virtualIdleAccumulator;
    private bool _justJiggled;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);
    
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    private double _currentIdleSeconds;
    public double CurrentIdleSeconds
    {
        get => _currentIdleSeconds;
        set { _currentIdleSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(IdleProgress)); }
    }

    public double IdleProgress => Math.Min((CurrentIdleSeconds / (double)IdleMiningStartDelay) * 100, 100);

    // Keep Awake Visualization
    private double _timeUntilNextJiggle;
    public double TimeUntilNextJiggle
    {
        get => _timeUntilNextJiggle;
        set { _timeUntilNextJiggle = value; OnPropertyChanged(); OnPropertyChanged(nameof(KeepAwakeProgress)); }
    }

    public double KeepAwakeProgress 
    {
        get 
        {
            if (KeepAwakeInterval <= 0) return 0;
            // Progress goes from 0 to 100 as time approaches interval
            double elapsed = KeepAwakeInterval - TimeUntilNextJiggle;
            return Math.Min((elapsed / KeepAwakeInterval) * 100, 100);
        }
    }
    
    public MainViewModel()
    {
        SaveConfigCommand = new RelayCommand(_ => SaveConfig(showNotification: true));
        AutoConfigCommand = new RelayCommand(_ => AutoConfigure());
        ResetConfigCommand = new RelayCommand(_ => ResetToBatchDefaults());
        EnterStealthModeCommand = new RelayCommand(_ => IsStealthMode = true);
        RestartAsAdminCommand = new RelayCommand(_ => RestartAsAdmin());
        RefreshUnmineableCommand = new RelayCommand(_ => _ = RefreshUnmineableStatsAsync());
        
        var config = LoadConfig();

        // Ensure we have configs for both
        var xmrigConfig = config.Miners.FirstOrDefault(m => m.Type == MinerType.XMRig) 
                          ?? new MinerConfig { Type = MinerType.XMRig };
        
        var rigelConfig = config.Miners.FirstOrDefault(m => m.Type == MinerType.Rigel) 
                          ?? new MinerConfig { Type = MinerType.Rigel };

        XmrigMiner = new MinerViewModel(xmrigConfig);
        RigelMiner = new MinerViewModel(rigelConfig);

        // Fill missing pool/algorithm/wallet with user defaults on startup / 시작 시 풀·알고리즘·지갑 누락분 자동 보완
        bool hadXmrigWallet = !string.IsNullOrWhiteSpace(XmrigMiner.Config.WalletAddress);
        bool hadRigelWallet = !string.IsNullOrWhiteSpace(RigelMiner.Config.WalletAddress);
        ApplyFactoryDefaults(overwrite: false);
        if (!hadXmrigWallet || !HasWorkerSuffix(XmrigMiner.Config.WalletAddress))
        {
            XmrigMiner.Config.WalletAddress = NormalizeWalletWorker(XmrigMiner.Config.WalletAddress, $".{Environment.MachineName}_CPU");
        }
        if (!hadRigelWallet || !HasWorkerSuffix(RigelMiner.Config.WalletAddress))
        {
            RigelMiner.Config.WalletAddress = NormalizeWalletWorker(RigelMiner.Config.WalletAddress, $".{Environment.MachineName}_GPU");
        }

        // Idle Timer (1Hz)
        _idleTimer = new System.Windows.Threading.DispatcherTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();

        // unMineable Stats Timer (5 min)
        _unmineableTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(5)
        };
        _unmineableTimer.Tick += (_, _) => _ = RefreshUnmineableStatsAsync();
        _unmineableTimer.Start();
        _ = RefreshUnmineableStatsAsync();

        // Subscribe to miner status and health changes for Stealth Status
        XmrigMiner.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MinerViewModel.Status) or nameof(MinerViewModel.HealthStatus)) UpdateStealthStatus();
        };
        RigelMiner.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName is nameof(MinerViewModel.Status) or nameof(MinerViewModel.HealthStatus)) UpdateStealthStatus();
        };
        
        UpdateStealthStatus(); // Initial state

        // Non-blocking startup checks / 기동 지연 방지를 위한 비동기 초기화
        _ = LoadStartupTaskStatusAsync();
        _ = Task.Run(CleanupZombieMiners);
    }

    private void RestartAsAdmin()
    {
        Cleanup();
        if (AdminHelper.RestartAsAdmin())
        {
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            _idleTimer.Start();
            _unmineableTimer.Start();
        }
    }

    private async Task RefreshUnmineableStatsAsync()
    {
        if (_isRefreshingUnmineable) return;
        _isRefreshingUnmineable = true;

        try
        {
            string coin = string.Empty;
            string address = string.Empty;
            if (!UnmineableService.TryParseWallet(XmrigMiner.Config.WalletAddress, out coin, out address) &&
                !UnmineableService.TryParseWallet(RigelMiner.Config.WalletAddress, out coin, out address))
            {
                UnmineableBalanceText = "Wallet not configured";
                UnmineableMetaText = "Set COIN:ADDRESS format in Settings";
                UnmineableProgress = 0;
                return;
            }

            var stats = await UnmineableService.FetchStatsAsync(coin, address);
            if (stats == null)
            {
                UnmineableMetaText = $"API fetch failed ({DateTime.Now:HH:mm:ss})";
                return;
            }

            UnmineableBalanceText = $"{stats.Balance:F6} / {stats.PaymentThreshold:0.##} {stats.Coin}";
            UnmineableProgress = stats.ProgressPercent;
            UnmineableMetaText = $"{stats.ProgressPercent:F1}%  |  Online Workers: {stats.OnlineWorkers}  |  Updated: {stats.UpdatedAt:HH:mm:ss}";
        }
        finally
        {
            _isRefreshingUnmineable = false;
        }
    }

    private async Task LoadStartupTaskStatusAsync()
    {
        try
        {
            bool isRegistered = await Task.Run(AdminHelper.IsStartupTaskRegistered);
            if (!_isAutoStartAdminTouched && _isAutoStartAdminEnabled != isRegistered)
            {
                // Update backing field directly to avoid triggering SetStartupTask / SetStartupTask 중복 호출 방지
                _isAutoStartAdminEnabled = isRegistered;
                OnPropertyChanged(nameof(IsAutoStartAdminEnabled));
            }
        }
        catch { /* Ignore startup task query failures */ }
    }

    private void CleanupZombieMiners()
    {
        try
        {
            string minerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Miners");
            if (XmrigMiner?.Status != "Running")
            {
                ProcessManager.KillProcessByPath("xmrig", minerDir);
            }
            if (RigelMiner?.Status != "Running")
            {
                ProcessManager.KillProcessByPath("rigel", minerDir);
            }
        }
        catch { /* Ignore */ }
    }

    // Stealth Status Logic
    private string _stealthStatusText = "Status: Idle";
    public string StealthStatusText
    {
        get => _stealthStatusText;
        set { _stealthStatusText = value; OnPropertyChanged(); }
    }

    private void UpdateStealthStatus()
    {
        bool isC = XmrigMiner.Status == "Running";
        bool isG = RigelMiner.Status == "Running";
        bool hasWarn = XmrigMiner.HasHealthWarning || RigelMiner.HasHealthWarning;
        string warnSuffix = hasWarn ? " (!)" : string.Empty;

        if (isC && isG) StealthStatusText = $"Status: Run C+G{warnSuffix}";
        else if (isC) StealthStatusText = $"Status: Run C{warnSuffix}";
        else if (isG) StealthStatusText = $"Status: Run G{warnSuffix}";
        else StealthStatusText = $"Status: Idle{warnSuffix}";
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
        _healthTickCounter++;
        if (_healthTickCounter >= 10)
        {
            _healthTickCounter = 0;
            XmrigMiner.EvaluateHealth();
            RigelMiner.EvaluateHealth();
        }

        if (!IsIdleMiningEnabled && !IsKeepAwakeEnabled)
        {
            CurrentIdleSeconds = 0;
            _virtualIdleAccumulator = 0;
            return;
        }

        double currentSystemIdle = Services.IdleDetector.GetIdleTimeSeconds();

        // 1. Detect Reset
        if (currentSystemIdle < _lastSystemIdleTime)
        {
            // System idle timer reset! Was it us?
            if (_justJiggled)
            {
                // It was us! Preserve the time we had accumulated
                _virtualIdleAccumulator += _lastSystemIdleTime;
            }
            else
            {
                // It was the user! Real reset.
                _virtualIdleAccumulator = 0;
            }
        }
        
        // 2. Clear Jiggle Flag (consumed)
        _justJiggled = false;
        
        // 3. Update Last Idle
        _lastSystemIdleTime = currentSystemIdle;

        // 4. Calculate Total (Virtual) Idle Time
        // The accumulator holds previous chunks of time that were interrupted by our jiggles.
        // currentSystemIdle is the time since the LAST reset (which might be our jiggle).
        CurrentIdleSeconds = currentSystemIdle + _virtualIdleAccumulator;

        // --- Mining Logic ---
        if (IsIdleMiningEnabled)
        {
            double StartThreshold = (double)IdleMiningStartDelay;
            const double StopThreshold = 1.0;

            if (CurrentIdleSeconds >= StartThreshold)
            {
                if (XmrigMiner.Status == "Stopped" && XmrigMiner.Config.Enabled && XmrigMiner.CanAutoRestart) XmrigMiner.StartCommand.Execute(null);
                if (RigelMiner.Status == "Stopped" && RigelMiner.Config.Enabled && RigelMiner.CanAutoRestart) RigelMiner.StartCommand.Execute(null);
            }
            else if (CurrentIdleSeconds < StopThreshold)
            {
                // Only stop if it's a REAL user action (accumulator is 0 means fresh start or user reset)
                // Actually, if CurrentIdleSeconds < 1, it implies accumulator is 0 AND system idle is < 1.
                if (XmrigMiner.Status == "Running") XmrigMiner.StopCommand.Execute(null);
                if (RigelMiner.Status == "Running") RigelMiner.StopCommand.Execute(null);
            }
        }

        // --- Keep Awake Logic ---
        if (IsKeepAwakeEnabled)
        {
            // Jiggle every Interval seconds
            // We use CurrentIdleSeconds to ensure we jiggle based on total inactivity
            // But to avoid drift or double jiggling, we can check if (CurrentIdle % Interval) is small,
            // OR simply keep track of 'last jiggle time'.
            // Simpler approach: If system idle time is reaching the interval, jiggle.
            
            // However, since we reset system idle, we should check simply `currentSystemIdle`
            // If we don't jiggle, system puts PC to sleep.
            // So we must jiggle before system sleep timeout.
            // User sets Interval. Let's strictly follow it.
            
            // To prevent integer precision issues with Modulo on doubles, we use a small epsilon window
            // But simpler: just track time since last action? 
            // Actually, we rely on the OS idle timer. We just need to ensure it never exceeds Interval.
            
            if (currentSystemIdle >= KeepAwakeInterval)
            {
                // Perform Jiggle - Make it visible as requested
                _justJiggled = true;
                
                // Run on background thread to avoid blocking UI during delay
                Task.Run(async () => 
                {
                    try
                    {
                        // [Korea] Intense Jiggle: 10px로 이동 거리 확대 & 쉐이킷(Shake) 동작
                        // Right -> Left -> Right -> Left (확실한 깨우기)
                        uint move = 10;
                        uint moveBack = unchecked((uint)-10);
                        
                        for (int i = 0; i < 2; i++)
                        {
                            mouse_event(MOUSEEVENTF_MOVE, move, 0, 0, 0);
                            await Task.Delay(50);
                            mouse_event(MOUSEEVENTF_MOVE, moveBack, 0, 0, 0);
                            await Task.Delay(50);
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ignore any errors in background task
                        System.Diagnostics.Debug.WriteLine($"Jiggle failed: {ex.Message}");
                    }
                });
            }
            
            // Visualization Update
            double timeRemaining = KeepAwakeInterval - currentSystemIdle;
            if (timeRemaining < 0) timeRemaining = 0;
            TimeUntilNextJiggle = timeRemaining;
        }
        else
        {
             TimeUntilNextJiggle = 0;
        }
    }

    private void AutoConfigure()
    {
        try
        {
            // Fill empty fields with defaults so new PCs work in one click / 빈 설정에서도 기본값 자동 보완
            ApplyFactoryDefaults(overwrite: false);
            XmrigMiner.Config.Enabled = true;

            // 1. Find Executables
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string minersDir = Path.Combine(baseDir, "Miners");

            if (Directory.Exists(minersDir))
            {
                var xmrigExe = Directory.GetFiles(minersDir, "xmrig.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(xmrigExe)) XmrigMiner.Config.ExecutablePath = xmrigExe;

                var rigelExe = Directory.GetFiles(minersDir, "rigel.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(rigelExe)) RigelMiner.Config.ExecutablePath = rigelExe;
            }

            // 2. CPU & RAM Optimization
            int coreCount = Environment.ProcessorCount;
            int threads = Math.Max(1, (int)(coreCount * 0.75));
            if (!XmrigMiner.Config.ExtraArguments.Contains("--cpu-max-threads-hint", StringComparison.OrdinalIgnoreCase))
            {
                XmrigMiner.Config.ExtraArguments = $"{XmrigMiner.Config.ExtraArguments} --cpu-max-threads-hint={threads * 100 / coreCount}".Trim();
            }

            double totalRamGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0);
            string randomXMode = (totalRamGb > 0 && totalRamGb < 6.0) ? "light" : "fast";
            XmrigMiner.Config.ExtraArguments = SetRandomXMode(XmrigMiner.Config.ExtraArguments, randomXMode);

            // 2.5 Normalize Worker Suffixes
            string machineName = Environment.MachineName;
            if (!string.IsNullOrWhiteSpace(XmrigMiner.Config.WalletAddress))
            {
                XmrigMiner.Config.WalletAddress = NormalizeWalletWorker(XmrigMiner.Config.WalletAddress, $".{machineName}_CPU");
            }
            if (!string.IsNullOrWhiteSpace(RigelMiner.Config.WalletAddress))
            {
                RigelMiner.Config.WalletAddress = NormalizeWalletWorker(RigelMiner.Config.WalletAddress, $".{machineName}_GPU");
            }

            // 3. Multi-GPU NVIDIA & VRAM Detection
            var gpus = DetectNvidiaGpus();
            var usableGpus = gpus.Where(g => g.VramMiB >= 2800).ToList();
            string gpuSummary;

            if (usableGpus.Count == 0)
            {
                RigelMiner.Config.Enabled = false;
                gpuSummary = gpus.Count > 0
                    ? $"Disabled ({gpus.Count} GPU(s) detected, all < 3GB VRAM)"
                    : "Disabled (No NVIDIA GPU detected)";
            }
            else
            {
                RigelMiner.Config.Enabled = true;

                // Filter out < 3GB cards if mixed / 저용량 GPU 혼재 시 채굴 가능 장치 인덱스(-d)만 지정
                if (usableGpus.Count < gpus.Count)
                {
                    string devicesArg = string.Join(",", usableGpus.Select(g => g.Index));
                    RigelMiner.Config.ExtraArguments = SetRigelOptionValue(RigelMiner.Config.ExtraArguments, "-d", @"(?:-d|--devices)", devicesArg);
                }

                // Configure per-GPU small-lut / GPU별 VRAM 크기에 맞춰 small-lut 설정 (예: on 또는 on,off)
                bool anySmall = usableGpus.Any(g => g.VramMiB <= 4500);
                string? lutArg = !anySmall
                    ? null
                    : (usableGpus.Count == 1
                        ? "on"
                        : string.Join(",", usableGpus.Select(g => g.VramMiB <= 4500 ? "on" : "off")));
                RigelMiner.Config.ExtraArguments = SetRigelOptionValue(RigelMiner.Config.ExtraArguments, "--nexapow-small-lut", @"--nexapow-small-lut", lutArg);

                // Ensure single thermal/fan values apply across all GPUs / 멀티 GPU 환경에서 단일 온도·팬 설정 일괄 적용
                if (usableGpus.Count > 1 &&
                    !RigelMiner.Config.ExtraArguments.Contains("--multi-device-arg-mode", StringComparison.OrdinalIgnoreCase))
                {
                    RigelMiner.Config.ExtraArguments = SetRigelOptionValue(RigelMiner.Config.ExtraArguments, "--multi-device-arg-mode", @"--multi-device-arg-mode", "first");
                }

                string lutDesc = lutArg == null ? "big-lut" : $"small-lut {lutArg}";
                string gpuLines = string.Join("\n  ", usableGpus.Select(g => $"GPU#{g.Index}: {g.Name} ({g.VramMiB} MiB)"));
                gpuSummary = $"Enabled ({usableGpus.Count}/{gpus.Count} usable, {lutDesc})\n  {gpuLines}";
            }

            SaveConfig(showNotification: false);
            string ramText = totalRamGb > 0 ? $"{totalRamGb:F1} GB" : "Unknown";
            string autoConfigSummary =
                $"Auto-configuration completed!\n" +
                $"RAM: {ramText} (RandomX mode: {randomXMode})\n" +
                $"CPU Hint: {threads} / {coreCount} threads\n" +
                $"GPU: {gpuSummary}\n" +
                $"Workers: .{machineName}_CPU / _GPU";
            NotifySettingsSavedAndPromptRestart(autoConfigSummary, "Auto Configure");
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Auto-configuration failed: {ex.Message}");
        }
    }

    private void ApplyFactoryDefaults(bool overwrite)
    {
        if (overwrite || string.IsNullOrWhiteSpace(XmrigMiner.Config.Algorithm))
            XmrigMiner.Config.Algorithm = "rx";
        if (overwrite || string.IsNullOrWhiteSpace(XmrigMiner.Config.PoolUrl))
            XmrigMiner.Config.PoolUrl = "stratum+ssl://rx-asia.unmineable.com:443";
        if (overwrite || string.IsNullOrWhiteSpace(XmrigMiner.Config.WalletAddress))
            XmrigMiner.Config.WalletAddress = "ALGO:Y3NPRE7TTC4G2HNKCOTMH5YRVB2OXOWLA4GJYWNGHTNKD7FYMJJL7MJSNA.unmineable_worker_dft_dt_cpu";
        if (overwrite || string.IsNullOrWhiteSpace(XmrigMiner.Config.ExtraArguments))
            XmrigMiner.Config.ExtraArguments = "--cpu-priority 0 --huge-pages-jit --randomx-mode=fast --randomx-wrmsr --randomx-rdmsr --print-time=60 --keepalive=true";
        if (overwrite)
            XmrigMiner.Config.Enabled = true;

        // Rigel Defaults (Updated with tc[80-90] and --fan-control 100 per user confirmation on 2026-11-25)
        if (overwrite || string.IsNullOrWhiteSpace(RigelMiner.Config.Algorithm))
            RigelMiner.Config.Algorithm = "nexapow";
        if (overwrite || string.IsNullOrWhiteSpace(RigelMiner.Config.PoolUrl))
            RigelMiner.Config.PoolUrl = "stratum+ssl://nexapow-asia.unmineable.com:443";
        if (overwrite || string.IsNullOrWhiteSpace(RigelMiner.Config.WalletAddress))
            RigelMiner.Config.WalletAddress = "ALGO:Y3NPRE7TTC4G2HNKCOTMH5YRVB2OXOWLA4GJYWNGHTNKD7FYMJJL7MJSNA.unmineable_worker_dft_DT_GPU";
        if (overwrite || string.IsNullOrWhiteSpace(RigelMiner.Config.ExtraArguments))
            RigelMiner.Config.ExtraArguments = "--no-strict-ssl --no-tui --stats-interval 60 --temp-limit tc[80-90]tm[105-115] --fan-control 100 --nexapow-small-lut on";
        if (overwrite)
            RigelMiner.Config.Enabled = true;
    }

    private static bool HasWorkerSuffix(string wallet)
    {
        if (string.IsNullOrWhiteSpace(wallet)) return false;
        int hashIdx = wallet.IndexOf('#');
        string basePart = hashIdx >= 0 ? wallet[..hashIdx] : wallet;
        int colonIdx = basePart.IndexOf(':');
        return basePart.IndexOf('.', Math.Max(0, colonIdx + 1)) > 0;
    }

    private static string NormalizeWalletWorker(string wallet, string suffix)
    {
        int hashIdx = wallet.IndexOf('#');
        string basePart = hashIdx >= 0 ? wallet[..hashIdx] : wallet;
        string refPart = hashIdx >= 0 ? wallet[hashIdx..] : string.Empty;

        int colonIdx = basePart.IndexOf(':');
        int dotIdx = basePart.IndexOf('.', Math.Max(0, colonIdx + 1));
        string cleanBase = dotIdx > 0 ? basePart[..dotIdx] : basePart;

        return $"{cleanBase.Trim()}{suffix}{refPart}";
    }

    private static string SetRandomXMode(string args, string mode)
    {
        const string pattern = @"--randomx-mode=\S+";
        string replacement = $"--randomx-mode={mode}";
        if (System.Text.RegularExpressions.Regex.IsMatch(args, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            return System.Text.RegularExpressions.Regex.Replace(args, pattern, replacement, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        return string.IsNullOrWhiteSpace(args) ? replacement : $"{args.Trim()} {replacement}";
    }

    private static string SetRigelOptionValue(string args, string flagToken, string flagRegex, string? value)
    {
        string cleaned = System.Text.RegularExpressions.Regex.Replace(
            args ?? string.Empty,
            $@"(?:^|\s){flagRegex}(?:=|\s+)(?:""[^""]+""|\S+)",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s{2,}", " ");
        if (string.IsNullOrWhiteSpace(value)) return cleaned;

        string token = $"{flagToken} {value.Trim()}";
        return string.IsNullOrEmpty(cleaned) ? token : $"{cleaned} {token}";
    }

    private static List<(int Index, string Name, int VramMiB)> DetectNvidiaGpus()
    {
        var result = new List<(int Index, string Name, int VramMiB)>();

        // 1. Prefer nvidia-smi for multi-GPU index and exact VRAM > 4GB / 멀티 GPU 인덱스 및 4GB 이상 정확한 VRAM 조회를 위해 nvidia-smi 우선 사용
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=index,name,memory.total --format=csv,noheader,nounits",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (proc != null)
            {
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(4000);
                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split(',');
                        if (parts.Length < 3) continue;

                        if (int.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int idx) &&
                            int.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int vramMiB))
                        {
                            result.Add((idx, parts[1].Trim(), vramMiB));
                        }
                    }

                    if (result.Count > 0) return result;
                }
            }
        }
        catch { /* Fallback to WMI */ }

        // 2. WMI Fallback
        if (OperatingSystem.IsWindows())
        {
            try
            {
                int idx = 0;
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
                foreach (var obj in searcher.Get())
                {
                    string name = obj["Name"]?.ToString() ?? string.Empty;
                    if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                    {
                        int vramMiB = 0;
                        if (obj["AdapterRAM"] != null)
                        {
                            ulong bytes = Convert.ToUInt64(obj["AdapterRAM"], System.Globalization.CultureInfo.InvariantCulture);
                            vramMiB = (int)(bytes / (1024UL * 1024UL));
                        }
                        result.Add((idx++, name, vramMiB));
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WMI Error] {ex.Message}");
            }
        }

        return result;
    }

    private void ResetToBatchDefaults()
    {
        try
        {
            // 1. Apply Hardcoded Factory Defaults
            ApplyFactoryDefaults(overwrite: true);

            // 2. Try to find and apply local batch file settings (Override if exists)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var batFiles = Directory.GetFiles(baseDir, "*.bat");

            if (batFiles.Length > 0)
            {
                foreach (var file in batFiles)
                {
                    string content = ReadAllTextSmart(file); // [Korea] 인코딩 자동 감지
                    
                    if (file.Contains("xmrig", StringComparison.OrdinalIgnoreCase))
                    {
                        var algo = ExtractArg(content, "-a");

                        var pool = ExtractArg(content, "-o");
                        var wallet = ExtractArg(content, "-u");

                        if (!string.IsNullOrEmpty(algo)) XmrigMiner.Config.Algorithm = algo;
                        if (!string.IsNullOrEmpty(pool)) XmrigMiner.Config.PoolUrl = pool;
                        if (!string.IsNullOrEmpty(wallet)) XmrigMiner.Config.WalletAddress = wallet;
                    }
                    else if (file.Contains("rigel", StringComparison.OrdinalIgnoreCase))
                    {
                        var algo = ExtractArg(content, "-a");
                        var pool = ExtractArg(content, "-o");
                        var wallet = ExtractArg(content, "-u");
                        
                        if (!string.IsNullOrEmpty(algo)) RigelMiner.Config.Algorithm = algo;
                        if (!string.IsNullOrEmpty(pool)) RigelMiner.Config.PoolUrl = pool;
                        if (!string.IsNullOrEmpty(wallet)) RigelMiner.Config.WalletAddress = wallet;
                    }
                }
                SaveConfig(showNotification: false);
                NotifySettingsSavedAndPromptRestart("Settings reset to Factory Defaults + Local Batch Files.", "Reset Settings");
            }
            else
            {
                SaveConfig(showNotification: false);
                NotifySettingsSavedAndPromptRestart("Settings reset to Factory Defaults (No batch files found).", "Reset Settings");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to reset settings: {ex.Message}");
        }
    }

    private string? ExtractArg(string content, string flag)
    {
        int index = content.IndexOf(flag);
        if (index == -1) return null;
        
        int start = index + flag.Length;
        while (start < content.Length && char.IsWhiteSpace(content[start])) start++;
        
        int end = start;
        while (end < content.Length && !char.IsWhiteSpace(content[end])) end++;
        
        return content.Substring(start, end - start);
    }

    private AppConfig LoadConfig()
    {
        if (File.Exists(ConfigFile))
        {
            try
            {
                string json = File.ReadAllText(ConfigFile);
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    // Apply global settings
                    IsIdleMiningEnabled = config.IsIdleMiningEnabled;

                    
                    IsKeepAwakeEnabled = config.IsKeepAwakeEnabled;
                    KeepAwakeInterval = config.KeepAwakeInterval;
                    IdleMiningStartDelay = config.IdleMiningStartDelay > 5 ? config.IdleMiningStartDelay : 60; // Validation

                    // Safety: Force Tray Start OFF due to visibility issues reported by user
                    IsStartInTrayEnabled = config.IsStartInTrayEnabled;
                    
                    IsStartInStealthEnabled = config.IsStartInStealthEnabled;
                    if (IsStartInStealthEnabled)
                    {
                        IsStealthMode = true;
                    }

                    return config;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to load settings: {ex.Message}");
            }
        }
        return new AppConfig();
    }

    public void SaveConfig(bool showNotification = false)
    {
        var config = new AppConfig
        {
            Miners = new List<MinerConfig> { XmrigMiner.Config, RigelMiner.Config },


            IsIdleMiningEnabled = IsIdleMiningEnabled,
            IsKeepAwakeEnabled = IsKeepAwakeEnabled,
            KeepAwakeInterval = KeepAwakeInterval,
            IsStartInTrayEnabled = IsStartInTrayEnabled,
            IsStartInStealthEnabled = IsStartInStealthEnabled,
            IdleMiningStartDelay = IdleMiningStartDelay
        };

        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
            if (showNotification)
            {
                NotifySettingsSavedAndPromptRestart("설정이 저장되었습니다.", "Settings Saved");
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save settings: {ex.Message}");
        }
    }

    // Prompt to restart running miners when settings are saved / 설정 저장 시 실행 중인 채굴기 즉시 재시작 여부 확인
    private void NotifySettingsSavedAndPromptRestart(string headerMessage, string caption)
    {
        bool xmrigRunning = XmrigMiner.Status == "Running";
        bool rigelRunning = RigelMiner.Status == "Running";

        if (xmrigRunning || rigelRunning)
        {
            var result = System.Windows.MessageBox.Show(
                $"{headerMessage}\n\n" +
                "현재 채굴기가 실행 중입니다.\n" +
                "지금 채굴기를 재시작해 변경된 설정을 바로 적용할까요?\n" +
                "(※ Start in Tray / Start in Stealth 옵션은 다음 프로그램 실행 시 적용)",
                caption,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _ = RestartRunningMinersAsync(xmrigRunning, rigelRunning);
            }
        }
        else
        {
            System.Windows.MessageBox.Show(
                $"{headerMessage}\n\n" +
                "변경된 채굴 설정은 다음 채굴 시작 시 자동 적용됩니다.\n" +
                "(※ Start in Tray / Start in Stealth 옵션은 다음 프로그램 실행 시 적용)",
                caption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private async Task RestartRunningMinersAsync(bool restartXmrig, bool restartRigel)
    {
        try
        {
            if (restartXmrig && XmrigMiner.Status == "Running")
            {
                XmrigMiner.StopCommand.Execute(null);
            }
            if (restartRigel && RigelMiner.Status == "Running")
            {
                RigelMiner.StopCommand.Execute(null);
            }

            // Wait briefly for miner process exit and GPU/log file handles release / 프로세스·파일 핸들 해제 대기
            await Task.Delay(500);

            if (restartXmrig && XmrigMiner.Config.Enabled && XmrigMiner.StartCommand.CanExecute(null))
            {
                XmrigMiner.StartCommand.Execute(null);
            }
            if (restartRigel && RigelMiner.Config.Enabled && RigelMiner.StartCommand.CanExecute(null))
            {
                RigelMiner.StartCommand.Execute(null);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[RestartMiners Error] {ex.Message}");
        }
    }

    public void Cleanup()
    {
        _idleTimer.Stop();
        _unmineableTimer.Stop();
        
        if (XmrigMiner.Status == "Running") XmrigMiner.StopCommand.Execute(null);
        if (RigelMiner.Status == "Running") RigelMiner.StopCommand.Execute(null);
    }

    private string ReadAllTextSmart(string path)
    {
        try 
        {
            // 1. Detect BOM
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length >= 3)
                {
                    var bom = new byte[3];
                    fs.Read(bom, 0, 3);
                    
                    if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf) 
                        return File.ReadAllText(path, Encoding.UTF8);
                }
            }

            // 2. Try CP949 (Korean) as default fallback for batch files
            // CodePage 949 requires System.Text.Encoding.CodePages package
            Encoding cp949 = Encoding.GetEncoding(949);
            return File.ReadAllText(path, cp949);
        }
        catch
        {
            // Fallback to strict UTF-8 or system default if 949 fails
            return File.ReadAllText(path); 
        }
    }


    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
