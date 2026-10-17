using Microsoft.Win32;

namespace DiscordStreamingIndicator;

/// <summary>
/// Windows のプライバシー機能が記録する CapabilityAccessManager のレジストリから、
/// Discord が画面キャプチャやマイクを今まさに使用中かを読み取る。
/// これは OS 自身が記録する使用状況なので、Discord 側の実装に依存しない。
/// </summary>
internal static class CapabilityUsage
{
    /// <summary>Windows.Graphics.Capture による画面キャプチャ（配信の映像取得）。</summary>
    public const string ScreenCapture = "graphicsCaptureProgrammatic";

    /// <summary>マイク（= 通話中）。</summary>
    public const string Microphone = "microphone";

    private const string ConsentStorePath =
        @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    public static bool IsInUse(string capability, IEnumerable<string> discordExePaths)
    {
        using var root = Registry.CurrentUser.OpenSubKey($@"{ConsentStorePath}\{capability}\NonPackaged");
        if (root is null)
        {
            return false;
        }

        // 異常終了したバージョンの記録が stop=0 のまま残ることがあるため、
        // 実行中の Discord の実行ファイルに対応する記録だけを見る。
        foreach (var exePath in discordExePaths)
        {
            using var key = root.OpenSubKey(exePath.Replace('\\', '#'));
            if (key?.GetValue("LastUsedTimeStop") is long stop && stop == 0
                && key.GetValue("LastUsedTimeStart") is long start && start != 0)
            {
                return true;
            }
        }
        return false;
    }
}
