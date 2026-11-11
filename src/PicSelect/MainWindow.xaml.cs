using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using PicSelect.Core;

namespace PicSelect;

public partial class MainWindow : Window
{
    private readonly record struct Snapshot(int Cursor, string[] Kept, string[] Seen);

    private List<string> _files = new();
    private readonly HashSet<string> _kept = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly Stack<Snapshot> _undo = new();
    private readonly DispatcherTimer _saveTimer = new();
    private readonly DispatcherTimer _fullTimer = new();
    private readonly DispatcherTimer _statTimer = new();

    private string _folder = "";
    private int _cursor = -1;
    private ImageCache? _cache;
    private int _fullIndex = -1;

    /// <summary>最近一次删除的张数，用于语言切换后重绘「筛选完成」页。 -1 表示不在完成态。</summary>
    private int _doneCount = -1;
    private bool _langReady;

    /// <summary>测试用：跳过恢复确认弹窗。</summary>
    public bool AutoRestore { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        ApplyScreenAdaptiveSize();

        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };

        _fullTimer.Interval = TimeSpan.FromMilliseconds(260);
        _fullTimer.Tick += OnFullTimerTick;

        _statTimer.Interval = TimeSpan.FromMilliseconds(500);
        _statTimer.Tick += (_, _) => UpdateCacheStat();
        _statTimer.Start();

        LangCombo.ItemsSource = Loc.DisplayNames;
        LangCombo.SelectedIndex = (int)Loc.Current;
        _langReady = true;

        ApplyLanguage();
        Loc.Changed += ApplyLanguage;

