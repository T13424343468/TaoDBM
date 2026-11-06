using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using Windows.Storage.Pickers;
using Windows.UI.Text;
using TaoDBM.Core;

namespace TaoDBM.UI;

public enum WorkMode { Query, TableData }

/// <summary>右侧工作区的一个标签页：查询编辑器或表数据浏览。</summary>
public class WorkTab : Grid
{
    private readonly MainWindow _owner;
    private readonly IDbProvider _provider;
    private string _db;
    private readonly string _child;
    private readonly WorkMode _mode;

    private readonly ComboBox _dbBox = new() { MinWidth = 160, PlaceholderText = "选择库" };
    private readonly RichEditBox _editor = new()
    {
        TextWrapping = TextWrapping.NoWrap,
        FontSize = 13,
        MinHeight = 160,
        PlaceholderText = "在此输入 SQL / 过滤器 / 命令，F5 或 Ctrl+Enter 执行",
    };
    private readonly DispatcherQueueTimer _hlTimer;
    private bool _applying;
    private readonly Button _runBtn = new() { Content = "▶ 执行", Background = Ant.Primary, Foreground = Ant.OnPrimary, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4), Padding = new Thickness(14, 5, 14, 5) };
    private readonly Button _csvBtn = new() { Content = "导出 CSV", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _insBtn = new() { Content = "导出 INSERT", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _structBtn = new() { Content = "结构", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _addRowBtn = new() { Content = "新增行", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _importBtn = new() { Content = "导入", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _designBtn = new() { Content = "设计表", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _fakerBtn = new() { Content = "拟真数据", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _histBtn = new() { Content = "历史", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _reportBtn = new() { Content = "报表", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _delKeyBtn = new() { Content = "删除此键", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly Button _favBtn = new() { Content = "收藏", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4) };
    private readonly TextBlock _pageInfo = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Ant.TextSub, FontSize = 12 };
    private readonly Button _prevBtn = new() { Content = "◀ 上一页", Padding = new Thickness(8, 3, 8, 3) };
    private readonly Button _nextBtn = new() { Content = "下一页 ▶", Padding = new Thickness(8, 3, 8, 3) };

    private readonly ScrollViewer _resultsHost = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly TextBox _messageBox = new()
    {
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = (FontFamily)Application.Current.Resources["AntMono"],
        FontSize = 12,
        MinHeight = 64,
        MaxHeight = 140,
        PlaceholderText = "消息",
    };

    private List<QueryResult> _last = new();
    private QueryResult? _browseResult;
    private List<ColumnSpec> _cols = new();
    private int _offset;
    private const int PageSize = 200;
    private bool _busy;
    private bool _isRelational => _provider is RelationalProvider;

    public WorkTab(MainWindow owner, IDbProvider provider, string db, string child, WorkMode mode, string? initialSql)
    {
        _owner = owner;
        _provider = provider;
        _db = db;
        _child = child;
        _mode = mode;

        _hlTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _hlTimer.Interval = TimeSpan.FromMilliseconds(250);
        _hlTimer.Tick += (_, _) => { _hlTimer.Stop(); ApplyHighlight(); };
        if (mode == WorkMode.Query)
        {
            _editor.TextChanged += (_, _) => { if (!_applying) { _hlTimer.Stop(); _hlTimer.Start(); } };
            _editor.Loaded += (_, _) => SetEditorFont();
            Ant.ThemeApplied += OnThemeApplied;
            Unloaded += (_, _) => Ant.ThemeApplied -= OnThemeApplied;
        }

        // 层色底定律：标签页根板显式挂主题底色，中间内容靠网格自带层色与描边，不依赖系统纯黑
        Background = Ant.Bg;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = mode == WorkMode.Query ? GridLength.Auto : new GridLength(0) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 工具栏
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(10, 8, 10, 8) };
        bar.Children.Add(_dbBox);
        bar.Children.Add(_runBtn);
        bar.Children.Add(_csvBtn);
        if (provider is not (MongoProvider or RedisProvider)) bar.Children.Add(_insBtn);
        if (mode == WorkMode.TableData && provider.SupportsBrowse && provider.IsNoSql == false)
        {
            bar.Children.Add(_structBtn);
            bar.Children.Add(_addRowBtn);
            bar.Children.Add(_importBtn);
            bar.Children.Add(_designBtn);
            bar.Children.Add(_fakerBtn);
        }
        if (mode == WorkMode.TableData && provider is RedisProvider && child.Length > 0)
        {
            bar.Children.Add(_delKeyBtn);   // Redis 键详情页：删除当前键
            _delKeyBtn.Click += (_, _) => _ = DeleteKeyAsync();
        }
        if (mode == WorkMode.Query) { bar.Children.Add(_favBtn); bar.Children.Add(_histBtn); }
        bar.Children.Add(_reportBtn);
        bar.Children.Add(new TextBlock { Text = "　", Width = 8 });
        bar.Children.Add(_prevBtn);
        bar.Children.Add(_pageInfo);
        bar.Children.Add(_nextBtn);
        Children.Add(bar);
        SetRow(bar, 0);

        if (mode == WorkMode.Query) { Children.Add(_editor); SetRow(_editor, 1); _editor.Height = 170; }

        _resultsHost.Padding = new Thickness(4);
        Children.Add(_resultsHost);
        SetRow(_resultsHost, 2);

        var msgBorder = new Border
        {
            Child = _messageBox,
            Margin = new Thickness(8, 4, 8, 8),
            BorderBrush = Ant.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        Children.Add(msgBorder);
        SetRow(msgBorder, 4);

        _runBtn.Click += (_, _) => _ = RunOrRefresh();
        _csvBtn.Click += (_, _) => _ = ExportAsync(true);
        _insBtn.Click += (_, _) => _ = ExportAsync(false);
        _structBtn.Click += (_, _) => _ = ShowStructure();
        _addRowBtn.Click += (_, _) => _ = EditRow(null);
        _importBtn.Click += (_, _) => _ = ImportCsv();
        _designBtn.Click += (_, _) => _ = UI.TableDesignerDialog.Show(_owner, _provider, _db, _child.Length > 0 ? _child : null, _cols.Count > 0 ? _cols : null);
        _fakerBtn.Click += (_, _) => _ = UI.FakerDialog.Show(_owner, _provider, _db, _child.Length > 0 ? _child : "main");
        _histBtn.Click += (_, _) => ShowHistory();
        _favBtn.Click += (_, _) => ShowFavorites();
        _reportBtn.Click += (_, _) => _ = ShowReport();
        _prevBtn.Click += (_, _) => { if (_offset >= PageSize) { _offset -= PageSize; _ = RunOrRefresh(); } };
        _nextBtn.Click += (_, _) => { _offset += PageSize; _ = RunOrRefresh(); };
        AddAccelerator(Windows.System.VirtualKey.F5, Windows.System.VirtualKeyModifiers.None, RunNow);
        AddAccelerator(Windows.System.VirtualKey.Enter, Windows.System.VirtualKeyModifiers.Control, RunNow);
        AddAccelerator(Windows.System.VirtualKey.Space, Windows.System.VirtualKeyModifiers.Control, ShowComplete);

        _dbBox.ItemsSource = new List<string> { string.IsNullOrEmpty(db) ? "(默认)" : db };
        _dbBox.SelectedIndex = 0;
        _dbBox.SelectionChanged += (_, _) =>
        {
            if (_dbBox.SelectedItem is string s && s != "(默认)") _db = s;
        };

        if (initialSql != null) EditorText = initialSql;
        if (mode == WorkMode.TableData) EditorText = "";

        UpdateButtons();
        if (mode == WorkMode.TableData) _ = RunOrRefresh();
    }

    private void AddAccelerator(Windows.System.VirtualKey key, Windows.System.VirtualKeyModifiers mod, Action action)
    {
        var acc = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = key, Modifiers = mod };
        acc.Invoked += (s, e) => { e.Handled = true; action(); };
        KeyboardAccelerators.Add(acc);
    }

    private async void RunNow()
    {
        try { await RunOrRefresh(); } catch { /* 已在界面提示 */ }
    }

    private void UpdateButtons()
    {
        bool browse = _mode == WorkMode.TableData && _provider.SupportsBrowse;
        _prevBtn.Visibility = browse || _mode == WorkMode.TableData ? Visibility.Visible : Visibility.Collapsed;
        _nextBtn.Visibility = _prevBtn.Visibility;
        _pageInfo.Visibility = _prevBtn.Visibility;
        _prevBtn.IsEnabled = _offset > 0;
    }

    private async Task RunOrRefresh()
    {
        if (_busy) return;
        _busy = true;
        _runBtn.IsEnabled = false;
        try
        {
            QueryResult r;
            if (_mode == WorkMode.TableData)
            {
                if (!_provider.SupportsBrowse)
                {
                    _messageBox.Text = "该引擎不支持表/集合网格浏览。";
                    return;
                }
                r = await _provider.Browse(_db, _child, _offset, PageSize);
                _browseResult = r;
                if (_provider is RedisProvider)
                {
                    bool isKeyDetail = _child.Length > 0;
                    ShowResult(r, onRowTap: isKeyDetail ? null : idx => OpenKeyDetail(idx));
                    _pageInfo.Text = r.TotalRows is long kt
                        ? $"共 {kt:N0} 个键 · 第 {_offset + 1}-{Math.Min(_offset + r.Rows.Count, int.MaxValue)} 个" + (isKeyDetail ? "" : " · 双击键查看详情")
                        : $"{r.Rows.Count} 行";
                    _prevBtn.IsEnabled = _offset > 0;
                    _nextBtn.IsEnabled = _offset + PageSize < (r.TotalRows ?? long.MaxValue);
                }
                else if (_isRelational)
                {
                    try { _cols = await _provider.GetColumns(_db, _child); } catch { _cols.Clear(); }
                    ShowResult(r, editable: true);
                    _pageInfo.Text = r.TotalRows is long t
                        ? $"共 {t:N0} 行 · 第 {_offset + 1}-{Math.Min(_offset + r.Rows.Count, int.MaxValue)} 行 · 单击行可编辑"
                        : $"{r.Rows.Count} 行";
                    _prevBtn.IsEnabled = _offset > 0;
                    _nextBtn.IsEnabled = _offset + PageSize < (r.TotalRows ?? long.MaxValue);
                }
                else
                {
                    ShowResult(r);
                    _pageInfo.Text = r.TotalRows is long m
                        ? $"共 {m:N0} 条 · 第 {_offset + 1}-{Math.Min(_offset + r.Rows.Count, int.MaxValue)} 条"
                        : $"{r.Rows.Count} 条";
                    _prevBtn.IsEnabled = _offset > 0;
                    _nextBtn.IsEnabled = _offset + PageSize < (r.TotalRows ?? long.MaxValue);
                }
            }
            else
            {
                var sql = EditorText;
                if (string.IsNullOrWhiteSpace(sql)) { _messageBox.Text = "请输入要执行的语句。"; return; }
                var list = await _provider.Execute(_db, sql);
                QueryHistory.Add(_provider.EngineName, _db, sql);
                ShowResults(list);
            }
            _owner.SetStatus($"{_provider.EngineName} · {_db}{(string.IsNullOrEmpty(_child) ? "" : " · " + _child)} 执行完成");
        }
        catch (Exception ex)
        {
            _messageBox.Text = "错误：" + ex.Message;
            _owner.SetStatus("执行出错");
        }
        finally
        {
            _busy = false;
            _runBtn.IsEnabled = true;
        }
    }

    private void ShowResults(List<QueryResult> list)
    {
        _last = list;
        var panel = new StackPanel { Spacing = 12 };
        var msgs = new System.Text.StringBuilder();
        foreach (var r in list)
        {
            panel.Children.Add(GridBuilder.Build(r));
            if (r.Message.Length > 0) msgs.AppendLine($"[{r.Title}] {r.Message}");
        }
        _resultsHost.Content = panel;
        _messageBox.Text = msgs.Length > 0 ? msgs.ToString() : $"{list.Count} 条结果 · 总耗时 {list.Sum(x => x.Elapsed.TotalMilliseconds):N0} ms";
    }

    private void ShowResult(QueryResult r, bool editable = false, Action<int>? onRowTap = null)
    {
        _last = new List<QueryResult> { r };
        FrameworkElement el;
        if (onRowTap is not null) el = GridBuilder.Build(r, onRowTap);
        else if (editable && _isRelational) el = GridBuilder.Build(r, idx => _ = EditRow(idx));
        else el = GridBuilder.Build(r);
        _resultsHost.Content = el;
        _messageBox.Text = r.Message.Length > 0 ? r.Message
            : (onRowTap is not null
                ? $"{r.Rows.Count} 行 · {r.Elapsed.TotalMilliseconds:N0} ms · 单击行进入详情"
                : $"{r.Rows.Count} 行 · {r.Elapsed.TotalMilliseconds:N0} ms" + (editable && _isRelational ? " · 单击行编辑 / 删除" : ""));
    }

    /// <summary>Redis 键列表：单击某键 → 打开该键详情页。</summary>
    private void OpenKeyDetail(int idx)
    {
        if (_browseResult is null || idx >= _browseResult.Rows.Count) return;
        var key = Convert.ToString(_browseResult.Rows[idx][0]) ?? "";
        if (key.Length == 0) return;
        var info = new NodeInfo { Kind = NodeKind.Table, Profile = _provider.Profile, Provider = _provider, Name = key, Db = _db };
        _owner.OpenTableTab(info);
    }

    /// <summary>Redis 键详情页：删除当前键并刷新列表来源。</summary>
    private async Task DeleteKeyAsync()
    {
        if (_provider is not RedisProvider rp) return;
        bool ok = await _owner.ConfirmAsync($"确定删除键“{_child}”吗？");
        if (!ok) return;
        try
        {
            bool n = await rp.ExecuteDelKey(_db, _child);
            _messageBox.Text = n ? $"键 {_child} 已删除。" : "键不存在（可能已过期或已被删除）。";
            _owner.SetStatus(n ? $"Redis 键 {_child} 已删除" : "键不存在");
        }
        catch (Exception ex)
        {
            _messageBox.Text = "错误：" + ex.Message;
        }
    }

    private async Task ExportAsync(bool csv)
    {
        if (_last.Count == 0) { _messageBox.Text = "没有可导出的结果，请先执行。"; return; }
        try
        {
            var picker = new FileSavePicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            };
            if (csv)
            {
                picker.SuggestedFileName = "taodbm_export";
                picker.FileTypeChoices.Add("CSV 文件", new List<string> { ".csv" });
            }
            else
            {
                picker.SuggestedFileName = "taodbm_insert";
                picker.FileTypeChoices.Add("SQL 文件", new List<string> { ".sql" });
            }
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner));
            var file = await picker.PickSaveFileAsync();
            if (file == null) return;
            var content = csv ? Exporter.Csv(_last[0]) : Exporter.InsertSql(_last[0], _child.Length > 0 ? _child : "table");
            await Windows.Storage.FileIO.WriteTextAsync(file, content);
            _messageBox.Text = $"已导出到 {file.Path}";
        }
        catch (Exception ex)
        {
            _messageBox.Text = "导出失败：" + ex.Message;
        }
    }

    // ---------- 表结构 ----------

    private async Task ShowStructure()
    {
        try
        {
            _cols = await _provider.GetColumns(_db, _child);
            var qr = new QueryResult { Title = $"{_child} · 结构", TotalRows = _cols.Count };
            qr.Columns.AddRange(new[] { "列名", "类型", "可空", "主键" });
            foreach (var c in _cols)
                qr.Rows.Add(new object?[] { c.Name, c.DataType, c.Nullable ? "是" : "否", c.IsKey ? "🔑 PRI" : "" });
            if (_cols.Count == 0) qr.Message = "未取到列信息。";
            ShowResult(qr);
            _messageBox.Text = $"表 {_child} 共 {_cols.Count} 列。";
        }
        catch (Exception ex)
        {
            _messageBox.Text = "读取结构失败：" + ex.Message;
        }
    }

    // ---------- 行内编辑（新增 / 修改 / 删除） ----------

    private ColumnSpec? KeyCol => _cols.FirstOrDefault(c => c.IsKey);

    private async Task EditRow(int? rowIndex)
    {
        if (!_isRelational) { _messageBox.Text = "该引擎不支持行内编辑。"; return; }
        if (_cols.Count == 0) { _messageBox.Text = "请先点“结构”或刷新数据读取列信息。"; return; }

        object?[]? existing = rowIndex.HasValue && _browseResult != null && rowIndex.Value < _browseResult.Rows.Count
            ? _browseResult.Rows[rowIndex.Value] : null;
        if (existing != null && KeyCol is null)
        {
            _messageBox.Text = "该表没有主键列，无法安全编辑（避免误改整表）。";
            return;
        }

        var inputs = new List<TextBox>();
        var panel = new StackPanel { Spacing = 10, MinWidth = 380, Margin = new Thickness(0, 0, 12, 0) };
        for (int i = 0; i < _cols.Count; i++)
        {
            var box = new TextBox
            {
                Header = $"{_cols[i].Name}（{_cols[i].DataType}{(_cols[i].IsKey ? " · 🔑" : "")}）",
                PlaceholderText = "留空表示 NULL",
            };
            if (existing != null && i < existing.Length)
            {
                var v = existing[i];
                box.Text = (v == null || v is DBNull) ? "" : Exporter.Format(v);
                if (_cols[i].IsKey) box.IsReadOnly = true;
            }
            inputs.Add(box);
            panel.Children.Add(box);
        }

        var dlg = new ContentDialog
        {
            Title = existing == null ? $"新增行 · {_child}" : $"编辑行 · {_child}",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            PrimaryButtonText = "保存",
            SecondaryButtonText = "删除该行",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _owner.Content.XamlRoot,
        };

        var result = await dlg.ShowThemedAsync();
        if (result == ContentDialogResult.None) return;

        if (result == ContentDialogResult.Secondary)
        {
            if (existing == null || KeyCol is null) return;
            var confirm = new ContentDialog
            {
                Title = "确认删除",
                Content = new TextBlock { Text = $"确定删除 {_child} 中 {KeyCol!.Name} = {inputs[_cols.IndexOf(KeyCol)].Text} 的这一行吗？", TextWrapping = TextWrapping.Wrap },
                PrimaryButtonText = "删除",
                CloseButtonText = "取消",
                XamlRoot = _owner.Content.XamlRoot,
            };
            if (await confirm.ShowThemedAsync() != ContentDialogResult.Primary) return;
            var del = $"DELETE FROM {_provider.Quote(_child)} WHERE {_provider.Quote(KeyCol!.Name)} = {Exporter.Literal(KeyCol, inputs[_cols.IndexOf(KeyCol)].Text)}";
            await ExecWrite(del, "已删除 1 行");
            return;
        }

        // 保存：UPDATE 或 INSERT
        var sets = new List<string>();
        var insCols = new List<string>();
        var insVals = new List<string>();
        for (int i = 0; i < _cols.Count; i++)
        {
            string val = Exporter.Literal(_cols[i], inputs[i].Text);
            if (existing == null)
            {
                if (inputs[i].Text.Length > 0) { insCols.Add(_provider.Quote(_cols[i].Name)); insVals.Add(val); }
            }
            else if (!_cols[i].IsKey)
            {
                sets.Add($"{_provider.Quote(_cols[i].Name)} = {val}");
            }
        }

        string sql;
        if (existing == null)
        {
            if (insCols.Count == 0) { _messageBox.Text = "没有填写任何值。"; return; }
            sql = $"INSERT INTO {_provider.Quote(_child)} ({string.Join(", ", insCols)}) VALUES ({string.Join(", ", insVals)})";
        }
        else
        {
            if (sets.Count == 0) { _messageBox.Text = "没有可更新的列（主键列不可修改）。"; return; }
            sql = $"UPDATE {_provider.Quote(_child)} SET {string.Join(", ", sets)} WHERE {_provider.Quote(KeyCol!.Name)} = {Exporter.Literal(KeyCol, inputs[_cols.IndexOf(KeyCol)].Text)}";
        }
        await ExecWrite(sql, existing == null ? "已新增 1 行" : "已保存修改");
    }

    private async Task ExecWrite(string sql, string okMsg)
    {
        try
        {
            var list = await _provider.Execute(_db, sql);
            var qr = list.LastOrDefault();
            _messageBox.Text = qr is { Message.Length: > 0 } ? qr.Message : okMsg;
            _owner.SetStatus(okMsg);
            _offset = 0;
            await RunOrRefresh();
        }
        catch (Exception ex)
        {
            _messageBox.Text = "执行失败：" + ex.Message;
        }
    }

    // ---------- CSV / JSON 导入 ----------

    private async Task ImportCsv()
    {
        if (!_isRelational) { _messageBox.Text = "该引擎不支持数据导入。"; return; }
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".json");
            picker.FileTypeFilter.Add(".txt");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var text = await Windows.Storage.FileIO.ReadTextAsync(file);
            bool isJson = file.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
            List<string> headers;
            List<string?[]> rows;
            if (isJson)
            {
                (headers, rows) = JsonImporter.Parse(text);
                if (headers.Count == 0 || rows.Count == 0) { _messageBox.Text = "JSON 内容为空或不是对象数组 / 行式 JSON。"; return; }
            }
            else
            {
                (headers, rows) = CsvImporter.Parse(text);
                if (headers.Count == 0 || rows.Count == 0) { _messageBox.Text = "CSV 内容为空或格式无法识别。"; return; }
            }
            if (_cols.Count == 0) _cols = await _provider.GetColumns(_db, _child);

            var stmts = CsvImporter.BuildInserts(_provider, _db, _child, _cols, headers, rows);
            int total = rows.Count, ok = 0; var errs = new System.Text.StringBuilder();
            foreach (var s in stmts)
            {
                var list = await _provider.Execute(_db, s);
                foreach (var qr in list)
                {
                    if (qr.Message.StartsWith("错误", StringComparison.Ordinal)) errs.AppendLine(qr.Message);
                    else ok++;
                }
            }
            QueryHistory.Add(_provider.EngineName, _db, $"-- {(isJson ? "JSON" : "CSV")} 导入 {file.Name}（{total} 行）");
            _messageBox.Text = $"导入完成：共 {total} 行，成功 {ok} 行{(errs.Length > 0 ? "\n" + errs : "")}";
            _owner.SetStatus($"{(isJson ? "JSON" : "CSV")} 导入 {file.Name}：{ok}/{total} 行");
            _offset = 0;
            await RunOrRefresh();
        }
        catch (Exception ex)
        {
            _messageBox.Text = "导入失败：" + ex.Message;
        }
    }

    // ---------- 标签会话快照（重启恢复用） ----------

    public string SessionConnectionId => _provider.Profile.Id;
    public string SessionMode => _mode.ToString();
    public string SessionDb => _db;
    public string SessionChild => _child;
    public string SessionSql => EditorText;

    // ---------- 查询收藏 ----------

    private async void ShowFavorites()
    {
        var lv = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 260 };
        Action refill = () =>
        {
            lv.Items.Clear();
            foreach (var f in Favorites.Load())
            {
                string oneLine = f.Sql.Replace("\r", " ").Replace("\n", " ");
                if (oneLine.Length > 70) oneLine = oneLine[..70] + "…";
                lv.Items.Add(new TextBlock
                {
                    Text = $"{f.Name}　[{f.Engine}/{(f.Database.Length > 0 ? f.Database : "(默认)")}]  {oneLine}",
                    TextWrapping = TextWrapping.NoWrap,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxWidth = 480,
                });
            }
        };
        refill();
        if (lv.Items.Count == 0)
        {
            var empty = new ContentDialog
            {
                Title = "查询收藏",
                Content = new TextBlock { Text = "还没有收藏。把编辑器里的 SQL 写好后，点「收藏」→「保存当前 SQL」即可。", Foreground = Ant.TextSub },
                CloseButtonText = "好的",
                XamlRoot = _owner.Content.XamlRoot,
            };
            await empty.ShowThemedAsync();
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "给这段 SQL 起个名字", Text = $"SQL {DateTime.Now:MM-dd HH:mm}" };
        var saveBtn = new Button { Content = "保存当前 SQL", CornerRadius = new CornerRadius(4) };
        var panel = new StackPanel { Spacing = 8 };
        var saveRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        saveRow.Children.Add(nameBox);
        saveRow.Children.Add(saveBtn);
        panel.Children.Add(saveRow);
        panel.Children.Add(lv);
        var dlg = new ContentDialog
        {
            Title = "查询收藏（点选填入编辑器）",
            Content = panel,
            PrimaryButtonText = "填入所选",
            SecondaryButtonText = "删除所选",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _owner.Content.XamlRoot,
        };
        saveBtn.Click += (_, _) =>
        {
            var sql = EditorText.Trim();
            var name = nameBox.Text.Trim();
            if (sql.Length == 0 || name.Length == 0) return;
            var items = Favorites.Load();
            Favorites.Add(items, name, _provider.EngineName, _db, sql);
            Favorites.Save(items);
            nameBox.Text = $"SQL {DateTime.Now:MM-dd HH:mm}";
            refill();
        };
        var result = await dlg.ShowThemedAsync();
        var listNow = Favorites.Load();
        if (result == ContentDialogResult.Primary && lv.SelectedIndex >= 0)
            EditorText = listNow[lv.SelectedIndex].Sql;
        else if (result == ContentDialogResult.Secondary && lv.SelectedIndex >= 0)
        {
            Favorites.Remove(listNow, listNow[lv.SelectedIndex].Id);
            Favorites.Save(listNow);
        }
    }

    // ---------- 查询历史 ----------

    private async void ShowHistory()
    {
        var list = QueryHistory.Items.Take(50).ToList();
        if (list.Count == 0)
        {
            var empty = new ContentDialog
            {
                Title = "查询历史",
                Content = new TextBlock { Text = "还没有历史记录。", Foreground = Ant.TextSub },
                CloseButtonText = "好的",
                XamlRoot = _owner.Content.XamlRoot,
            };
            await empty.ShowThemedAsync();
            return;
        }

        var lv = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 360 };
        foreach (var h in list)
        {
            string oneLine = h.Sql.Replace("\r", " ").Replace("\n", " ");
            if (oneLine.Length > 90) oneLine = oneLine[..90] + "…";
            lv.Items.Add(new TextBlock
            {
                Text = $"[{h.Time:MM-dd HH:mm}] {h.Engine}/{h.Database}  {oneLine}",
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 480,
            });
        }
        var dlg = new ContentDialog
        {
            Title = "查询历史（点选填入编辑器）",
            Content = lv,
            PrimaryButtonText = "填入所选",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _owner.Content.XamlRoot,
        };
        var result = await dlg.ShowThemedAsync();
        if (result == ContentDialogResult.Primary && lv.SelectedIndex >= 0)
            EditorText = list[lv.SelectedIndex].Sql;
    }

    // ---------- 报表 ----------

    private async Task ShowReport()
    {
        var qr = _last.FirstOrDefault();
        if (qr is null) { _messageBox.Text = "没有可生成报表的结果，请先执行查询或浏览数据。"; return; }
        var (rows, stats) = ReportBuilder.Build(qr);

        var lv = new ListView { Height = 320, SelectionMode = ListViewSelectionMode.None };
        foreach (var c in stats)
        {
            string min = c.Min?.ToString("0.###") ?? "-", max = c.Max?.ToString("0.###") ?? "-", avg = c.Avg?.ToString("0.###") ?? "-";
            string top = c.Top.Count == 0 ? "-" : string.Join("，", c.Top.Select(t => $"{t.Value}（{t.Count}）"));
            lv.Items.Add(new TextBlock
            {
                Text = $"{c.Name}：空 {c.NullCount} · 去重 {c.DistinctCount}" +
                       (c.Min.HasValue ? $" · 最小 {min} · 最大 {max} · 平均 {avg}" : $" · 常见值：{top}"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 560,
            });
        }
        var exportBtn = new Button { Content = "导出 HTML 报表", CornerRadius = new CornerRadius(4) };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = $"共 {rows} 行 · {qr.Columns.Count} 列", Foreground = Ant.TextSub });
        panel.Children.Add(lv);
        panel.Children.Add(exportBtn);
        var dlg = new ContentDialog
        {
            Title = "报表 · " + qr.Title,
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = _owner.Content.XamlRoot,
        };
        exportBtn.Click += async (_, _) =>
        {
            try
            {
                var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeChoices.Add("HTML 报表", new List<string> { ".html" });
                picker.SuggestedFileName = $"TaoDBM-报表-{DateTime.Now:yyyyMMdd-HHmm}";
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(_owner));
                var file = await picker.PickSaveFileAsync();
                if (file is null) return;
                await Windows.Storage.FileIO.WriteTextAsync(file, ReportBuilder.ToHtml(qr, qr.Title));
                _messageBox.Text = "报表已导出到 " + file.Path;
            }
            catch (Exception ex) { _messageBox.Text = "导出失败：" + ex.Message; }
        };
        await dlg.ShowThemedAsync();
    }

    // ---------- SQL 语法高亮（RichEditBox 着色） ----------

    /// <summary>编辑器文本（Document 读写，末尾可能有段落符，执行前会 Trim）。</summary>
    private string EditorText
    {
        get
        {
            try { _editor.Document.GetText(Microsoft.UI.Text.TextGetOptions.None, out var t); return t ?? ""; }
            catch { return ""; }
        }
        set
        {
            try
            {
                _applying = true;
                _editor.Document?.SetText(Microsoft.UI.Text.TextSetOptions.None, value ?? "");
            }
            catch { }
            finally { _applying = false; }
            ApplyHighlight();
        }
    }

    private void SetEditorFont()
    {
        try
        {
            var fmt = _editor.Document.GetDefaultCharacterFormat();
            fmt.Name = "Consolas";
            fmt.Size = 13;
            _editor.Document.SetDefaultCharacterFormat(fmt);
            ApplyHighlight();
        }
        catch { /* 字体设置失败不影响使用 */ }
    }

    /// <summary>按 SqlHighlighter 词法结果着色，保留光标位置。</summary>
    /// <summary>换肤后重刷高亮：关键字取当前主题主色（ThemeApplied 在画刷变色后广播）。</summary>
    private void OnThemeApplied(IReadOnlyDictionary<string, string> pal)
    {
        if (_mode == WorkMode.Query) ApplyHighlight();
    }

    private void ApplyHighlight()
    {
        if (_mode != WorkMode.Query) return;
        var doc = _editor.Document;
        if (doc is null) return;
        _applying = true;
        try
        {
            string text = EditorText;
            if (text.Length == 0) return;
            int caret = doc.Selection?.StartPosition ?? 0;

            // 先整体还原默认色，避免已删关键字的残色
            doc.GetRange(0, text.Length).CharacterFormat.ForegroundColor = Ant.Text.Color;

            foreach (var t in SqlHighlighter.Tokenize(text))
            {
                if (t.Kind == TokenKind.Plain) continue;
                int s = Math.Min(t.Start, text.Length);
                int e = Math.Min(t.Start + t.Length, text.Length);
                if (e <= s) continue;
                var r = doc.GetRange(s, e);
                r.CharacterFormat.ForegroundColor = t.Kind switch
                {
                    TokenKind.Keyword => Ant.Primary.Color,
                    TokenKind.TypeName => Ant.Color("722ED1"),
                    TokenKind.String => Ant.Color("D46B08"),
                    TokenKind.Comment => Ant.Color("8C8C8C"),
                    TokenKind.Number => Ant.Color("389E0D"),
                    TokenKind.QuotedIdent => Ant.Color("13C2C2"),
                    _ => Ant.Color("595959"),
                };
                if (t.Kind is TokenKind.Keyword or TokenKind.TypeName) r.CharacterFormat.Bold = Microsoft.UI.Text.FormatEffect.On;
                if (t.Kind == TokenKind.Comment) r.CharacterFormat.Italic = Microsoft.UI.Text.FormatEffect.On;
            }
            doc.Selection?.SetRange(caret, caret);
        }
        catch { /* 高亮失败不影响编辑 */ }
        finally { _applying = false; }
    }

    // ---------- SQL 补全（Ctrl+Space） ----------

    private static readonly string[] Keywords =
    {
        "SELECT", "FROM", "WHERE", "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE",
        "CREATE", "TABLE", "DROP", "ALTER", "JOIN", "LEFT", "RIGHT", "INNER", "OUTER",
        "GROUP", "BY", "ORDER", "HAVING", "LIMIT", "OFFSET", "AND", "OR", "NOT", "NULL",
        "IS", "IN", "AS", "DISTINCT", "COUNT", "SUM", "AVG", "MIN", "MAX", "LIKE",
        "BETWEEN", "EXISTS", "UNION", "ALL", "DESC", "ASC", "PRIMARY", "KEY", "INDEX",
    };

    private async void ShowComplete()
    {
        int caret = _editor.Document?.Selection?.StartPosition ?? 0;
        string text = EditorText;
        int start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        string word = text[start..caret];

        var candidates = Keywords
            .Concat(_cols.Select(c => c.Name))
            .Where(k => k.StartsWith(word, StringComparison.OrdinalIgnoreCase) &&
                        !k.Equals(word, StringComparison.OrdinalIgnoreCase))
            .Take(30).ToList();
        if (candidates.Count == 0) return;

        var lv = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 280, MaxWidth = 380 };
        foreach (var k in candidates) lv.Items.Add(new TextBlock { Text = k, FontFamily = (FontFamily)Application.Current.Resources["AntMono"] });

        var dlg = new ContentDialog
        {
            Title = $"补全（当前词：{word}）",
            Content = lv,
            PrimaryButtonText = "插入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _owner.Content.XamlRoot,
        };
        var result = await dlg.ShowThemedAsync();
        if (result != ContentDialogResult.Primary || lv.SelectedIndex < 0) return;
        string pick = candidates[lv.SelectedIndex];
        EditorText = text[..start] + pick + text[caret..];
        _editor.Document?.Selection?.SetRange(start + pick.Length, start + pick.Length);
        _editor.Focus(FocusState.Programmatic);
    }
}
