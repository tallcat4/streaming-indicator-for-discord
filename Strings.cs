using System.Globalization;

namespace StreamingIndicatorForDiscord;

/// <summary>
/// 表示文言。言語設定が "auto" なら Windows の表示言語か地域の書式が日本語のとき日本語、それ以外は英語。
/// </summary>
internal sealed record Strings(
    string StreamingLabel,
    string InCallLabel,
    string DetectionErrorLabel,
    string StateStreaming,
    string StateInCall,
    string StateIdle,
    string StateError,
    string Capture,
    string Encode,
    string Detected,
    string NotDetected,
    string MenuShowInCall,
    string MenuShowLabel,
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
        InCallLabel: "Discord 通話 検出",
        DetectionErrorLabel: "Discord 状態 検出不能",
        StateStreaming: "画面共有 検出",
        StateInCall: "通話 検出",
        StateIdle: "待機中",
        StateError: "検出エラー",
        Capture: "キャプチャ",
        Encode: "エンコード",
        Detected: "あり",
        NotDetected: "なし",
        MenuShowInCall: "通話中も黄色枠を表示",
        MenuShowLabel: "枠にラベル文字を表示",
        MenuStartup: "Windows 起動時に実行",
        MenuLanguage: "言語 (Language)",
        MenuLanguageAuto: "自動",
        MenuExit: "終了");

    public static readonly Strings English = new(
        StreamingLabel: "● Discord screen share detected",
        InCallLabel: "Discord call detected",
        DetectionErrorLabel: "Discord status unknown",
        StateStreaming: "Screen share detected",
        StateInCall: "Call detected",
        StateIdle: "Idle",
        StateError: "Detection error",
        Capture: "capture",
        Encode: "encode",
        Detected: "yes",
        NotDetected: "no",
        MenuShowInCall: "Show yellow border during calls",
        MenuShowLabel: "Show label text on border",
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

    public string LabelFor(OverlayStyle style) =>
        style == OverlayStyle.Streaming ? StreamingLabel
        : style == OverlayStyle.InCall ? InCallLabel
        : DetectionErrorLabel;
}
