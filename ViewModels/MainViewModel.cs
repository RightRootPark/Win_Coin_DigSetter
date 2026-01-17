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

    public bool IsRunOnStartupEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("EncryptionMinerControl") != null;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key == null) return;

                if (value)
                {
                    string path = Environment.ProcessPath ?? "";
                    if (!string.IsNullOrEmpty(path))
                        key.SetValue("EncryptionMinerControl", path);
                }
                else
                {
                    if (key.GetValue("EncryptionMinerControl") != null)
                        key.DeleteValue("EncryptionMinerControl");
                }
                OnPropertyChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to change startup setting: {ex.Message}");
            }
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
        if (!IsIdleMiningEnabled) return;

        double idleSeconds = Services.IdleDetector.GetIdleTimeSeconds();

        // Thresholds
        const double StartThreshold = 60.0; // 1 minute
        const double StopThreshold = 1.0;   // 1 second (instant)

        if (idleSeconds >= StartThreshold)
        {
             // Start if not running
             if (XmrigMiner.Status == "Stopped" && XmrigMiner.Config.Enabled) XmrigMiner.StartCommand.Execute(null);
             if (RigelMiner.Status == "Stopped" && RigelMiner.Config.Enabled) RigelMiner.StartCommand.Execute(null);
        }
        else if (idleSeconds < StopThreshold)
        {
             // Stop if running
             // Note: This stops even if user manually started it, IF idle mode is checked.
             // This is consistent with "Check box... automatically stops when mouse moves".
             if (XmrigMiner.Status == "Running") XmrigMiner.StopCommand.Execute(null);
             if (RigelMiner.Status == "Running") RigelMiner.StopCommand.Execute(null);
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
            
            MessageBox.Show($"Auto-configuration completed!\nWallet Updated: Yes\nCPU Hint: {threads} threads");
            SaveConfig(); // Save updates
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Auto-configuration failed: {ex.Message}");
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
            MessageBox.Show("Settings reset from found batch files.");
            SaveConfig();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to reset settings: {ex.Message}");
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
                if (config != null) return config;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load settings: {ex.Message}");
            }
        }
        return new AppConfig();
    }

    public void SaveConfig()
    {
        var config = new AppConfig
        {
            Miners = new List<MinerConfig> { XmrigMiner.Config, RigelMiner.Config }
        };

        try
        {
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFile, json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
