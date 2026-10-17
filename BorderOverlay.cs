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
/// 1 枚のモニターの外周に枠を描く、クリック透過の最前面ウィンドウ。
/// </summary>
internal sealed class BorderOverlay : Form
{
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private static readonly Color TransparentColor = Color.FromArgb(1, 2, 3);

    private readonly OverlayStyle _style;
    private readonly Font _labelFont = new("Yu Gothic UI", 11f, FontStyle.Bold);

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    public BorderOverlay(Screen screen, OverlayStyle style)
    {
        _style = style;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        BackColor = TransparentColor;
        TransparencyKey = TransparentColor;
        Bounds = screen.Bounds;
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
        // 配信の映像にはこの枠を映さない（本人にだけ見える）。
        SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
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

        // 上辺中央のラベル。TransparencyKey と混ざらないようアンチエイリアスは使わない。
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
        var textSize = g.MeasureString(_style.Label, _labelFont);
        var labelWidth = (int)textSize.Width + 24;
        var labelHeight = (int)textSize.Height + 8;
        var labelX = (w - labelWidth) / 2;
        g.FillRectangle(brush, labelX, 0, labelWidth, labelHeight);
        using var textBrush = new SolidBrush(_style.LabelTextColor);
        g.DrawString(_style.Label, _labelFont, textBrush, labelX + 12, 4);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }
}
