using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace StreamingIndicatorForDiscord;

/// <summary>
/// 画面共有中に起動していたら強く警告するアプリの一覧を編集する。
/// 一覧そのものが知られたくない情報になりうるため、この画面も配信の映像には映さない。
/// 登録内容は exe のフルパス（古い形式では拡張子なしの exe 名）。照合は exe 名で行うので、
/// 同じ名前の別のアプリ（例: Chromium 系ブラウザの chrome.exe）も対象になる＝警告側に倒れる。
/// </summary>
internal sealed class WatchedAppsForm : Form
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private readonly ListView _list;
    private readonly ImageList _icons;
    private readonly ContextMenuStrip _runningMenu = new();
    private readonly Action<IReadOnlyList<string>> _onChanged;

    public WatchedAppsForm(Strings s, string title, IEnumerable<string> apps, Action<IReadOnlyList<string>> onChanged)
    {
        _onChanged = onChanged;
        // 実行中のアプリの一覧も別ウィンドウなので、同じく映さない。
        _runningMenu.HandleCreated += (_, _) => SetWindowDisplayAffinity(_runningMenu.Handle, WDA_EXCLUDEFROMCAPTURE);

        Text = title;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        ClientSize = new Size(460, 360);
        MinimumSize = new Size(360, 280);
        Padding = new Padding(10);

        var description = new Label
        {
            Text = s.WatchedAppsDescription,
            Dock = DockStyle.Fill,
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Margin = new Padding(0, 0, 0, 8),
        };

        var iconSize = (int)(16 * DeviceDpi / 96f);
        _icons = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(iconSize, iconSize) };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = true,
            MultiSelect = true,
            Sorting = SortOrder.Ascending,
            SmallImageList = _icons,
        };
        _list.Columns.Add("");
        _list.Resize += (_, _) => _list.Columns[0].Width = _list.ClientSize.Width;
        foreach (var app in apps)
        {
            AddItem(app);
        }
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
            }
        };

        var addFile = new Button { Text = s.WatchedAppsAddFile, AutoSize = true };
        addFile.Click += (_, _) => AddFromFile();
        var addRunning = new Button { Text = s.WatchedAppsAddRunning, AutoSize = true };
        addRunning.Click += (_, _) => ShowRunningApps(addRunning);
        var remove = new Button { Text = s.WatchedAppsRemove, AutoSize = true };
        remove.Click += (_, _) => RemoveSelected();
        var close = new Button { Text = s.WatchedAppsClose, AutoSize = true };
        close.Click += (_, _) => Close();
        CancelButton = close;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.AddRange([addFile, addRunning, remove, close]);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(description, 0, 0);
        layout.Controls.Add(_list, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
    }

    /// <summary>実行中のアプリの一覧を開いているか。開いている間は枠を最前面に戻し直さない（一覧が閉じてしまうため）。</summary>
    public bool IsMenuOpen => _runningMenu.Visible;

    /// <summary>登録内容から、照合に使う拡張子なしの exe 名を取り出す。</summary>
    public static string ProcessNameOf(string entry) =>
        entry.Contains('\\') ? Path.GetFileNameWithoutExtension(entry) : entry;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetWindowDisplayAffinity(Handle, WDA_EXCLUDEFROMCAPTURE);
    }

    private IEnumerable<string> Apps => _list.Items.Cast<ListViewItem>().Select(item => (string)item.Tag!);

    // exe の説明（例: "Thorium"）と exe 名を並べ、同じ名前の別アプリを見分けられるようにする。
    private static string DisplayNameOf(string entry)
    {
        if (!entry.Contains('\\'))
        {
            return entry;
        }
        var fileName = Path.GetFileName(entry);
        try
        {
            var info = FileVersionInfo.GetVersionInfo(entry);
            var description = string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                return $"{description.Trim()} ({fileName})";
            }
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            // アンインストール済みなどで読めなければファイル名だけ出す。
        }
        return fileName;
    }

    private static Bitmap? IconOf(string entry)
    {
        if (!entry.Contains('\\'))
        {
            return null;
        }
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(entry);
            return icon?.ToBitmap();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void AddItem(string entry)
    {
        var item = new ListViewItem(DisplayNameOf(entry)) { Tag = entry };
        if (IconOf(entry) is { } icon)
        {
            _icons.Images.Add(entry, icon);
            item.ImageKey = entry;
        }
        _list.Items.Add(item);
    }

    private void Add(string entry)
    {
        if (!Apps.Contains(entry, StringComparer.OrdinalIgnoreCase))
        {
            AddItem(entry);
            _onChanged([.. Apps]);
        }
    }

    private void AddFromFile()
    {
        using var dialog = new OpenFileDialog { Filter = "*.exe|*.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Add(dialog.FileName);
        }
    }

    // 実行中のプロセスの exe のパス。管理者として動くプロセスでも読める方法を使う。
    private static string? ImagePathOf(Process process)
    {
        var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
        if (handle == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString(0, size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    // ウィンドウを持つ実行中のアプリを、exe の場所ごとに一覧にして選ばせる。
    private void ShowRunningApps(Control anchor)
    {
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var self = Environment.ProcessId;
        foreach (var process in Process.GetProcesses())
        {
            if (process.Id != self && process.MainWindowHandle != IntPtr.Zero)
            {
                entries.Add(ImagePathOf(process) ?? process.ProcessName);
            }
            process.Dispose();
        }
        entries.ExceptWith(Apps);

        foreach (ToolStripItem old in _runningMenu.Items)
        {
            old.Image?.Dispose();
        }
        _runningMenu.Items.Clear();
        foreach (var (entry, name) in entries.Select(entry => (entry, DisplayNameOf(entry)))
                     .OrderBy(pair => pair.Item2, StringComparer.CurrentCultureIgnoreCase))
        {
            _runningMenu.Items.Add(name, IconOf(entry), (_, _) => Add(entry));
        }
        _runningMenu.Show(anchor, new Point(0, anchor.Height));
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItems.Count == 0)
        {
            return;
        }
        foreach (var item in _list.SelectedItems.Cast<ListViewItem>().ToList())
        {
            _list.Items.Remove(item);
        }
        _onChanged([.. Apps]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (ToolStripItem item in _runningMenu.Items)
            {
                item.Image?.Dispose();
            }
            _runningMenu.Dispose();
            _icons.Dispose();
        }
        base.Dispose(disposing);
    }
}
