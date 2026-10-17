using Microsoft.Win32;

namespace DiscordStreamingIndicator;

/// <summary>
/// タスクトレイに常駐し、Discord の状態に応じて全モニターの枠を出し入れする。
/// 見逃しを防ぐことを優先し、判定できないときは警告側に倒す。
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    // エンコードが少しでも検出されたら配信中とみなす（実測: 配信中 約40%、非配信時 0%）。
    private const double EncodeThresholdPercent = 0.0;
    // 検出の遅れを小さくするため短い間隔で読む（ウォームアップ後の読み取りは数 ms）。
    private const int PollIntervalMs = 250;
    // 一瞬途切れただけで赤枠が消えないよう、約 3 秒連続で配信の兆候がなければ下げる。
    private const int OffAfterIdleTicks = 3000 / PollIntervalMs;

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "DiscordStreamingIndicator";
    private const string SettingsKeyPath = @"Software\DiscordStreamingIndicator";
    private const string ShowInCallValueName = "ShowInCallBorder";

    private readonly EncoderMonitor _monitor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollIntervalMs };
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _showInCallItem;
    private readonly List<BorderOverlay> _overlays = new();

    private OverlayStyle? _currentStyle;
    private int _idleTicks;
    private bool _polling;

    public TrayContext()
    {
        _startupItem = new ToolStripMenuItem("Windows 起動時に実行", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };
        _showInCallItem = new ToolStripMenuItem("通話中も黄色枠を表示", null, (_, _) => ToggleShowInCall())
        {
            Checked = IsShowInCallEnabled(),
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_showInCallItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => ExitThread());

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Text = "Discord 配信インジケーター: 待機中",
            ContextMenuStrip = menu,
            Visible = true,
        };

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _timer.Tick += (_, _) => Poll();
        _timer.Start();
    }

    private async void Poll()
    {
        if (_polling)
        {
            return;
        }
        _polling = true;

        double encode;
        bool capturing;
        bool inCall;
        try
        {
            // 初回の読み取りは数秒かかることがあるため UI スレッドを塞がない。
            (encode, capturing, inCall) = await Task.Run(() => (
                _monitor.ReadDiscordEncodeUtilization(),
                CapabilityUsage.IsInUse(CapabilityUsage.ScreenCapture, _monitor.DiscordExePaths),
                CapabilityUsage.IsInUse(CapabilityUsage.Microphone, _monitor.DiscordExePaths)));
        }
        catch (Exception ex)
        {
            // 判定できないときに「配信していない」と扱うと見逃しになるため、警告を出す。
            _trayIcon.Text = Truncate("Discord 配信インジケーター: 検出エラー " + ex.Message);
            if (_currentStyle != OverlayStyle.Streaming)
            {
                ApplyStyle(OverlayStyle.DetectionError);
            }
            return;
        }
        finally
        {
            _polling = false;
        }

        // 主判定は OS が記録する画面キャプチャの使用状況（Discord のクリップ機能がオフであることが前提。
        // オンだと常にキャプチャ中になり、赤枠が出っぱなしになる＝安全側に倒れる）。
        // GPU エンコードは、記録されない方式でキャプチャされた場合の保険。
        if (capturing || encode > EncodeThresholdPercent)
        {
            _idleTicks = 0;
            ApplyStyle(OverlayStyle.Streaming);
        }
        else if (_currentStyle == OverlayStyle.Streaming && ++_idleTicks < OffAfterIdleTicks)
        {
            // 赤枠を維持する。
        }
        else if (inCall && _showInCallItem.Checked)
        {
            ApplyStyle(OverlayStyle.InCall);
        }
        else
        {
            ApplyStyle(null);
        }

        _trayIcon.Text = Truncate($"Discord 配信インジケーター: {DescribeState()} (キャプチャ {(capturing ? "使用中" : "なし")} / エンコード {encode:N1}%)");

        // 全画面アプリなどに前面を奪われても枠を最前面に戻す。
        foreach (var overlay in _overlays)
        {
            overlay.TopMost = true;
        }
    }

    private string DescribeState()
    {
        if (_currentStyle == OverlayStyle.Streaming) return "配信中";
        if (_currentStyle == OverlayStyle.InCall) return "通話中";
        return "待機中";
    }

    private void ApplyStyle(OverlayStyle? style)
    {
        if (_currentStyle == style)
        {
            return;
        }
        _currentStyle = style;
        _trayIcon.Icon = style == OverlayStyle.Streaming ? SystemIcons.Error
            : style is null ? SystemIcons.Shield
            : SystemIcons.Warning;
        RebuildOverlays();
    }

    private void RebuildOverlays()
    {
        foreach (var overlay in _overlays)
        {
            overlay.Close();
            overlay.Dispose();
        }
        _overlays.Clear();

        if (_currentStyle is null)
        {
            return;
        }
        foreach (var screen in Screen.AllScreens)
        {
            var overlay = new BorderOverlay(screen, _currentStyle);
            overlay.Show();
            _overlays.Add(overlay);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RebuildOverlays();

    private static string Truncate(string text) => text.Length <= 127 ? text : text[..127];

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is string;
    }

    private void ToggleStartup()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (IsStartupEnabled())
        {
            key.DeleteValue(RunValueName, false);
        }
        else
        {
            key.SetValue(RunValueName, $"\"{Application.ExecutablePath}\"");
        }
        _startupItem.Checked = IsStartupEnabled();
    }

    private static bool IsShowInCallEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        return key?.GetValue(ShowInCallValueName) is not int value || value != 0;
    }

    private void ToggleShowInCall()
    {
        var enabled = !_showInCallItem.Checked;
        using (var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
        {
            key.SetValue(ShowInCallValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        _showInCallItem.Checked = enabled;
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        _currentStyle = null;
        RebuildOverlays();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        base.ExitThreadCore();
    }
}
