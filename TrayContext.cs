using Microsoft.Win32;

namespace StreamingIndicatorForDiscord;

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
    // 枠を最前面に戻し直す間隔（約 2 秒）。
    private const int TopMostReassertTicks = 2000 / PollIntervalMs;

    private const string AppName = "Streaming Indicator for Discord";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "StreamingIndicatorForDiscord";
    private const string SettingsKeyPath = @"Software\StreamingIndicatorForDiscord";
    private const string ShowInCallValueName = "ShowInCallBorder";
    private const string ShowLabelValueName = "ShowLabel";
    // 点滅の周期 (ms)。0 は点滅しない。
    private const string BlinkPeriodValueName = "BlinkPeriodMs";
    private const string LanguageValueName = "Language";

    private readonly EncoderMonitor _monitor = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = PollIntervalMs };
    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _startupItem;
    private readonly ToolStripMenuItem _showInCallItem;
    private readonly ToolStripMenuItem _showLabelItem;
    private readonly ToolStripMenuItem _blinkItem;
    private readonly ToolStripMenuItem _blinkOffItem;
    private readonly ToolStripMenuItem _blinkSlowItem;
    private readonly ToolStripMenuItem _blinkNormalItem;
    private readonly ToolStripMenuItem _blinkFastItem;
    private readonly ToolStripMenuItem _languageItem;
    private readonly ToolStripMenuItem _languageAutoItem;
    private readonly ToolStripMenuItem _languageJapaneseItem;
    private readonly ToolStripMenuItem _languageEnglishItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly ContextMenuStrip _menu;
    private readonly List<OverlayWindow> _overlays = new();

    private Strings _strings;
    private OverlayStyle? _currentStyle;
    private int _idleTicks;
    private bool _polling;
    private int _ticksSinceTopMost;

    public TrayContext()
    {
        _startupItem = new ToolStripMenuItem("", null, (_, _) => ToggleStartup())
        {
            Checked = IsStartupEnabled(),
        };
        _showInCallItem = new ToolStripMenuItem("", null, (sender, _) => ToggleSetting((ToolStripMenuItem)sender!, ShowInCallValueName))
        {
            Checked = IsSettingEnabled(ShowInCallValueName),
        };
        _showLabelItem = new ToolStripMenuItem("", null, (sender, _) =>
        {
            ToggleSetting((ToolStripMenuItem)sender!, ShowLabelValueName);
            RebuildOverlays();
        })
        {
            Checked = IsSettingEnabled(ShowLabelValueName),
        };
        _blinkOffItem = new ToolStripMenuItem("", null, (_, _) => SetBlinkPeriod(0)) { Tag = 0 };
        _blinkSlowItem = new ToolStripMenuItem("", null, (_, _) => SetBlinkPeriod(2000)) { Tag = 2000 };
        _blinkNormalItem = new ToolStripMenuItem("", null, (_, _) => SetBlinkPeriod(1000)) { Tag = 1000 };
        _blinkFastItem = new ToolStripMenuItem("", null, (_, _) => SetBlinkPeriod(500)) { Tag = 500 };
        _blinkItem = new ToolStripMenuItem("");
        _blinkItem.DropDownItems.AddRange([_blinkOffItem, _blinkSlowItem, _blinkNormalItem, _blinkFastItem]);
        UpdateBlinkChecks(LoadBlinkPeriod());

        // 言語名はどの言語設定でも読めるよう、それぞれの言語で表記する。
        _languageAutoItem = new ToolStripMenuItem("", null, (_, _) => SetLanguage(Strings.Auto));
        _languageJapaneseItem = new ToolStripMenuItem("日本語", null, (_, _) => SetLanguage(Strings.JapaneseCode));
        _languageEnglishItem = new ToolStripMenuItem("English", null, (_, _) => SetLanguage(Strings.EnglishCode));
        _languageItem = new ToolStripMenuItem("");
        _languageItem.DropDownItems.AddRange([_languageAutoItem, _languageJapaneseItem, _languageEnglishItem]);

        _exitItem = new ToolStripMenuItem("", null, (_, _) => ExitThread());

        _menu = new ContextMenuStrip();
        _menu.Items.Add(_showInCallItem);
        _menu.Items.Add(_showLabelItem);
        _menu.Items.Add(_blinkItem);
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(_languageItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_exitItem);

        _trayIcon = new NotifyIcon
        {
            Icon = TrayIcons.Idle,
            Text = AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };

        _strings = Strings.For(LoadLanguage());
        ApplyLanguage();

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
            SetTrayText($"{AppName}: {_strings.StateError} {ex.Message}");
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
        var encoding = encode > EncodeThresholdPercent;
        if (capturing || encoding)
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

        var s = _strings;
        SetTrayText($"{AppName}: {DescribeState()} ({s.Capture}: {(capturing ? s.Detected : s.NotDetected)} / {s.Encode}: {(encoding ? s.Detected : s.NotDetected)})");

        // 全画面アプリなどに前面を奪われても枠を最前面に戻す。
        // 毎回やるとトレイのメニューより前に出て閉じてしまうため、間隔を空け、メニュー表示中は行わない。
        if (++_ticksSinceTopMost >= TopMostReassertTicks && !_menu.Visible)
        {
            _ticksSinceTopMost = 0;
            foreach (var overlay in _overlays)
            {
                overlay.TopMost = true;
            }
        }
    }

    private void SetTrayText(string text)
    {
        // NotifyIcon の更新はトレイのメニューやオーバーフロー領域を閉じてしまうため、変化したときだけ行う。
        text = Truncate(text);
        if (_trayIcon.Text != text)
        {
            _trayIcon.Text = text;
        }
    }

    private string DescribeState()
    {
        if (_currentStyle == OverlayStyle.Streaming) return _strings.StateStreaming;
        if (_currentStyle == OverlayStyle.InCall) return _strings.StateInCall;
        if (_currentStyle == OverlayStyle.DetectionError) return _strings.StateError;
        return _strings.StateIdle;
    }

    private void ApplyStyle(OverlayStyle? style)
    {
        if (_currentStyle == style)
        {
            return;
        }
        _currentStyle = style;
        _trayIcon.Icon = style == OverlayStyle.Streaming ? TrayIcons.Streaming
            : style is null ? TrayIcons.Idle
            : TrayIcons.InCall;
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
        var blink = _currentStyle == OverlayStyle.Streaming && Blink.PeriodMs > 0;
        foreach (var screen in Screen.AllScreens)
        {
            _overlays.Add(new BorderOverlay(screen, _currentStyle, blink));
            if (_showLabelItem.Checked)
            {
                _overlays.Add(new LabelOverlay(screen, _currentStyle, _strings.LabelFor(_currentStyle), blink));
            }
        }
        foreach (var overlay in _overlays)
        {
            overlay.Show();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => RebuildOverlays();

    private static string Truncate(string text) => text.Length <= 127 ? text : text[..127];

    private static string LoadLanguage()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        return key?.GetValue(LanguageValueName) as string ?? Strings.Auto;
    }

    private void SetLanguage(string language)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
        {
            key.SetValue(LanguageValueName, language);
        }
        _strings = Strings.For(language);
        ApplyLanguage();
        RebuildOverlays();
    }

    private void ApplyLanguage()
    {
        var language = LoadLanguage();
        _showInCallItem.Text = _strings.MenuShowInCall;
        _showLabelItem.Text = _strings.MenuShowLabel;
        _blinkItem.Text = _strings.MenuBlink;
        _blinkOffItem.Text = _strings.MenuBlinkOff;
        _blinkSlowItem.Text = _strings.MenuBlinkSlow;
        _blinkNormalItem.Text = _strings.MenuBlinkNormal;
        _blinkFastItem.Text = _strings.MenuBlinkFast;
        _startupItem.Text = _strings.MenuStartup;
        _languageItem.Text = _strings.MenuLanguage;
        _languageAutoItem.Text = _strings.MenuLanguageAuto;
        _languageAutoItem.Checked = language != Strings.JapaneseCode && language != Strings.EnglishCode;
        _languageJapaneseItem.Checked = language == Strings.JapaneseCode;
        _languageEnglishItem.Checked = language == Strings.EnglishCode;
        _exitItem.Text = _strings.MenuExit;
    }

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

    // 設定は既定でオン。明示的に 0 が保存されているときだけオフ。
    private static bool IsSettingEnabled(string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        return key?.GetValue(valueName) is not int value || value != 0;
    }

    // 点滅は好みが分かれるため既定でオフ。
    private static int LoadBlinkPeriod()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath);
        return key?.GetValue(BlinkPeriodValueName) is int value && value > 0 ? value : 0;
    }

    private void SetBlinkPeriod(int periodMs)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
        {
            key.SetValue(BlinkPeriodValueName, periodMs, RegistryValueKind.DWord);
        }
        UpdateBlinkChecks(periodMs);
        RebuildOverlays();
    }

    private void UpdateBlinkChecks(int periodMs)
    {
        Blink.PeriodMs = periodMs;
        foreach (ToolStripMenuItem item in _blinkItem.DropDownItems)
        {
            item.Checked = (int)item.Tag! == periodMs;
        }
    }

    private static void ToggleSetting(ToolStripMenuItem item, string valueName)
    {
        var enabled = !item.Checked;
        using (var key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
        {
            key.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        item.Checked = enabled;
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
