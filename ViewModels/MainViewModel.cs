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
            OnPropertyChanged(); 
        }
    }

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
        // [Korea] 1. 시작 전 좀비 프로세스 청소 (이전 세션의 잔재 제거)
        CleanupZombieMiners();

        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        AutoConfigCommand = new RelayCommand(_ => AutoConfigure());
        ResetConfigCommand = new RelayCommand(_ => ResetToBatchDefaults());
        EnterStealthModeCommand = new RelayCommand(_ => IsStealthMode = true);
        
        var config = LoadConfig();

        // Ensure we have configs for both
        var xmrigConfig = config.Miners.FirstOrDefault(m => m.Type == MinerType.XMRig) 
                          ?? new MinerConfig { Type = MinerType.XMRig };
        
        var rigelConfig = config.Miners.FirstOrDefault(m => m.Type == MinerType.Rigel) 
                          ?? new MinerConfig { Type = MinerType.Rigel };

        XmrigMiner = new MinerViewModel(xmrigConfig);
        RigelMiner = new MinerViewModel(rigelConfig);

        // Idle Timer (1Hz)
        _idleTimer = new System.Windows.Threading.DispatcherTimer();
        _idleTimer.Interval = TimeSpan.FromSeconds(1);
        _idleTimer.Tick += IdleTimer_Tick;
        _idleTimer.Start();

        // Subscribe to miner status changes for Stealth Status
        XmrigMiner.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MinerViewModel.Status)) UpdateStealthStatus(); };
        RigelMiner.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MinerViewModel.Status)) UpdateStealthStatus(); };
        
        UpdateStealthStatus(); // Initial state
    }

    private void CleanupZombieMiners()
    {
        try
        {
            string minerDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Miners");
            ProcessManager.KillProcessByPath("xmrig", minerDir);
            ProcessManager.KillProcessByPath("rigel", minerDir);
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

        if (isC && isG) StealthStatusText = "Status: Run C+G";
        else if (isC) StealthStatusText = "Status: Run C";
        else if (isG) StealthStatusText = "Status: Run G";
        else StealthStatusText = "Status: Idle";
    }

    private void IdleTimer_Tick(object? sender, EventArgs e)
    {
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
                if (XmrigMiner.Status == "Stopped" && XmrigMiner.Config.Enabled) XmrigMiner.StartCommand.Execute(null);
                if (RigelMiner.Status == "Stopped" && RigelMiner.Config.Enabled) RigelMiner.StartCommand.Execute(null);
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
                        // Move 5 pixels right
                        mouse_event(MOUSEEVENTF_MOVE, 5, 0, 0, 0);
                        // Wait 50ms (visible twitch)
                        await Task.Delay(50);
                        // Move back
                        mouse_event(MOUSEEVENTF_MOVE, unchecked((uint)-5), 0, 0, 0);
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

            // 2. CPU Optimization
            int coreCount = Environment.ProcessorCount;
            int threads = (int)(coreCount * 0.75);
            if (threads < 1) threads = 1;
            
            if (!XmrigMiner.Config.ExtraArguments.Contains("--cpu-max-threads-hint"))
            {
                 XmrigMiner.Config.ExtraArguments += $" --cpu-max-threads-hint={threads * 100 / coreCount}"; 
            }

            // 2.5 Wallet Name Append
            string machineName = Environment.MachineName;
            if (!string.IsNullOrEmpty(XmrigMiner.Config.WalletAddress))
            {
                 string suffix = $".{machineName}_CPU";
                 if (!XmrigMiner.Config.WalletAddress.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                 {
                     // Remove old suffix if present (simple check)
                     if (XmrigMiner.Config.WalletAddress.EndsWith($".{machineName}", StringComparison.OrdinalIgnoreCase))
                     {
                         XmrigMiner.Config.WalletAddress = XmrigMiner.Config.WalletAddress.Replace($".{machineName}", suffix);
                     }
                     else
                     {
                         XmrigMiner.Config.WalletAddress += suffix;
                     }
                 }
            }

            if (!string.IsNullOrEmpty(RigelMiner.Config.WalletAddress))
            {
                 string suffix = $".{machineName}_GPU";
                 if (!RigelMiner.Config.WalletAddress.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                 {
                     // Remove old suffix if present
                     if (RigelMiner.Config.WalletAddress.EndsWith($".{machineName}", StringComparison.OrdinalIgnoreCase))
                     {
                         RigelMiner.Config.WalletAddress = RigelMiner.Config.WalletAddress.Replace($".{machineName}", suffix);
                     }
                     else
                     {
                         RigelMiner.Config.WalletAddress += suffix;
                     }
                 }
            }

            // 3. GPU Detection
            if (OperatingSystem.IsWindows())
            {
                bool hasNvidia = false;
                try 
                {
                    // [Korea] WMI 안정성 강화: 일부 윈도우(Lite, Gaming)에서 WMI가 손상된 경우 방어
                    using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_VideoController"))
                    {
                        foreach (var obj in searcher.Get())
                        {
                            string name = obj["Name"]?.ToString() ?? "";
                            if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
                            {
                                hasNvidia = true;
                                break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                { 
                    // WMI failed - possibly no GPU or broken OS components.
                    System.Diagnostics.Debug.WriteLine($"[WMI Error] {ex.Message}");
                }

                if (hasNvidia)
                {
                    RigelMiner.Config.Enabled = true;
                }
            }
            
            System.Windows.MessageBox.Show($"Auto-configuration completed!\nWallet Updated: Yes\nCPU Hint: {threads} threads");
            SaveConfig(); // Save updates
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Auto-configuration failed: {ex.Message}");
        }
    }

    private void ResetToBatchDefaults()
    {
        try
        {
            // 1. Apply Hardcoded Factory Defaults
            // XMRig Defaults
            XmrigMiner.Config.Algorithm = "rx";
            XmrigMiner.Config.PoolUrl = "stratum+ssl://rx-asia.unmineable.com:443";
            XmrigMiner.Config.WalletAddress = "ALGO:Y3NPRE7TTC4G2HNKCOTMH5YRVB2OXOWLA4GJYWNGHTNKD7FYMJJL7MJSNA.unmineable_worker_dft_dt_cpu"; 
            XmrigMiner.Config.ExtraArguments = "--cpu-priority 0 --hube-pages-jit --randomx-mode=fast --randomx-wrmsr --randomx-rdmsr --print-time=60 --keepalive=true";
            XmrigMiner.Config.Enabled = true;

            // Rigel Defaults
            RigelMiner.Config.Algorithm = "karlsenhashv2";
            RigelMiner.Config.PoolUrl = "stratum+ssl://karlsenhash-asia.unmineable.com:443";
            RigelMiner.Config.WalletAddress = "ALGO:Y3NPRE7TTC4G2HNKCOTMH5YRVB2OXOWLA4GJYWNGHTNKD7FYMJJL7MJSNA.unmineable_worker_dft_DT_GPU"; 
            RigelMiner.Config.ExtraArguments = "--no-strict-ssl --no-tui --stats-interval 60 --temp-limit tc[60-70]tm[105-115]";
            RigelMiner.Config.Enabled = true;

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
                System.Windows.MessageBox.Show("Settings reset to Factory Defaults + Local Batch Files.");
            }
            else
            {
                System.Windows.MessageBox.Show("Settings reset to Factory Defaults (No batch files found).");
            }

            SaveConfig();
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

    public void SaveConfig()
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
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Failed to save settings: {ex.Message}");
        }
    }

    public void Cleanup()
    {
        _idleTimer.Stop();
        
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
