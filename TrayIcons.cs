using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace DiscordStreamingIndicator;

/// <summary>
/// 状態ごとのトレイアイコン（モニターの形）。app.ico も同じデザインで
/// tools/make-icon.ps1 から生成している。
/// </summary>
internal static class TrayIcons
{
    public static readonly Icon Idle = Create(Color.FromArgb(150, 150, 150), filled: false);
    public static readonly Icon InCall = Create(Color.FromArgb(255, 190, 0), filled: false);
    public static readonly Icon Streaming = Create(Color.FromArgb(230, 20, 20), filled: true);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Icon Create(Color color, bool filled)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(color);
            using var pen = new Pen(color, 3f);

            // 画面部分
            using var screen = RoundedRect(new RectangleF(2.5f, 3.5f, 27f, 19f), 3f);
            if (filled)
            {
                g.FillPath(brush, screen);
            }
            g.DrawPath(pen, screen);

            // スタンド
            g.FillRectangle(brush, 14, 23, 4, 4);
            g.FillRectangle(brush, 9, 27, 14, 3);

            // 「配信中」を示す点
            if (filled)
            {
                g.FillEllipse(Brushes.White, 11f, 8f, 10f, 10f);
            }
        }

        // GetHicon で作ったハンドルは Icon が所有しないため、複製してから解放する。
        var handle = bitmap.GetHicon();
        try
        {
            return (Icon)Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
