using System.Diagnostics;
using System.Text.RegularExpressions;

namespace StreamingIndicatorForDiscord;

/// <summary>
/// Discord プロセスが GPU の動画エンコードエンジンを使っているかを
/// Windows のパフォーマンスカウンター "GPU Engine" から読み取る。
/// 配信中は映像をエンコードするため、これが配信状態の判定材料になる。
/// </summary>
internal sealed partial class EncoderMonitor
{
    private static readonly string[] DiscordProcessNames = ["Discord", "DiscordPTB", "DiscordCanary", "DiscordDevelopment"];

    private readonly PerformanceCounterCategory _category = new("GPU Engine");
    private Dictionary<string, CounterSample> _previous = new();

    [GeneratedRegex(@"^pid_(\d+)_.*_engtype_VideoEncode$", RegexOptions.IgnoreCase)]
    private static partial Regex EncodeInstancePattern();

    // プロセス列挙は重い（1 回数十 ms）ため、Discord のプロセス一覧はこの間隔でだけ更新する。
    private static readonly TimeSpan ProcessRefreshInterval = TimeSpan.FromSeconds(2);

    private HashSet<int> _discordPids = new();
    private HashSet<string> _discordExePaths = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastProcessRefresh = DateTime.MinValue;

    /// <summary>実行中の Discord の実行ファイルのパス（最大 2 秒前の情報）。</summary>
    public IReadOnlyCollection<string> DiscordExePaths => _discordExePaths;

    private void RefreshDiscordProcessesIfStale()
    {
        if (DateTime.UtcNow - _lastProcessRefresh < ProcessRefreshInterval)
        {
            return;
        }
        _lastProcessRefresh = DateTime.UtcNow;

        var pids = new HashSet<int>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var process in Process.GetProcesses())
        {
            if (DiscordProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            {
                pids.Add(process.Id);
                try
                {
                    if (process.MainModule?.FileName is { } path)
                    {
                        paths.Add(path);
                    }
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // 権限の都合で読めないプロセスは飛ばす（同じ exe の別プロセスで拾える）。
                }
            }
            process.Dispose();
        }
        _discordPids = pids;
        _discordExePaths = paths;
    }

    /// <summary>Discord 全プロセスの動画エンコード使用率の合計 (%)。</summary>
    public double ReadDiscordEncodeUtilization()
    {
        RefreshDiscordProcessesIfStale();
        var discordPids = _discordPids;

        var data = _category.ReadCategory()["Utilization Percentage"];
        var current = new Dictionary<string, CounterSample>();
        double total = 0;

        if (data is null)
        {
            _previous = current;
            return 0;
        }

        foreach (InstanceData instance in data.Values)
        {
            var match = EncodeInstancePattern().Match(instance.InstanceName);
            if (!match.Success || !discordPids.Contains(int.Parse(match.Groups[1].Value)))
            {
                continue;
            }

            var sample = instance.Sample;
            current[instance.InstanceName] = sample;
            // 使用率はカウンターの差分から計算するため、前回のサンプルが必要。
            if (_previous.TryGetValue(instance.InstanceName, out var previousSample))
            {
                total += CounterSample.Calculate(previousSample, sample);
            }
        }

        _previous = current;
        return total;
    }
}
