using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using EncryptionMinerControl.Models;

namespace EncryptionMinerControl.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private const string ConfigFile = "settings.json";
    
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
        }
    }

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

    public double IdleProgress => Math.Min((CurrentIdleSeconds / 60.0) * 100, 100);

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
        SaveConfigCommand = new RelayCommand(_ => SaveConfig());
        AutoConfigCommand = new RelayCommand(_ => AutoConfigure());
        ResetConfigCommand = new RelayCommand(_ => ResetToBatchDefaults());
        
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
            const double StartThreshold = 60.0;
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
                catch { /* Ignore WMI errors */ }

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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var batFiles = Directory.GetFiles(baseDir, "*.bat");

            foreach (var file in batFiles)
            {
                string content = File.ReadAllText(file);
                
                // Very basic parser assuming standard command line flags
                if (file.Contains("xmrig", StringComparison.OrdinalIgnoreCase))
                {
                    XmrigMiner.Config.Algorithm = ExtractArg(content, "-a") ?? XmrigMiner.Config.Algorithm;
                    XmrigMiner.Config.PoolUrl = ExtractArg(content, "-o") ?? XmrigMiner.Config.PoolUrl;
                    XmrigMiner.Config.WalletAddress = ExtractArg(content, "-u") ?? XmrigMiner.Config.WalletAddress;
                }
                else if (file.Contains("rigel", StringComparison.OrdinalIgnoreCase))
                {
                    RigelMiner.Config.Algorithm = ExtractArg(content, "-a") ?? RigelMiner.Config.Algorithm;
                    RigelMiner.Config.PoolUrl = ExtractArg(content, "-o") ?? RigelMiner.Config.PoolUrl;
                    RigelMiner.Config.WalletAddress = ExtractArg(content, "-u") ?? RigelMiner.Config.WalletAddress;
                }
            }
            System.Windows.MessageBox.Show("Settings reset from found batch files.");
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
                    IsStartInTrayEnabled = config.IsStartInTrayEnabled;
                    
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


            IsKeepAwakeEnabled = IsKeepAwakeEnabled,
            KeepAwakeInterval = KeepAwakeInterval,
            IsStartInTrayEnabled = IsStartInTrayEnabled
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

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
