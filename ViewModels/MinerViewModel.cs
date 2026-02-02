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
    private string _latestLog = "";
    
    // [Korea] UI 최적화: 로그 버퍼링 (ConcurrentQueue & Timer)
    // 1. Process -> Queue (빠름)
    // 2. Queue -> UI (0.2초마다 한 번씩 렌더링)
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private readonly DispatcherTimer _logUpdateTimer;

    // UI log buffer (Observable for UI binding)
    public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

    public MinerConfig Config { get; }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    public MinerViewModel(MinerConfig config)
    {
        Config = config;
        _processManager = new ProcessManager(OnLogReceived);

        // [Korea] Log Throttling Timer (5 FPS)
        _logUpdateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200) // 0.2초
        };
        _logUpdateTimer.Tick += ProcessLogBuffer;
        _logUpdateTimer.Start();

        StartCommand = new RelayCommand(_ => Start(), _ => !_processManager.IsRunning && Config.Enabled);
        StopCommand = new RelayCommand(_ => Stop(), _ => _processManager.IsRunning);
    }

    private void Start()
    {
        Status = "Running";
        string args = BuildArguments();
        _processManager.Start(Config.ExecutablePath, args);
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
                processedCount++;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? platformName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(platformName));
    }
}
