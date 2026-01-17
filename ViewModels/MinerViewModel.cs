using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using EncryptionMinerControl.Models;
using EncryptionMinerControl.Services;
using System.Collections.ObjectModel;
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

        StartCommand = new RelayCommand(_ => Start(), _ => !_processManager.IsRunning);
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
        // Dispatch to UI thread
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (Logs.Count > 1000) Logs.RemoveAt(0);
            Logs.Add(message);
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? platformName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(platformName));
    }
}
