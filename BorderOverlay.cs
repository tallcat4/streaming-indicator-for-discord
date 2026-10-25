using System.Runtime.InteropServices;

namespace StreamingIndicatorForDiscord;

// 状態ごとの見た目。ラベルの文言は Strings.LabelFor で言語に応じて決める。
// 同じ見た目でも状態を区別できるよう、値ではなく参照で比較する class にしている。
internal sealed class OverlayStyle(Color color, int thickness, Color labelTextColor)
{
    public static readonly OverlayStyle Streaming = new(Color.FromArgb(230, 20, 20), 6, Color.White);
    // 画面共有中に注意するアプリが起動しているとき。枠を太くし、必ず速く点滅させる。
    public static readonly OverlayStyle StreamingWithWatchedApp = new(Color.FromArgb(230, 20, 20), 14, Color.White);
    public static readonly OverlayStyle InCall = new(Color.FromArgb(255, 190, 0), 3, Color.Black);
    public static readonly OverlayStyle DetectionError = new(Color.FromArgb(255, 190, 0), 3, Color.Black);

    public Color Color { get; } = color;
    public int Thickness { get; } = thickness;
    public Color LabelTextColor { get; } = labelTextColor;
}

// 点滅の点灯・消灯。ウィンドウごとにタイマーを持つと切り替えの時刻がずれるため、
// 1 つのタイマーで全モニターの枠とラベルを同時に切り替える。
internal static class Blink
{
    // 周期のうち点灯している割合 (%)。
    private const int OnPercent = 60;

    private static readonly System.Windows.Forms.Timer Timer = new() { Interval = 20 };
    private static event Action? ChangedCore;

    static Blink() => Timer.Tick += (_, _) =>
    {
        var off = Environment.TickCount64 % PeriodMs >= PeriodMs * OnPercent / 100;
        if (off != IsOff)
        {
            IsOff = off;
            ChangedCore?.Invoke();
        }
    };

    public static int PeriodMs { get; set; } = 1000;

    public static bool IsOff { get; private set; }

    public static event Action Changed
    {
        add
        {
            ChangedCore += value;
            Timer.Start();
        }
        remove
        {
            ChangedCore -= value;
            if (ChangedCore is null)
            {
                Timer.Stop();
            }
        }
    }
}

// マウスカーソルの位置。枠ごとにタイマーを持たず、1 つのタイマーで動いたときだけ知らせる。
internal static class CursorWatcher
{
    private static readonly System.Windows.Forms.Timer Timer = new() { Interval = 30 };
    private static event Action? MovedCore;

    static CursorWatcher() => Timer.Tick += (_, _) =>
    {
        var position = Cursor.Position;
        if (position != Position)
        {
            Position = position;
            MovedCore?.Invoke();
        }
    };

    public static Point Position { get; private set; } = Cursor.Position;

    public static event Action Moved
    {
        add
        {
            MovedCore += value;
            Timer.Start();
        }
        remove
        {
            MovedCore -= value;
            if (MovedCore is null)
            {
                Timer.Stop();
            }
        }
    }
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
/// モニター外周の枠の 1 辺。マウスカーソルの周りの円の中だけ枠を薄くし、
/// 枠の下にあるものを見えるようにする（クリックは常に透過する）。
/// ピクセルごとの透明度で描くため、細い 1 辺ずつのウィンドウにして描き直すデータを小さく保つ。
/// </summary>
internal sealed class BorderOverlay : OverlayWindow
{
    // カーソルからこの距離 (96 DPI 換算の px) までは枠を消し、外側の距離にかけて元の濃さに戻す。
    private const int FadeInnerRadius = 150;
    private const int FadeOuterRadius = 450;

    private const byte AC_SRC_OVER = 0x00;
    private const byte AC_SRC_ALPHA = 0x01;
    private const uint ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int cx, int cy)
    {
        public int Cx = cx;
        public int Cy = cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref NativePoint pptDst, ref NativeSize psize,
        IntPtr hdcSrc, ref NativePoint pptSrc, uint crKey, ref BlendFunction pblend, uint dwFlags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);

