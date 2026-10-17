using System.Runtime.InteropServices;

namespace DiscordStreamingIndicator;

internal sealed record OverlayStyle(Color Color, int Thickness, string Label, Color LabelTextColor)
{
    public static readonly OverlayStyle Streaming =
        new(Color.FromArgb(230, 20, 20), 6, "● Discord 配信中", Color.White);

    public static readonly OverlayStyle InCall =
        new(Color.FromArgb(255, 190, 0), 3, "Discord 通話中 — 画面共有していないか確認", Color.Black);

    public static readonly OverlayStyle DetectionError =
        new(Color.FromArgb(255, 190, 0), 3, "Discord の配信状態を検出できません", Color.Black);
}

/// <summary>
/// クリック透過・非アクティブ・最前面で、配信の映像には映らないウィンドウの共通部分。
/// </summary>
internal abstract class OverlayWindow : Form
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    protected OverlayWindow()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        DoubleBuffered = true;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // 配信の映像にはこのウィンドウを映さない（本人にだけ見える）。
        SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
    }
}

/// <summary>
/// 1 枚のモニターの外周に枠を描く。
/// </summary>
internal sealed class BorderOverlay : OverlayWindow
{
    private static readonly Color TransparentColor = Color.FromArgb(1, 2, 3);

    private readonly OverlayStyle _style;

    public BorderOverlay(Screen screen, OverlayStyle style)
    {
        _style = style;
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        Bounds = screen.Bounds;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var w = ClientSize.Width;
        var h = ClientSize.Height;
        var t = _style.Thickness;

        using var brush = new SolidBrush(_style.Color);
        g.FillRectangle(brush, 0, 0, w, t);
        g.FillRectangle(brush, 0, h - t, w, t);
        g.FillRectangle(brush, 0, 0, t, h);
        g.FillRectangle(brush, w - t, 0, t, h);
    }
}

/// <summary>
/// モニター上辺中央のラベル。マウスカーソルが近づくとフェードアウトし、
/// 下にあるブラウザのタブなどを見えるようにする（クリックは常に透過する）。
/// </summary>
internal sealed class LabelOverlay : OverlayWindow
{
    // カーソルがラベルからこの距離 (px) 以内に来たら消す。
    private const int HoverMargin = 120;
    private const int FadeStep = 90;
    // 普段は控えめな間隔でカーソル位置を確認し、フェード中だけ滑らかに動かす。
    private const int IdleIntervalMs = 100;
    private const int FadingIntervalMs = 30;
    private const uint LWA_ALPHA = 0x00000002;

    private readonly OverlayStyle _style;
    private readonly Screen _screen;
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = IdleIntervalMs };
    private Font? _font;
    private int _alpha = 255;

    // WinForms の Opacity は内部でレイヤードスタイルを付け外しするため使わず、直接設定する。
    // レイヤードウィンドウは透明度を一度も設定しないと表示されないので、作成時に必ず設定する。
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    public LabelOverlay(Screen screen, OverlayStyle style)
    {
        _style = style;
        _screen = screen;
        BackColor = style.Color;
        // ハンドル作成時にモニターの DPI で大きさを決めるため、まずは対象モニター上に置いておく。
        Bounds = new Rectangle(screen.Bounds.X + screen.Bounds.Width / 2, screen.Bounds.Y, 1, 1);
        _fadeTimer.Tick += (_, _) => UpdateFade();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var scale = DeviceDpi / 96f;
        _font = new Font("Yu Gothic UI", 15f * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        var textSize = TextRenderer.MeasureText(_style.Label, _font);
        var width = textSize.Width + (int)(24 * scale);
        var height = textSize.Height + (int)(8 * scale);
        Bounds = new Rectangle(_screen.Bounds.X + (_screen.Bounds.Width - width) / 2, _screen.Bounds.Y, width, height);

        SetLayeredWindowAttributes(Handle, 0, (byte)_alpha, LWA_ALPHA);
        _fadeTimer.Start();
    }

    private void UpdateFade()
    {
        var near = Rectangle.Inflate(Bounds, HoverMargin, HoverMargin).Contains(Cursor.Position);
        var target = near ? 0 : 255;
        if (_alpha != target)
        {
            _alpha = target > _alpha ? Math.Min(target, _alpha + FadeStep) : Math.Max(target, _alpha - FadeStep);
            SetLayeredWindowAttributes(Handle, 0, (byte)_alpha, LWA_ALPHA);
        }
        _fadeTimer.Interval = _alpha == target ? IdleIntervalMs : FadingIntervalMs;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_font is null)
        {
            return;
        }
        TextRenderer.DrawText(e.Graphics, _style.Label, _font, ClientRectangle, _style.LabelTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fadeTimer.Dispose();
            _font?.Dispose();
        }
        base.Dispose(disposing);
    }
}
