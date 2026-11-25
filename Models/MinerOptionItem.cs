using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace EncryptionMinerControl.Models;

public enum MinerOptionKind
{
    BoolFlag,
    PairFlag,
    SpaceValue,
    EqualsValue,
    QuotedSpaceValue
}

/// <summary>
/// Represents a toggleable miner CLI option synchronized with ExtraArguments.
/// ExtraArguments 문자열과 양방향 동기화되는 개별 마이너 옵션 항목임.
/// </summary>
public class MinerOptionItem : INotifyPropertyChanged
{
    private bool _isChecked;
    private string _value = string.Empty;

    public string Label { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public MinerOptionKind Kind { get; init; }
    public string FlagToken { get; init; } = string.Empty;
    public string ParsePattern { get; init; } = string.Empty;
    public string RemovePattern { get; init; } = string.Empty;
    public string DefaultValue { get; init; } = string.Empty;
    public string Placeholder { get; init; } = string.Empty;

    public bool HasValue => Kind is MinerOptionKind.SpaceValue or MinerOptionKind.EqualsValue or MinerOptionKind.QuotedSpaceValue;

    public event Action<MinerOptionItem>? OptionChanged;

    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            if (value && HasValue && string.IsNullOrWhiteSpace(_value) && !string.IsNullOrEmpty(DefaultValue))
            {
                _value = DefaultValue;
                OnPropertyChanged(nameof(Value));
            }
            OnPropertyChanged();
            OptionChanged?.Invoke(this);
        }
    }

    public string Value
    {
        get => _value;
        set
        {
            string next = value ?? string.Empty;
            if (_value == next) return;
            _value = next;
            OnPropertyChanged();
            if (_isChecked)
            {
                OptionChanged?.Invoke(this);
            }
        }
    }

    public void SetStateSilently(bool isChecked, string? value)
    {
        if (_isChecked != isChecked)
        {
            _isChecked = isChecked;
            OnPropertyChanged(nameof(IsChecked));
        }

        if (value != null && _value != value)
        {
            _value = value;
            OnPropertyChanged(nameof(Value));
        }
    }

    public void SyncFromArguments(string? currentArgs)
    {
        string input = currentArgs ?? string.Empty;
        var match = Regex.Match(input, ParsePattern, RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            SetStateSilently(false, null);
            return;
        }

        if (!HasValue)
        {
            SetStateSilently(true, null);
            return;
        }

        string extracted = !string.IsNullOrEmpty(match.Groups[1].Value)
            ? match.Groups[1].Value
            : (match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty);
        SetStateSilently(true, extracted);
    }

    public string ApplyToArguments(string? currentArgs)
    {
        string cleaned = Regex.Replace(currentArgs ?? string.Empty, RemovePattern, string.Empty, RegexOptions.IgnoreCase);
        cleaned = Regex.Replace(cleaned.Trim(), @"\s{2,}", " ");

        if (!_isChecked) return cleaned;

        string? token = Kind switch
        {
            MinerOptionKind.BoolFlag or MinerOptionKind.PairFlag => FlagToken,
            MinerOptionKind.SpaceValue => BuildSpaceToken(_value),
            MinerOptionKind.EqualsValue => BuildEqualsToken(_value),
            MinerOptionKind.QuotedSpaceValue => BuildQuotedToken(_value),
            _ => null
        };

        if (string.IsNullOrEmpty(token)) return cleaned;
        return string.IsNullOrEmpty(cleaned) ? token : $"{cleaned} {token}";
    }

    private string? BuildSpaceToken(string raw)
    {
        string trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return $"{FlagToken} {trimmed}";
    }

    private string? BuildEqualsToken(string raw)
    {
        string trimmed = raw.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        string formatted = trimmed.Contains(' ') && !trimmed.StartsWith('"') ? $"\"{trimmed}\"" : trimmed;
        return $"{FlagToken}={formatted}";
    }

    private string? BuildQuotedToken(string raw)
    {
        string trimmed = raw.Trim().Trim('"');
        if (string.IsNullOrEmpty(trimmed)) return null;
        return $"{FlagToken} \"{trimmed}\"";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