    private readonly OverlayStyle _style;
    private readonly bool _blink;
    private int[] _solidPixels = [];
    private int[] _pixels = [];
    private IntPtr _memDc;
    private IntPtr _bitmap;
    private IntPtr _oldBitmap;
    private IntPtr _bits;
    private int _innerRadius;
    private int _outerRadius;
    private bool _drawnWithFade;

    private BorderOverlay(Rectangle bounds, OverlayStyle style, bool blink)
    {
        _style = style;
        _blink = blink;
        Bounds = bounds;
    }

    /// <summary>1 枚のモニターの外周 4 辺。</summary>
    public static IEnumerable<BorderOverlay> ForScreen(Screen screen, OverlayStyle style, bool blink)
    {
        var b = screen.Bounds;
        var t = style.Thickness;
        yield return new(new Rectangle(b.Left, b.Top, b.Width, t), style, blink);
        yield return new(new Rectangle(b.Left, b.Bottom - t, b.Width, t), style, blink);
        yield return new(new Rectangle(b.Left, b.Top + t, t, b.Height - 2 * t), style, blink);
        yield return new(new Rectangle(b.Right - t, b.Top + t, t, b.Height - 2 * t), style, blink);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        var scale = DeviceDpi / 96f;
        _innerRadius = (int)(FadeInnerRadius * scale);
        _outerRadius = (int)(FadeOuterRadius * scale);
        CreateBitmap();
        Render();

        CursorWatcher.Moved += OnCursorMoved;
        if (_blink)
        {
            Blink.Changed += Render;
        }
    }

    private void CreateBitmap()
    {
        // 上から下へ並ぶ 32bpp の DIB。ピクセルはアルファ乗算済みの BGRA。
        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = Width,
            Height = -Height,
            Planes = 1,
            BitCount = 32,
        };
        _memDc = CreateCompatibleDC(IntPtr.Zero);
        _bitmap = CreateDIBSection(_memDc, ref header, 0, out _bits, IntPtr.Zero, 0);
        _oldBitmap = SelectObject(_memDc, _bitmap);