        Viewer.ZoomChanged += (_, _) => MaybeLoadFull();
        Loaded += OnLoaded;
    }

    private void OnLangChanged(object? sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_langReady) return;
        if (LangCombo.SelectedIndex < 0) return;
        Loc.Set((Lang)LangCombo.SelectedIndex);
    }

    /// <summary>把当前语言应用到所有界面文案。切换语言时整体重刷一遍。</summary>
    private void ApplyLanguage()
    {
        Title = _folder.Length > 0
            ? Loc.T("app.titleFolder", Loc.T("app.title"), Path.GetFileName(_folder))
            : Loc.T("app.title");

        BtnOpen.Content = Loc.T("top.open");
        BtnJump.Content = Loc.T("top.jump");
        BtnRestart.Content = Loc.T("top.restart");
        BtnHelp.Content = Loc.T("top.help");
        BtnExport.Content = Loc.T("top.export");
        BtnDelete.Content = Loc.T("top.delete");

        TxtKeepTitle.Text = Loc.T("btn.keep");
        TxtKeepHint.Text = Loc.T("hint.keep");
        TxtSkipTitle.Text = Loc.T("btn.skip");
        TxtSkipHint.Text = Loc.T("hint.skip");
        TxtPrevTitle.Text = Loc.T("btn.prev");
        TxtPrevHint.Text = Loc.T("hint.prev");
        TxtMarkHint.Text = Loc.T("hint.mark");
        // 标题本身也要在这里刷：没打开文件夹时 UpdateInfo 不会被调用，
        // 否则这个按钮会一直停在上一门语言。
        TxtMarkTitle.Text = IsCurrentMarked() ? Loc.T("btn.unmark") : Loc.T("btn.mark");
        TxtPrevMarkTitle.Text = Loc.T("btn.prevMarked");
        TxtPrevMarkHint.Text = Loc.T("hint.prevMarked");
        TxtNextMarkTitle.Text = Loc.T("btn.nextMarked");
        TxtNextMarkHint.Text = Loc.T("hint.nextMarked");
        TxtUndo.Text = $"{Loc.T("btn.undo")}  {Loc.T("hint.undo")}";
        TxtBadge.Text = Loc.T("badge.marked");

        if (BusyBadge.Visibility == Visibility.Visible)
            TxtBusy.Text = Loc.T(_busyKey);

        // 空状态 / 完成态的静态提示
        if (_doneCount >= 0)
        {
            TxtHint.Text = Loc.T("hint.done.title");
            TxtHintSub.Text = Loc.T("hint.done.sub", _doneCount);
        }
        else if (_files.Count == 0)
        {
            TxtHint.Text = Loc.T("hint.drop");
            TxtHintSub.Text = "";
        }

        if (_files.Count > 0 && _cursor >= 0)
        {
            UpdateInfo();
            UpdateStatus();
        }
    }

    /// <summary>
    /// 小屏笔记本（1366×768、1280×720）上固定 1320×880 会顶到任务栏下面，
    /// 启动时按工作区收一次。
    /// </summary>
    private void ApplyScreenAdaptiveSize()
    {
        var wa = SystemParameters.WorkArea;
        double w = Math.Min(1320, wa.Width - 32);
        double h = Math.Min(880, wa.Height - 32);

        Width = Math.Max(MinWidth, w);
        Height = Math.Max(MinHeight, h);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    // ────────────────────────────── 启动 / 会话恢复 ──────────────────────────────

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        var st = SessionStore.Load();
        if (st is null) return;

        if (!Directory.Exists(st.Folder))
        {
            SessionStore.Clear();
            return;
        }

        var onDisk = FileScanner.Scan(st.Folder);
        var savedNames = new HashSet<string>(st.Files, StringComparer.OrdinalIgnoreCase);

        // _files 全程必须是完整路径。会话里只存了文件名，所以要拿磁盘扫描结果
        // （带目录）按文件名过滤回来，不能直接把 st.Files 赋给 _files。
        var survivors = onDisk.Where(f => savedNames.Contains(Path.GetFileName(f) ?? f)).ToList();
        var diskSet = new HashSet<string>(
            survivors.Select(f => Path.GetFileName(f) ?? f), StringComparer.OrdinalIgnoreCase);
        if (survivors.Count == 0)
        {
            SessionStore.Clear();
            return;
        }

        MessageBoxResult answer;
        if (AutoRestore)
        {
            answer = MessageBoxResult.Yes;
        }
        else
        {
            answer = MessageBox.Show(
                Loc.T("dlg.restore.body",
                    st.Folder,
                    Math.Min(st.Cursor + 1, survivors.Count),
                    survivors.Count,
                    st.Kept.Count),
                Loc.T("dlg.restore.title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.Yes);
        }

        if (answer != MessageBoxResult.Yes)
        {
            SessionStore.Clear();
            return;
        }

        _folder = st.Folder;
        _files = survivors;
        _kept.Clear();
        foreach (var k in st.Kept) if (diskSet.Contains(k)) _kept.Add(k);
        _seen.Clear();
        foreach (var k in st.Seen) if (diskSet.Contains(k)) _seen.Add(k);
        _undo.Clear();

        EnsureCache();
        _doneCount = -1;
        ApplyLanguage();
        HintPanel.Visibility = Visibility.Collapsed;
        ShowIndex(Math.Clamp(st.Cursor, 0, _files.Count - 1));
    }

    // ────────────────────────────── 打开文件夹 ──────────────────────────────

    private void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("dlg.openFolder.title") };
        if (dlg.ShowDialog(this) == true)
            OpenFolder(dlg.FolderName);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        var p = paths[0];
        if (Directory.Exists(p)) OpenFolder(p);
        else if (File.Exists(p)) OpenFolder(Path.GetDirectoryName(p)!);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;

        List<string> files;
        try { files = FileScanner.Scan(folder); }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.T("err.scan", ex.Message), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (files.Count == 0)
        {
            MessageBox.Show(Loc.T("err.noImages"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _cache?.Dispose();
        _cache = null;

        _files = files;
        _folder = folder;
        _kept.Clear();
        _seen.Clear();
        _undo.Clear();
        _fullIndex = -1;
        _cursor = 0;

        EnsureCache();
        _doneCount = -1;
        ApplyLanguage();
        HintPanel.Visibility = Visibility.Collapsed;

        ShowIndex(0);
    }

    private void EnsureCache()
    {
        double w = ActualWidth > 100 ? ActualWidth : 1400;
        double h = ActualHeight > 100 ? ActualHeight : 900;
        var dpi = VisualTreeHelper.GetDpi(this);
        int pw = Math.Max(800, (int)(w * dpi.DpiScaleX * 1.3));
        int ph = Math.Max(600, (int)(h * dpi.DpiScaleY * 1.3));
        _cache = new ImageCache(_files, pw, ph);
    }

    private void OnRestart(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) { OnOpenFolder(sender, e); return; }

        var r = MessageBox.Show(
            Loc.T("dlg.restart.body"),
            Loc.T("dlg.restart.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (r != MessageBoxResult.Yes) return;

        _kept.Clear();
        _seen.Clear();
        _undo.Clear();
        SaveNow();
        ShowIndex(0);
    }

    // ────────────────────────────── 显示 ──────────────────────────────

    private async void ShowIndex(int idx)
    {
        if (_cache is null || idx < 0 || idx >= _files.Count) return;

        _cursor = idx;
        _fullIndex = -1;

        UpdateInfo();
        UpdateStatus();
        _cache.SetCursor(idx);
        ScheduleSave();

        var cached = _cache.TryGet(idx);
        if (cached is not null)
        {
            BusyBadge.Visibility = Visibility.Collapsed;
            Viewer.SetImage(cached.Bitmap);
        }
        else
        {
            ShowBusy("busy.loading");
        }

        if (cached is null)
        {
            var d = await _cache.GetAsync(idx);
            if (d is null)
            {
                if (_cursor == idx)
                    ShowBusy(File.Exists(_files[idx]) ? "busy.decode" : "busy.gone");
                return;
            }
            if (_cursor != idx) return;   // 用户已经翻页，丢弃过期结果
            BusyBadge.Visibility = Visibility.Collapsed;
            Viewer.SetImage(d.Bitmap);
        }

        MaybeLoadFull();
    }

    private string _busyKey = "busy.loading";

    private void ShowBusy(string key)
    {
        _busyKey = key;
        TxtBusy.Text = Loc.T(key);
        BusyBadge.Visibility = Visibility.Visible;
    }

    /// <summary>当前这张是否已标记。没有图片时恒为 false。</summary>
    private bool IsCurrentMarked() =>
        _cursor >= 0 && _cursor < _files.Count && _kept.Contains(Path.GetFileName(_files[_cursor]));

    private void UpdateInfo()
    {
        if (_cursor < 0 || _cursor >= _files.Count) return;

        var path = _files[_cursor];
        var name = Path.GetFileName(path);
        bool marked = _kept.Contains(name);

        TxtFile.Text = name;
        TxtPos.Text = $"{_cursor + 1} / {_files.Count}";

        string size = "";
        try
        {
            var fi = new FileInfo(path);
            if (fi.Exists) size = fi.Length >= 1024 * 1024
                ? $"{fi.Length / 1024.0 / 1024.0:F1} MB"
                : $"{fi.Length / 1024.0:F0} KB";
        }
        catch { }

        TxtMeta.Text = marked ? Loc.T("meta.marked", size) : size;
        TxtMeta.Foreground = marked
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8F, 0xC0, 0x62))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x7A, 0x7A));

        TxtMarkTitle.Text = marked ? Loc.T("btn.unmark") : Loc.T("btn.mark");
        MarkBadge.Visibility = marked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatus()
    {
        if (_files.Count == 0) return;

        TxtProgress.Text = Loc.T("status.processed", _seen.Count, _files.Count);
        TxtStatus.Text = Loc.T("status.kept", _kept.Count, _files.Count - _kept.Count);

        double ratio = _files.Count == 0 ? 0 : (double)_seen.Count / _files.Count;
        ratio = Math.Clamp(ratio, 0, 1);
        ProgDone.Width = new GridLength(ratio, GridUnitType.Star);
        ProgRest.Width = new GridLength(Math.Max(0.0001, 1 - ratio), GridUnitType.Star);

        UpdateCacheStat();
    }

    private void UpdateCacheStat()
    {
        if (_cache is null) { TxtCache.Text = ""; return; }
        TxtCache.Text = Loc.T("status.cache", _cache.CachedCount, _cache.CachedBytes / 1024 / 1024);
    }

    // ────────────────────────────── 高清按需加载 ──────────────────────────────

    private void MaybeLoadFull()
    {
        if (_cursor < 0 || _cache is null) return;
        _fullTimer.Stop();
        _fullTimer.Start();
    }

    private async void OnFullTimerTick(object? sender, EventArgs e)
    {
        _fullTimer.Stop();
        if (_cache is null || _cursor < 0) return;
        if (_fullIndex == _cursor) return;
        if (Viewer.ZoomLevel <= 1.05) return;

        int idx = _cursor;
        var preview = _cache.TryGet(idx);
        if (preview is null || preview.IsFullResolution) return;

        var full = await _cache.GetFullAsync(idx);
        if (full is null || _cursor != idx) return;

        _fullIndex = idx;
        if (Viewer.IsFitMode) Viewer.SetImage(full.Bitmap);
        else Viewer.ReplaceSource(full.Bitmap);
    }

    // ────────────────────────────── 操作 ──────────────────────────────

    private void PushUndo()
    {
        if (_cursor < 0) return;
        _undo.Push(new Snapshot(_cursor, _kept.ToArray(), _seen.ToArray()));
        while (_undo.Count > 60)
        {
            var arr = _undo.ToArray();
            _undo.Clear();
            for (int i = arr.Length - 2; i >= 0; i--) _undo.Push(arr[i]);
        }
    }

    private void OnKeepNext(object? sender, RoutedEventArgs e)
    {
        if (_cursor < 0) return;
        PushUndo();
        var n = Path.GetFileName(_files[_cursor]);
        _kept.Add(n);
        _seen.Add(n);
        GoNext();
    }

    private void OnSkip(object? sender, RoutedEventArgs e)
    {
        if (_cursor < 0) return;
        PushUndo();
        _seen.Add(Path.GetFileName(_files[_cursor]));
        GoNext();
    }

    private void OnPrev(object? sender, RoutedEventArgs e)
    {
        if (_cursor <= 0) return;
        PushUndo();
        ShowIndex(_cursor - 1);
    }

    // ────────────────────────── 在已标记的图片之间跳转 ──────────────────────────

    private void OnPrevMarked(object? sender, RoutedEventArgs e) => JumpToMarked(-1);
    private void OnNextMarked(object? sender, RoutedEventArgs e) => JumpToMarked(+1);

    private int PrevMarkedIndex(int from)
    {
        for (int i = Math.Min(from, _files.Count) - 1; i >= 0; i--)
            if (_kept.Contains(Path.GetFileName(_files[i]))) return i;
        return -1;
    }

    private int NextMarkedIndex(int from)
    {
        for (int i = from + 1; i < _files.Count; i++)
            if (_kept.Contains(Path.GetFileName(_files[i]))) return i;
        return -1;
    }

    /// <summary>最后一张处理过的图片（标记或跳过过）。</summary>
    private int LastSeenIndex()
    {
        for (int i = _files.Count - 1; i >= 0; i--)
            if (_seen.Contains(Path.GetFileName(_files[i]))) return i;
        return -1;
    }

    private void JumpToMarked(int dir)
    {
        if (_files.Count == 0 || _cursor < 0) return;

        int target;
        string note = "";

        if (dir < 0)
        {
            target = PrevMarkedIndex(_cursor);
            if (target < 0)
            {
                target = 0;                       // 前面没有已标记 → 回到第一张
                note = Loc.T("nav.noPrev");
            }
        }
        else
        {
            target = NextMarkedIndex(_cursor);
            if (target < 0)
            {
                target = LastSeenIndex();         // 后面没有已标记 → 最后一张处理过的
                if (target < 0)
                {
                    TxtStatus.Text = Loc.T("nav.noNextNone");
                    return;
                }
                note = Loc.T("nav.noNext");
            }
        }

        if (target == _cursor)
        {
            TxtStatus.Text = Loc.T(dir < 0 ? "nav.atFirst" : "nav.atLast");
            return;
        }

        PushUndo();
        ShowIndex(target);
        if (note.Length > 0) TxtStatus.Text = note;
    }

    private void OnToggleMark(object? sender, RoutedEventArgs e)
    {
        if (_cursor < 0) return;
        PushUndo();
        var n = Path.GetFileName(_files[_cursor]);
        if (!_kept.Remove(n)) _kept.Add(n);
        _seen.Add(n);
        UpdateInfo();
        UpdateStatus();
        ScheduleSave();
    }

    private void OnUndo(object? sender, RoutedEventArgs e)
    {
        if (_undo.Count == 0) return;
        var snap = _undo.Pop();

        _kept.Clear();
        foreach (var k in snap.Kept) _kept.Add(k);
        _seen.Clear();
        foreach (var k in snap.Seen) _seen.Add(k);

        ShowIndex(Math.Clamp(snap.Cursor, 0, _files.Count - 1));
    }

    private void GoNext()
    {
        if (_cursor + 1 < _files.Count)
        {
            ShowIndex(_cursor + 1);
            return;
        }

        // 已经是最后一张：不换图，但这一张的标记状态刚刚变过，
        // 必须重刷信息栏和徽章，否则点了「保留」界面看起来毫无反应。
        UpdateInfo();
        UpdateStatus();
        TxtStatus.Text = Loc.T("status.lastImage");
    }

    // ────────────────────────────── 跳转 ──────────────────────────────

    private void OnJump(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        var dlg = new JumpDialog(_files.Count, _cursor + 1) { Owner = this };
        if (dlg.ShowDialog() == true) JumpTo(dlg.Query);
    }

    private void JumpTo(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return;

        if (int.TryParse(query, out int n) && n >= 1 && n <= _files.Count)
        {
            ShowIndex(n - 1);
            return;
        }

        var pattern = query.Contains('*') || query.Contains('?') ? query : $"*{query}*";
        Regex rx;
        try { rx = WildcardToRegex(pattern); }
        catch { return; }

        for (int step = 1; step <= _files.Count; step++)
        {
            int i = (_cursor + step) % _files.Count;
            if (rx.IsMatch(Path.GetFileName(_files[i])))
            {
                ShowIndex(i);
                return;
            }
        }

        MessageBox.Show(Loc.T("jump.notFound", query), Loc.T("jump.title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static Regex WildcardToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (var c in pattern)
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    // ────────────────────────────── 删除 ──────────────────────────────

    private async void OnDeleteUnmarked(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;

        var victims = _files.Where(f => !_kept.Contains(Path.GetFileName(f))).ToList();
        if (victims.Count == 0)
        {
            MessageBox.Show(Loc.T("dlg.delete.none"), Loc.T("top.delete"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var answer = MessageBox.Show(
            Loc.T("dlg.delete.body", victims.Count, _kept.Count),
            Loc.T("dlg.delete.title"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes) return;

        await DeleteFilesAsync(victims);
    }

    private async Task DeleteFilesAsync(List<string> victims)
    {
        // 先停掉解码线程，确保没有句柄在读这些文件
        _cache?.Dispose();
        _cache = null;
        Viewer.SetImage(null);
        _fullIndex = -1;

        int ok = 0;
        var failures = new List<string>();

        for (int i = 0; i < victims.Count; i++)
        {
            TxtStatus.Text = Loc.T("del.progress", i + 1, victims.Count);
            if (Trash.SendToRecycleBin(victims[i], out var err)) ok++;
            else failures.Add($"{Path.GetFileName(victims[i])} — {err}");

            if (i % 16 == 0)
                await Dispatcher.Yield(DispatcherPriority.Background);
        }

        var deletedSet = new HashSet<string>(
            victims.Except(failures.Select(f => f.Split(" — ")[0]).Select(x => victims.First(v => Path.GetFileName(v) == x))),
            StringComparer.OrdinalIgnoreCase);

        // 从列表中移除真正删掉的文件
        var stillThere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var v in victims)
            if (File.Exists(v)) stillThere.Add(v);
        _files = _files.Where(f => !(victims.Contains(f) && !stillThere.Contains(f))).ToList();

        if (failures.Count == 0)
        {
            SessionStore.Clear();
            _kept.Clear();
            _seen.Clear();
            _undo.Clear();
            _cursor = -1;
            _files.Clear();

            _doneCount = ok;
            HintPanel.Visibility = Visibility.Visible;
            TxtHint.Text = Loc.T("hint.done.title");
            TxtHintSub.Text = Loc.T("hint.done.sub", ok);
            TxtFile.Text = "";
            TxtMeta.Text = "";
            TxtPos.Text = "";
            TxtProgress.Text = "";
            TxtStatus.Text = "";
            TxtCache.Text = "";
            ApplyLanguage();
            return;
        }

        // 部分失败：保留状态，让用户处理剩余
        var detail = string.Join("\n", failures.Take(12));
        if (failures.Count > 12) detail += "\n" + Loc.T("del.more", failures.Count - 12);

        MessageBox.Show(
            Loc.T("del.partial.body", ok, failures.Count, detail),
            Loc.T("del.partial.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        if (_files.Count > 0)
        {
            EnsureCache();
            ShowIndex(Math.Clamp(_cursor, 0, _files.Count - 1));
        }
        SaveNow();
    }

    private void OnExportKeep(object? sender, RoutedEventArgs e)
    {
        if (_files.Count == 0) return;
        var path = SessionStore.ExportKeepList(_folder, _kept.OrderBy(x => x, NaturalComparer.Instance));
        if (path is null)
        {
            MessageBox.Show(Loc.T("export.fail"), Loc.T("export.title"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        MessageBox.Show(Loc.T("export.ok", path), Loc.T("export.title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ────────────────────────────── 键盘 ──────────────────────────────

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is System.Windows.Controls.TextBox) return;

        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.Z: OnUndo(sender, e); e.Handled = true; break;
                case Key.G: OnJump(sender, e); e.Handled = true; break;
                case Key.O: OnOpenFolder(sender, e); e.Handled = true; break;
                case Key.Delete: OnDeleteUnmarked(sender, e); e.Handled = true; break;
                case Key.Left: JumpToMarked(-1); e.Handled = true; break;
                case Key.Right: JumpToMarked(+1); e.Handled = true; break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.Space: OnKeepNext(sender, e); e.Handled = true; break;
            case Key.D:
            case Key.Right:
            case Key.PageDown: OnSkip(sender, e); e.Handled = true; break;
            case Key.A:
            case Key.Left:
            case Key.PageUp: OnPrev(sender, e); e.Handled = true; break;
            case Key.S: OnToggleMark(sender, e); e.Handled = true; break;
            case Key.OemOpenBrackets: JumpToMarked(-1); e.Handled = true; break;
            case Key.OemCloseBrackets: JumpToMarked(+1); e.Handled = true; break;
            case Key.Home: if (_files.Count > 0) { ShowIndex(0); e.Handled = true; } break;
            case Key.End: if (_files.Count > 0) { ShowIndex(_files.Count - 1); e.Handled = true; } break;
            case Key.F:
            case Key.Enter:
                // 部分笔记本把 F1-F12 交给 Fn 组合键，Enter 更稳，所以主推 Enter
                if (Viewer.IsFitMode) Viewer.ActualSize(); else Viewer.FitToWindow();
                e.Handled = true;
                break;
            case Key.D0:
            case Key.NumPad0: Viewer.ActualSize(); e.Handled = true; break;
        }
    }

    private void OnShowHelp(object? sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            Loc.T("help.body"),
            Loc.T("help.title"),
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    // ────────────────────────────── 状态保存 ──────────────────────────────

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (_files.Count == 0 || _folder.Length == 0) return;
        SessionStore.Save(new SessionState
        {
            Folder = _folder,
            Files = _files.Select(f => Path.GetFileName(f) ?? f).ToList(),
            Cursor = _cursor,
            Kept = _kept.ToList(),
            Seen = _seen.ToList(),
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _saveTimer.Stop();
        _fullTimer.Stop();
        _statTimer.Stop();
        SaveNow();
        _cache?.Dispose();
        _cache = null;
    }

    // ────────────────────────────── 调试辅助 ──────────────────────────────

    public void OpenFolderExternal(string folder) => OpenFolder(folder);

    /// <summary>测试用：把当前这张标记为保留，用于验证已标记状态的界面表现。</summary>
    public void MarkCurrentForTest()
    {
        if (_cursor < 0 || _cursor >= _files.Count) return;
        var n = Path.GetFileName(_files[_cursor]) ?? _files[_cursor];
        _kept.Add(n);
        _seen.Add(n);
        UpdateInfo();
        UpdateStatus();
    }

    public void CaptureTo(string path)
    {
        UpdateLayout();

        try
        {
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "diag.txt"),
                $"window={ActualWidth:F1}x{ActualHeight:F1} " +
                $"dpi={VisualTreeHelper.GetDpi(this).DpiScaleX:F2}\n" +
                $"viewer={Viewer.ActualWidth:F1}x{Viewer.ActualHeight:F1} " +
                $"zoom={Viewer.ZoomLevel:F4} fit={Viewer.IsFitMode} " +
                $"img={Viewer.SourceSize}\n{Viewer.DebugRect(this)}\n");
        }
        catch { }

        try
        {
            CaptureWindowWithPrintWindow(path);
            return;
        }
        catch { }

        var dpi = VisualTreeHelper.GetDpi(this);
        var rtb = new RenderTargetBitmap(
            (int)Math.Round(ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(ActualHeight * dpi.DpiScaleY),
            96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);

        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);

    private void CaptureWindowWithPrintWindow(string path)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("no hwnd");
        if (!GetWindowRect(hwnd, out var r)) throw new InvalidOperationException("no rect");

        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;

        IntPtr screenDc = GetWindowDC(hwnd);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBmp = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, hBmp);

        try
        {
            PrintWindow(hwnd, memDc, 2);   // PW_RENDERFULLCONTENT

            var src = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(src));
            using var fs = File.Create(path);
            enc.Save(fs);
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBmp);
            DeleteDC(memDc);
            ReleaseDC(hwnd, screenDc);
        }
    }

    public async Task RunDeleteTestAsync(string folder)
    {
        var sb = new StringBuilder();
        OpenFolder(folder);
        await Task.Delay(900);

        sb.AppendLine($"打开图片: {_files.Count} 张");

        int mark = Math.Min(8, _files.Count);
        for (int i = 0; i < mark; i++)
            _kept.Add(Path.GetFileName(_files[i]) ?? _files[i]);
        SaveNow();
        sb.AppendLine($"标记保留: {_kept.Count} 张");

        int before = Directory.GetFiles(folder).Length;
        var victims = _files.Where(f => !_kept.Contains(Path.GetFileName(f) ?? f)).ToList();
        sb.AppendLine($"待删除: {victims.Count} 张");

        await DeleteFilesAsync(victims);

        int after = Directory.GetFiles(folder).Length;
        sb.AppendLine($"文件夹文件数: {before} -> {after}");
        sb.AppendLine($"会话文件残留: {File.Exists(Path.Combine(SessionStore.DataDir, "session.json"))}");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "autotest.txt"), sb.ToString(), Encoding.UTF8);
    }

    // ────────────────────────────── 真实鼠标点击验证 ──────────────────────────────

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    /// <summary>用真实鼠标事件点一下语言下拉框，确认它不是「闪一下就关」。</summary>
    public async Task RunClickTestAsync()
    {
        var sb = new StringBuilder();
        Activate();
        Topmost = true;
        await Task.Delay(500);
        Topmost = false;

        var p = LangCombo.PointToScreen(new Point(LangCombo.ActualWidth / 2, LangCombo.ActualHeight / 2));
        sb.AppendLine($"下拉框屏幕坐标: ({p.X:F0}, {p.Y:F0})");

        SetCursorPos((int)p.X, (int)p.Y);
        await Task.Delay(120);
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
        await Task.Delay(60);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);

        // 分别在看 150ms / 800ms 后的状态：如果先开后关，说明有东西把它关了
        await Task.Delay(150);
        bool openEarly = LangCombo.IsDropDownOpen;
        await Task.Delay(700);
        bool openLate = LangCombo.IsDropDownOpen;

        var popup = LangCombo.Template.FindName("PART_Popup", LangCombo)
                    as System.Windows.Controls.Primitives.Popup;

        sb.AppendLine($"点击后 150ms: IsDropDownOpen={openEarly}");
        sb.AppendLine($"点击后 850ms: IsDropDownOpen={openLate}，Popup.IsOpen={popup?.IsOpen}");
        sb.AppendLine();
        sb.AppendLine(openLate ? "PASS 下拉框保持打开" : openEarly ? "FAIL 弹出后立刻被关闭" : "FAIL 根本没有弹出");

        LangCombo.IsDropDownOpen = false;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "clicktest.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>回归测试：切换语言后所有按钮文案都要跟着变（空状态 / 已标记状态各测一遍）。</summary>
    public async Task RunLangSwitchTestAsync(string folder = "")
    {
        var sb = new StringBuilder();

        if (folder.Length > 0 && Directory.Exists(folder))
        {
            OpenFolder(folder);
            await Task.Delay(800);
            OnToggleMark(this, new RoutedEventArgs());   // 标记当前这张，进入「取消标记」状态
            await Task.Delay(200);
        }

        bool marked = IsCurrentMarked();
        sb.AppendLine($"当前状态：图片 {_files.Count} 张，当前张已标记={marked}");
        sb.AppendLine();

        int pass = 0, fail = 0;
        var order = new[] { Lang.Zh, Lang.Ja, Lang.En };
        foreach (var lang in order)
        {
            Loc.Set(lang);
            await Task.Delay(80);

            string wantMark = marked ? Loc.T("btn.unmark") : Loc.T("btn.mark");
            bool ok = TxtMarkTitle.Text == wantMark
                   && TxtKeepTitle.Text == Loc.T("btn.keep")
                   && TxtSkipTitle.Text == Loc.T("btn.skip")
                   && TxtPrevMarkTitle.Text == Loc.T("btn.prevMarked")
                   && TxtBadge.Text == Loc.T("badge.marked")
                   && TxtUndo.Text.StartsWith(Loc.T("btn.undo"))
                   && Title.StartsWith(Loc.T("app.title"));   // 打开文件夹时标题会带「 — 目录名」
            if (ok) pass++; else fail++;

            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {lang,-2} 标记=\"{TxtMarkTitle.Text}\" " +
                          $"保留=\"{TxtKeepTitle.Text}\" 跳过=\"{TxtSkipTitle.Text}\" " +
                          $"上标记=\"{TxtPrevMarkTitle.Text}\" 徽章=\"{TxtBadge.Text}\" 标题=\"{Title}\"");
        }

        sb.AppendLine();
        sb.AppendLine($"{pass} 通过 / {fail} 失败");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "langswitch.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>验证语言下拉框：点击能否命中、能否弹出、条目是否齐全。</summary>
    public async Task RunLangTestAsync()
    {
        var sb = new StringBuilder();

        // 1) 命中测试：点击中心点，落在谁身上？如果被别的元素盖住，鼠标点就没反应。
        var center = new Point(LangCombo.ActualWidth / 2, LangCombo.ActualHeight / 2);
        var hit = LangCombo.InputHitTest(center);
        sb.AppendLine($"中心点命中元素: {hit?.GetType().Name ?? "null"}");

        // 2) 找到模板里的 ToggleButton（真正接收点击的那个）
        System.Windows.Controls.Primitives.ToggleButton? toggle = null;
        var q = new Stack<System.Windows.DependencyObject>();
        q.Push(LangCombo);
        while (q.Count > 0 && toggle is null)
        {
            var d = q.Pop();
            if (d is System.Windows.Controls.Primitives.ToggleButton t) { toggle = t; break; }
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++) q.Push(VisualTreeHelper.GetChild(d, i));
        }
        sb.AppendLine($"找到 ToggleButton: {toggle is not null}");

        // 3) 走和鼠标点击同一条路径（ButtonBase.OnClick → ToggleButton.OnToggle）
        if (toggle is not null)
        {
            var peer = new System.Windows.Automation.Peers.ToggleButtonAutomationPeer(toggle);
            var provider = peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle)
                           as System.Windows.Automation.Provider.IToggleProvider;
            provider?.Toggle();
        }
        await Task.Delay(400);

        var popup = LangCombo.Template.FindName("PART_Popup", LangCombo)
                    as System.Windows.Controls.Primitives.Popup;

        int items = 0;
        if (popup?.Child is System.Windows.DependencyObject root)
        {
            var stack = new Stack<System.Windows.DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (d is System.Windows.Controls.ComboBoxItem) items++;
                int n = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(d, i));
            }
        }

        sb.AppendLine($"点击后 IsDropDownOpen: {LangCombo.IsDropDownOpen}");
        sb.AppendLine($"Popup 存在: {popup is not null}，IsOpen: {popup?.IsOpen}");
        sb.AppendLine($"弹出层可见: {popup?.IsOpen == true && items > 0}");
        sb.AppendLine($"条目数: {items}（预期 3）");
        sb.AppendLine($"选中项: {LangCombo.SelectedItem}，当前语言: {Loc.Current}");
        double popupW = popup?.Child is FrameworkElement fe ? fe.ActualWidth : 0;
        sb.AppendLine($"弹出层宽度: {popupW:F0}（下拉框宽度 {LangCombo.ActualWidth:F0}）");
        sb.AppendLine();

        bool hitOk = hit is System.Windows.Controls.Primitives.ToggleButton
                     || (hit is DependencyObject hd && IsDescendantOf(hd, toggle));
        sb.AppendLine($"命中正确: {hitOk}");
        sb.AppendLine(hitOk && toggle is not null && LangCombo.IsDropDownOpen && popup?.IsOpen == true && items == 3
            ? "PASS" : "FAIL");

        LangCombo.IsDropDownOpen = false;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "langtest.txt"), sb.ToString(), Encoding.UTF8);
    }

    private static bool IsDescendantOf(DependencyObject node, DependencyObject? ancestor)
    {
        if (ancestor is null) return false;
        while (node is not null)
        {
            if (ReferenceEquals(node, ancestor)) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    /// <summary>回归测试：停在最后一张时点「保留」，界面必须立刻反映标记状态。</summary>
    public async Task RunKeepLastTestAsync(string folder)
    {
        var sb = new StringBuilder();
        OpenFolder(folder);
        await Task.Delay(900);

        if (_files.Count == 0)
        {
            sb.AppendLine("没有图片，测试跳过");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "keeplast.txt"), sb.ToString(), Encoding.UTF8);
            return;
        }

        ShowIndex(_files.Count - 1);
        await Task.Delay(400);

        string name = Path.GetFileName(_files[_files.Count - 1]);
        sb.AppendLine($"停在最后一张：{name}（{_files.Count} / {_files.Count}）");
        sb.AppendLine($"点击前：kept={_kept.Contains(name)} badge={MarkBadge.Visibility} " +
                      $"按钮标题=\"{TxtMarkTitle.Text}\" 信息栏=\"{TxtMeta.Text}\"");

        OnKeepNext(this, new RoutedEventArgs());
        await Task.Delay(300);

        bool kept = _kept.Contains(name);
        bool badge = MarkBadge.Visibility == Visibility.Visible;
        bool titleOk = TxtMarkTitle.Text == Loc.T("btn.unmark");
        bool metaOk = TxtMeta.Text.Contains("·");

        sb.AppendLine($"点击后：kept={kept} badge={MarkBadge.Visibility} " +
                      $"按钮标题=\"{TxtMarkTitle.Text}\" 信息栏=\"{TxtMeta.Text}\"");
        sb.AppendLine($"位置仍在最后一张：{_cursor == _files.Count - 1}");
        sb.AppendLine($"状态栏：\"{TxtStatus.Text}\"");
        sb.AppendLine();
        sb.AppendLine(kept && badge && titleOk && metaOk ? "PASS 标记状态已刷新" : "FAIL 标记状态未刷新");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "keeplast.txt"), sb.ToString(), Encoding.UTF8);
    }

    /// <summary>验证「上一个/下一个已标记」的跳转规则，含两个兜底分支。</summary>
    public async Task RunJumpTestAsync(string folder)
    {
        var sb = new StringBuilder();
        OpenFolder(folder);
        await Task.Delay(900);

        if (_files.Count < 30)
        {
            sb.AppendLine($"图片太少（{_files.Count} 张），需要至少 30 张");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jumptest.txt"), sb.ToString(), Encoding.UTF8);
            return;
        }

        // 造数据：标记第 1/6/21 张（1-based），第 1..10 张视为「已处理」
        _kept.Clear();
        _seen.Clear();
        foreach (int i in new[] { 0, 5, 20 }) _kept.Add(Path.GetFileName(_files[i]));
        for (int i = 0; i <= 9; i++) _seen.Add(Path.GetFileName(_files[i]));

        sb.AppendLine($"共 {_files.Count} 张；标记了第 1、6、21 张；已处理到第 10 张");
        sb.AppendLine();

        int pass = 0, fail = 0;
        void Case(string name, int start, int dir, int expect)
        {
            ShowIndex(start);
            JumpToMarked(dir);
            bool ok = _cursor == expect;
            if (ok) pass++; else fail++;
            sb.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}：" +
                          $"第 {start + 1} 张按「{(dir < 0 ? "上一个标记" : "下一个标记")}」" +
                          $" → 第 {_cursor + 1} 张（预期 {expect + 1}）");
        }

        Case("中间向前命中标记", 3, -1, 0);
        Case("中间向后命中标记", 3, +1, 5);
        Case("标记之后向前", 7, -1, 5);
        Case("跨过多个位置向后", 10, +1, 20);
        Case("后面无标记 → 回落到最后处理位置", 25, +1, 9);
        Case("已在第一张 → 原地不动", 0, -1, 0);

        sb.AppendLine();
        sb.AppendLine($"{pass} 通过 / {fail} 失败");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "jumptest.txt"), sb.ToString(), Encoding.UTF8);
    }

    public async Task RunBenchAsync(string folder, int steps)    {
        var sb = new StringBuilder();
        var sw = new System.Diagnostics.Stopwatch();

        int tier = RenderCapability.Tier >> 16;
        sb.AppendLine($"渲染层: Tier {tier} ({(tier == 2 ? "硬件加速" : tier == 1 ? "部分加速" : "软件渲染")})");
        sb.AppendLine($"逻辑核心: {Environment.ProcessorCount}");
        sb.AppendLine($"物理内存: {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024 / 1024} MB 可用");
        sb.AppendLine();

        if (_files.Count == 0) OpenFolder(folder);
        else sb.AppendLine("（沿用已恢复的会话，不重新打开文件夹）");
        await Task.Delay(1200);

        int hit = 0, miss = 0;
        var times = new List<double>();

        // 验证「换图重置缩放」：先放大到 2 倍，再翻页，看是否回到适应窗口
        Viewer.SetZoomForTest(2.0);
        double before = Viewer.ZoomLevel;
        if (_cursor + 1 < _files.Count) ShowIndex(_cursor + 1);
        await Task.Delay(150);
        bool resetOk = Viewer.IsFitMode && Math.Abs(Viewer.ZoomLevel - Viewer.FitZoom) < 0.01;
        sb.AppendLine($"缩放重置检查: 放大到 {before:F3} → 换图后 {Viewer.ZoomLevel:F3} " +
                      $"(适应值 {Viewer.FitZoom:F3}) {(resetOk ? "PASS" : "FAIL")}");
        sb.AppendLine();

        for (int i = 0; i < steps && _cursor + 1 < _files.Count; i++)
        {
            int next = _cursor + 1;
            bool cached = _cache?.TryGet(next) is not null;

            sw.Restart();
            ShowIndex(next);
            await Dispatcher.Yield(DispatcherPriority.Background);
            await Task.Delay(25);
            sw.Stop();

            if (cached) hit++; else miss++;
            times.Add(sw.Elapsed.TotalMilliseconds);
            sb.AppendLine($"第 {next + 1,3} 张\t{(cached ? "HIT " : "MISS")}\t{sw.Elapsed.TotalMilliseconds,6:F0} ms\t缓存 {_cache?.CachedCount ?? 0} 张");
        }

        sb.AppendLine();
        sb.AppendLine($"命中 {hit} / {hit + miss}，平均 {times.DefaultIfEmpty(0).Average():F0} ms");

        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "bench.txt"), sb.ToString(), Encoding.UTF8);
    }
}
