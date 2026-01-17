using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace EncryptionMinerControl.Models;

public enum MinerType
{
    XMRig,
    Rigel
}

public class MinerConfig : INotifyPropertyChanged
{
    private string _executablePath = string.Empty;
    private string _algorithm = string.Empty;
    private string _walletAddress = string.Empty;
    private string _poolUrl = string.Empty;
    private string _extraArguments = string.Empty;
    private bool _enabled = false;

    public MinerType Type { get; set; }

    public string ExecutablePath
    {
        get => _executablePath;
        set { _executablePath = value; OnPropertyChanged(); }
    }

    public string Algorithm
    {
        get => _algorithm;
        set { _algorithm = value; OnPropertyChanged(); }
    }

    public string WalletAddress
    {
        get => _walletAddress;
        set { _walletAddress = value; OnPropertyChanged(); }
    }

    public string PoolUrl
    {
        get => _poolUrl;
        set { _poolUrl = value; OnPropertyChanged(); }
    }

    public string ExtraArguments
    {
        get => _extraArguments;
        set { _extraArguments = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    [JsonIgnore]
    public string Name => Type == MinerType.XMRig ? "XMRig (CPU)" : "Rigel (GPU)";

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? unk = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(unk));
    }
}

public class AppConfig
{
    public List<MinerConfig> Miners { get; set; } = new List<MinerConfig>();
}