        var c = _style.Color;
        _solidPixels = new int[Width * Height];
        Array.Fill(_solidPixels, Premultiply(c, 255));
        _pixels = new int[_solidPixels.Length];
    }

    private static int Premultiply(Color c, int alpha) =>
        alpha << 24 | c.R * alpha / 255 << 16 | c.G * alpha / 255 << 8 | c.B * alpha / 255;

    // カーソルが近いときと、前回フェードを描いたとき（元に戻すため）だけ描き直す。
    private void OnCursorMoved()
    {
        if (_drawnWithFade || DistanceTo(CursorWatcher.Position) < _outerRadius)
        {
            Render();
        }
    }

    private double DistanceTo(Point p)
    {
        var dx = Math.Max(Math.Max(Left - p.X, p.X - (Right - 1)), 0);
        var dy = Math.Max(Math.Max(Top - p.Y, p.Y - (Bottom - 1)), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void Render()
    {
        if (_bits == IntPtr.Zero)
        {
            return;
        }

        var cursor = CursorWatcher.Position;
        _drawnWithFade = DistanceTo(cursor) < _outerRadius;
        if (_blink && Blink.IsOff)
        {
            Array.Clear(_pixels);
        }
        else
        {
            Array.Copy(_solidPixels, _pixels, _pixels.Length);
            if (_drawnWithFade)
            {
                ApplyFade(cursor);
            }
        }
        Marshal.Copy(_pixels, 0, _bits, _pixels.Length);

        var destination = new NativePoint(Left, Top);
        var size = new NativeSize(Width, Height);
        var source = new NativePoint(0, 0);
        var blend = new BlendFunction { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
        UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, _memDc, ref source, 0, ref blend, ULW_ALPHA);
    }

    // カーソルの周りの円の中だけ、距離に応じて透明にする。円にかかる範囲のピクセルだけ計算する。
    private void ApplyFade(Point cursor)
    {
        var w = Width;
        var x0 = Math.Max(0, cursor.X - _outerRadius - Left);
        var x1 = Math.Min(w, cursor.X + _outerRadius - Left + 1);
        var y0 = Math.Max(0, cursor.Y - _outerRadius - Top);
        var y1 = Math.Min(Height, cursor.Y + _outerRadius - Top + 1);
        for (var y = y0; y < y1; y++)
        {
            var dy = Top + y - cursor.Y;
            for (var x = x0; x < x1; x++)
            {
                var dx = Left + x - cursor.X;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance >= _outerRadius)
                {
                    continue;
                }
                var alpha = distance <= _innerRadius ? 0
                    : (int)(255 * (distance - _innerRadius) / (_outerRadius - _innerRadius));
                _pixels[y * w + x] = Premultiply(_style.Color, alpha);
            }
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 表示は UpdateLayeredWindow で行うため、通常の描画はしない。
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CursorWatcher.Moved -= OnCursorMoved;
            if (_blink)
            {
                Blink.Changed -= Render;
            }
        }
        if (_memDc != IntPtr.Zero)
        {
            SelectObject(_memDc, _oldBitmap);
            DeleteObject(_bitmap);
            DeleteDC(_memDc);
            _memDc = IntPtr.Zero;
            _bits = IntPtr.Zero;
        }
        base.Dispose(disposing);
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
    private readonly string _label;
    private readonly Screen _screen;
    private readonly bool _blink;
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = IdleIntervalMs };
    private Font? _font;
    private int _alpha = 255;
    private int _appliedAlpha = -1;

    // WinForms の Opacity は内部でレイヤードスタイルを付け外しするため使わず、直接設定する。
    // レイヤードウィンドウは透明度を一度も設定しないと表示されないので、作成時に必ず設定する。
    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    public LabelOverlay(Screen screen, OverlayStyle style, string label, bool blink)
    {
        _style = style;
        _label = label;
        _screen = screen;
        _blink = blink;
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
        var textSize = TextRenderer.MeasureText(_label, _font);
        var width = textSize.Width + (int)(24 * scale);
        var height = textSize.Height + (int)(8 * scale);
        Bounds = new Rectangle(_screen.Bounds.X + (_screen.Bounds.Width - width) / 2, _screen.Bounds.Y, width, height);

        ApplyAlpha();
        _fadeTimer.Start();
        if (_blink)
        {
            Blink.Changed += ApplyAlpha;
        }
    }

    private void UpdateFade()
    {
        var near = Rectangle.Inflate(Bounds, HoverMargin, HoverMargin).Contains(Cursor.Position);
        var target = near ? 0 : 255;
        if (_alpha != target)
        {
            _alpha = target > _alpha ? Math.Min(target, _alpha + FadeStep) : Math.Max(target, _alpha - FadeStep);
        }
        ApplyAlpha();
        _fadeTimer.Interval = _alpha == target ? IdleIntervalMs : FadingIntervalMs;
    }

    // カーソルによるフェードに、点滅の消灯を重ねる。変化したときだけ設定する。
    private void ApplyAlpha()
    {
        var alpha = _blink && Blink.IsOff ? 0 : _alpha;
        if (alpha != _appliedAlpha)
        {
            _appliedAlpha = alpha;
            SetLayeredWindowAttributes(Handle, 0, (byte)alpha, LWA_ALPHA);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_font is null)
        {
            return;
        }
        TextRenderer.DrawText(e.Graphics, _label, _font, ClientRectangle, _style.LabelTextColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_blink)
            {
                Blink.Changed -= ApplyAlpha;
            }
            _fadeTimer.Dispose();
            _font?.Dispose();
        }
        base.Dispose(disposing);
    }
}
