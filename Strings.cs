using System.Globalization;

namespace StreamingIndicatorForDiscord;

/// <summary>
/// 表示文言。言語設定が "auto" なら Windows の表示言語か地域の書式が日本語のとき日本語、それ以外は英語。
/// </summary>
internal sealed record Strings(
    string StreamingLabel,
    string WatchedAppLabelFormat,
    string InCallLabel,
    string DetectionErrorLabel,
    string StateStreaming,
    string StateStreamingWithWatchedApp,
    string StateInCall,
    string StateIdle,
    string StateError,
    string Capture,
    string Encode,
    string Detected,
    string NotDetected,
    string MenuShowInCall,
    string MenuShowLabel,
    string MenuBlink,
    string MenuBlinkOff,
    string MenuBlinkSlow,
    string MenuBlinkNormal,
    string MenuBlinkFast,
    string MenuWatchedApps,
    string WatchedAppsDescription,
    string WatchedAppsAddFile,
    string WatchedAppsAddRunning,
    string WatchedAppsRemove,
    string WatchedAppsClose,
    string MenuStartup,
    string MenuLanguage,
    string MenuLanguageAuto,
    string MenuExit)
{
    public const string Auto = "auto";
    public const string JapaneseCode = "ja";
    public const string EnglishCode = "en";

    public static readonly Strings Japanese = new(
        StreamingLabel: "● Discord 画面共有 検出",
        WatchedAppLabelFormat: "● Discord 画面共有 検出 ／ 注意: {0} 起動中",
        InCallLabel: "Discord 通話 検出",
        DetectionErrorLabel: "Discord 状態 検出不能",
        StateStreaming: "画面共有 検出",
        StateStreamingWithWatchedApp: "画面共有 検出・注意アプリ起動中",
        StateInCall: "通話 検出",
        StateIdle: "待機中",
        StateError: "検出エラー",
        Capture: "キャプチャ",
        Encode: "エンコード",
        Detected: "あり",
        NotDetected: "なし",
        MenuShowInCall: "通話中も黄色枠を表示",
        MenuShowLabel: "枠にラベル文字を表示",
        MenuBlink: "画面共有中の点滅",
        MenuBlinkOff: "なし",
        MenuBlinkSlow: "ゆっくり (2 秒)",
        MenuBlinkNormal: "普通 (1 秒)",
        MenuBlinkFast: "速い (0.5 秒)",
        MenuWatchedApps: "注意するアプリ...",
        WatchedAppsDescription: "画面共有中にここに登録したアプリが起動していると、太い枠と速い点滅で強く警告します。",
        WatchedAppsAddFile: "ファイルから追加...",
        WatchedAppsAddRunning: "実行中のアプリから追加 ▼",
        WatchedAppsRemove: "削除",
        WatchedAppsClose: "閉じる",
        MenuStartup: "Windows 起動時に実行",
        MenuLanguage: "言語 (Language)",
        MenuLanguageAuto: "自動",
        MenuExit: "終了");

    public static readonly Strings English = new(
        StreamingLabel: "● Discord screen share detected",
        WatchedAppLabelFormat: "● Discord screen share detected / Caution: {0} running",
        InCallLabel: "Discord call detected",
        DetectionErrorLabel: "Discord status unknown",
        StateStreaming: "Screen share detected",
        StateStreamingWithWatchedApp: "Screen share detected, watched app running",
        StateInCall: "Call detected",
        StateIdle: "Idle",
        StateError: "Detection error",
        Capture: "capture",
        Encode: "encode",
        Detected: "yes",
        NotDetected: "no",
        MenuShowInCall: "Show yellow border during calls",
        MenuShowLabel: "Show label text on border",
        MenuBlink: "Blink during screen share",
        MenuBlinkOff: "Off",
        MenuBlinkSlow: "Slow (2 s)",
        MenuBlinkNormal: "Normal (1 s)",
        MenuBlinkFast: "Fast (0.5 s)",
        MenuWatchedApps: "Watched apps...",
        WatchedAppsDescription: "If any app listed here is running during a screen share, a thicker, fast-blinking border warns you more strongly.",
        WatchedAppsAddFile: "Add from file...",
        WatchedAppsAddRunning: "Add running app ▼",
        WatchedAppsRemove: "Remove",
        WatchedAppsClose: "Close",
        MenuStartup: "Run at Windows startup",
        MenuLanguage: "Language",
        MenuLanguageAuto: "Auto",
        MenuExit: "Exit");

    public static Strings For(string languageSetting) => languageSetting switch
    {
        JapaneseCode => Japanese,
        EnglishCode => English,
        // 英語版 Windows を日本語の地域設定で使う人もいるため、表示言語に加えて地域の書式も見る。
        _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == JapaneseCode
             || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == JapaneseCode ? Japanese : English,
    };

    public string LabelFor(OverlayStyle style, IReadOnlyList<string> watchedApps) =>
        style == OverlayStyle.StreamingWithWatchedApp ? string.Format(WatchedAppLabelFormat, string.Join(", ", watchedApps))
        : style == OverlayStyle.Streaming ? StreamingLabel
        : style == OverlayStyle.InCall ? InCallLabel
        : DetectionErrorLabel;
}
