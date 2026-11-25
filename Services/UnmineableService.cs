using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace EncryptionMinerControl.Services;

public record UnmineableStats(
    string Coin,
    decimal Balance,
    decimal PaymentThreshold,
    double ProgressPercent,
    int OnlineWorkers,
    DateTime UpdatedAt);

/// <summary>
/// Queries unMineable public v4 API for payout balance and online worker counts.
/// unMineable 공개 v4 API에서 잔고, 출금 기준, 온라인 워커 수를 조회함.
/// </summary>
public static class UnmineableService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public static bool TryParseWallet(string? walletAddress, out string coin, out string address)
    {
        coin = string.Empty;
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(walletAddress)) return false;

        int colonIdx = walletAddress.IndexOf(':');
        if (colonIdx <= 0) return false;

        coin = walletAddress[..colonIdx].Trim().ToUpperInvariant();
        string rest = walletAddress[(colonIdx + 1)..].Trim();
        int sepIdx = rest.IndexOfAny(['.', '#']);
        address = (sepIdx >= 0 ? rest[..sepIdx] : rest).Trim();

        return !string.IsNullOrEmpty(coin) && !string.IsNullOrEmpty(address);
    }

    public static async Task<UnmineableStats?> FetchStatsAsync(string coin, string address)
    {
        try
        {
            string addressUrl = $"https://api.unmineable.com/v4/address/{Uri.EscapeDataString(address)}?coin={Uri.EscapeDataString(coin)}";
            using var resp = await Http.GetAsync(addressUrl).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                return null;

            if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                return null;

            decimal balance = TryReadDecimal(dataEl, "balance");
            decimal threshold = TryReadDecimal(dataEl, "payment_threshold");
            double progress = threshold > 0 ? Math.Min((double)(balance / threshold) * 100.0, 100.0) : 0.0;

            int onlineWorkers = 0;
            if (dataEl.TryGetProperty("uuid", out var uuidEl) && uuidEl.ValueKind == JsonValueKind.String)
            {
                string? uuid = uuidEl.GetString();
                if (!string.IsNullOrEmpty(uuid))
                {
                    onlineWorkers = await FetchOnlineWorkerCountAsync(uuid).ConfigureAwait(false);
                }
            }

            return new UnmineableStats(coin, balance, threshold, progress, onlineWorkers, DateTime.Now);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<int> FetchOnlineWorkerCountAsync(string uuid)
    {
        try
        {
            string url = $"https://api.unmineable.com/v4/account/{Uri.EscapeDataString(uuid)}/workers";
            using var resp = await Http.GetAsync(url).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return 0;

            string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean())
                return 0;

            if (!doc.RootElement.TryGetProperty("data", out var dataEl) || dataEl.ValueKind != JsonValueKind.Object)
                return 0;

            int count = 0;
            foreach (var algoProp in dataEl.EnumerateObject())
            {
                if (algoProp.Value.ValueKind != JsonValueKind.Object) continue;
                if (!algoProp.Value.TryGetProperty("workers", out var workersEl) || workersEl.ValueKind != JsonValueKind.Array) continue;

                foreach (var worker in workersEl.EnumerateArray())
                {
                    if (worker.TryGetProperty("online", out var onlineEl) && onlineEl.ValueKind == JsonValueKind.True)
                    {
                        count++;
                    }
                }
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }

    private static decimal TryReadDecimal(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var el)) return 0m;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out decimal num)) return num;
        if (el.ValueKind == JsonValueKind.String &&
            decimal.TryParse(el.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed))
        {
            return parsed;
        }
        return 0m;
    }
}
