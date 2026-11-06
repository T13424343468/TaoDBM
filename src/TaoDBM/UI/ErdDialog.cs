using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TaoDBM.Core;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace TaoDBM.UI;

/// <summary>ER 图对话框：加载库内表结构成图，拖拽排布，导出 PNG，保存 / 打开布局。</summary>
public static class ErdDialog
{
    private static Canvas _canvas = null!;
    private static ErdModel _model = new();
    private static readonly Dictionary<string, FrameworkElement> _boxes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> _pkOf = new(StringComparer.OrdinalIgnoreCase);
    private static TextBlock _hint = null!;

    public static async Task Show(MainWindow owner, List<ConnectionProfile> profiles, ConnectionProfile? preselect)
    {
        var relational = profiles.Where(p => p.Engine is not (DbEngine.MongoDB or DbEngine.Redis)).ToList();
        if (relational.Count == 0)
        {
            await new MessageDialogHelper(owner).Show("模型设计（ER 图）",
                "ER 图基于关系型连接生成，请先新建 MySQL / PostgreSQL / SQL Server / SQLite / Oracle 连接。");
            return;
        }

        _canvas = new Canvas { Background = Ant.Card, MinHeight = 560, MinWidth = 760 };
        _boxes.Clear();
        _model = new ErdModel();
        _hint = new TextBlock
        {
            Foreground = Ant.TextSub,
            Margin = new Thickness(12, 10, 0, 0),
            Text = "选连接与库后点「生成 ER 图」；框可拖拽，线是推断出的主外键关系。",
        };

        var connBox = new ComboBox { MinWidth = 200, PlaceholderText = "连接" };
        foreach (var p in relational) connBox.Items.Add(p);
        if (preselect is not null && relational.Contains(preselect)) connBox.SelectedItem = preselect;
        var dbBox = new ComboBox { MinWidth = 160, PlaceholderText = "库" };
        connBox.SelectionChanged += async (_, _) =>
        {
            var p = connBox.SelectedItem as ConnectionProfile;
            if (p is null) return;
            dbBox.Items.Clear();
            try
            {
                using var prov = ProviderFactory.Create(p);
                foreach (var d in await prov.GetDatabases()) dbBox.Items.Add(d);
                if (dbBox.Items.Count > 0) dbBox.SelectedIndex = 0;
            }
            catch { /* 状态区提示 */ }
        };
        if (connBox.SelectedItem is ConnectionProfile selP)
        {
            try
            {
                using var prov0 = ProviderFactory.Create(selP);
                foreach (var d in await prov0.GetDatabases()) dbBox.Items.Add(d);
                if (dbBox.Items.Count > 0) dbBox.SelectedIndex = 0;
            }
            catch { }
        }

        var genBtn = new Button { Content = "生成 ER 图", CornerRadius = new CornerRadius(4) };
        var layoutBtn = new Button { Content = "重新布局", CornerRadius = new CornerRadius(4) };
        var pngBtn = new Button { Content = "导出 PNG", CornerRadius = new CornerRadius(4) };
        var saveBtn = new Button { Content = "保存布局", CornerRadius = new CornerRadius(4) };
        var openBtn = new Button { Content = "打开布局", CornerRadius = new CornerRadius(4) };

        Task<(string Db, IDbProvider Prov)?> Selected()
        {
            var p = connBox.SelectedItem as ConnectionProfile;
            var db = dbBox.SelectedItem as string;
            if (p is null || string.IsNullOrEmpty(db))
            {
                _hint.Text = "请先选好连接与库。";
                return Task.FromResult<(string, IDbProvider)?>(null);
            }
            return Task.FromResult<(string, IDbProvider)?>(new ValueTuple<string, IDbProvider>(db, ProviderFactory.Create(p)));
        }

        genBtn.Click += async (_, _) =>
        {
            var sel = await Selected();
            if (sel is null) return;
            await Build(sel.Value.Db, sel.Value.Prov);
        };
        layoutBtn.Click += (_, _) =>
        {
            ErdModel.AutoLayout(_model);
            Place(_model);
        };
        pngBtn.Click += async (_, _) => await ExportPng(owner);
        saveBtn.Click += async (_, _) => await SaveLayout(owner);
        openBtn.Click += async (_, _) => await OpenLayout(owner);

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        bar.Children.Add(connBox);
        bar.Children.Add(dbBox);
        bar.Children.Add(genBtn);
        bar.Children.Add(layoutBtn);
        bar.Children.Add(pngBtn);
        bar.Children.Add(saveBtn);
        bar.Children.Add(openBtn);

        var panel = new StackPanel { Spacing = 4, MinWidth = 800, MinHeight = 640 };
        panel.Children.Add(bar);
        panel.Children.Add(_hint);
        var border = new Border
        {
            Child = new ScrollViewer
            {
                Content = _canvas,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            BorderBrush = Ant.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Height = 560,
        };
        panel.Children.Add(border);

        var dlg = new ContentDialog
        {
            Title = "模型设计 · ER 图",
            Content = panel,
            CloseButtonText = "关闭",
            XamlRoot = owner.Content.XamlRoot,
        };
        _ = dlg.ShowThemedAsync();
    }

    private static async Task Build(string db, IDbProvider prov)
    {
        _model = new ErdModel();
        _boxes.Clear();
        _canvas.Children.Clear();
        _canvas.Children.Add(_hint);
        Canvas.SetZIndex(_hint, 100);
        try
        {
            var tables = await prov.GetChildren(db);
            int limit = Math.Min(tables.Count, 40);
            for (int i = 0; i < limit; i++)
            {
                var name = tables[i];
                List<ColumnSpec> cols;
                try { cols = await prov.GetColumns(db, name); }
                catch { continue; }
                if (cols.Count == 0) continue;
                _model.Tables.Add(ErdModel.FromSpecs(name, cols));
                _pkOf[name] = cols.FirstOrDefault(c => c.IsKey)?.Name ?? "";
            }
            if (_model.Tables.Count == 0) { _hint.Text = "该库没有可用表。"; return; }
            _model.Edges = ErdModel.InferEdges(_model);
            ErdModel.AutoLayout(_model);
            Place(_model);
            _hint.Text = $"已生成 {_model.Tables.Count} 张表、{_model.Edges.Count} 条关系线（最多取前 40 张表）。";
        }
        catch (Exception ex) { _hint.Text = "生成失败：" + ex.Message; }
    }

    private static void Place(ErdModel m)
    {
        _canvas.Children.Clear();
        _canvas.Children.Add(_hint);
        Canvas.SetZIndex(_hint, 100);
        _boxes.Clear();
        foreach (var t in m.Tables) _boxes[t.Name] = AddBox(t);
        DrawEdges();
    }

    private static FrameworkElement AddBox(ErdTable t)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in t.Columns.Take(12))
            sb.AppendLine((c.IsKey ? "🔑 " : "    ") + c.Name + "  " + c.Type + (t.Columns.Count > 12 && c == t.Columns[11] ? "  …" : ""));
        var box = new Border
        {
            Background = Ant.Card,
            BorderBrush = Ant.Primary,
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            MinWidth = 180,
            MaxWidth = 260,
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = t.Name,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = Ant.Primary,
            Margin = new Thickness(0, 0, 0, 4),
        });
        stack.Children.Add(new TextBlock { Text = sb.ToString(), FontSize = 11, LineHeight = 16, Foreground = Ant.TextSub });
        box.Child = stack;

        double startX = 0, startY = 0, baseX = t.X, baseY = t.Y;
        bool dragging = false;
        box.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(box).Properties.IsLeftButtonPressed)
            {
                var pos = e.GetCurrentPoint(_canvas).Position;
                startX = pos.X; startY = pos.Y; baseX = t.X; baseY = t.Y;
                dragging = true;
                box.CapturePointer(e.Pointer);
            }
        };
        box.PointerMoved += (_, e) =>
        {
            if (!dragging) return;
            var pos = e.GetCurrentPoint(_canvas).Position;
            t.X = Math.Max(0, baseX + pos.X - startX);
            t.Y = Math.Max(0, baseY + pos.Y - startY);
            Canvas.SetLeft(box, t.X);
            Canvas.SetTop(box, t.Y);
            DrawEdges();
        };
        box.PointerReleased += (_, e) => { dragging = false; box.ReleasePointerCapture(e.Pointer); };

        Canvas.SetLeft(box, t.X);
        Canvas.SetTop(box, t.Y);
        Canvas.SetZIndex(box, 10);
        _canvas.Children.Add(box);
        return box;
    }

    private static void DrawEdges()
    {
        foreach (var child in _canvas.Children.OfType<Line>().ToList()) _canvas.Children.Remove(child);
        foreach (var e in _model.Edges)
        {
            if (!_boxes.TryGetValue(e.FromTable, out var from) || !_boxes.TryGetValue(e.ToTable, out var to)) continue;
            var a = new Point(Canvas.GetLeft(from) + 90, Canvas.GetTop(from) + 20);
            var b = new Point(Canvas.GetLeft(to) + 90, Canvas.GetTop(to) + 20);
            var line = new Line
            {
                X1 = a.X, Y1 = a.Y, X2 = b.X, Y2 = b.Y,
                Stroke = Ant.Primary,
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 2 },
            };
            _canvas.Children.Add(line);
            Canvas.SetZIndex(line, 1);
        }
    }

    private static async Task ExportPng(MainWindow owner)
    {
        if (_model.Tables.Count == 0) { _hint.Text = "先生成 ER 图再导出。"; return; }
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
            picker.FileTypeChoices.Add("PNG 图片", new List<string> { ".png" });
            picker.SuggestedFileName = $"TaoDBM-ER-{DateTime.Now:yyyyMMdd-HHmm}";
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var f = await picker.PickSaveFileAsync();
            if (f is null) return;

            var rtb = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await rtb.RenderAsync(_canvas);
            var pixels = await rtb.GetPixelsAsync();
            var bytes = new byte[pixels.Length];
            DataReader.FromBuffer(pixels).ReadBytes(bytes);

            var ras = await f.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ras);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore,
                (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, bytes);
            await encoder.FlushAsync();
            _hint.Text = $"已导出：{f.Path}（{rtb.PixelWidth}x{rtb.PixelHeight}）";
        }
        catch (Exception ex) { _hint.Text = "导出失败：" + ex.Message; }
    }

    private static async Task SaveLayout(MainWindow owner)
    {
        if (_model.Tables.Count == 0) { _hint.Text = "先布局再保存。"; return; }
        try
        {
            var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("TaoDBM ER 布局", new List<string> { ".erjson" });
            picker.SuggestedFileName = $"TaoDBM-ER-布局-{DateTime.Now:yyyyMMdd}";
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var f = await picker.PickSaveFileAsync();
            if (f is null) return;
            await FileIO.WriteTextAsync(f, _model.ToJson());
            _hint.Text = "布局已保存：" + f.Path;
        }
        catch (Exception ex) { _hint.Text = "保存失败：" + ex.Message; }
    }

    private static async Task OpenLayout(MainWindow owner)
    {
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add(".erjson");
            picker.FileTypeFilter.Add("*");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var f = await picker.PickSingleFileAsync();
            if (f is null) return;
            var text = await FileIO.ReadTextAsync(f);
            var m = ErdModel.FromJson(text);
            if (m is null || m.Tables.Count == 0) { _hint.Text = "布局文件无效。"; return; }
            _model = m;
            foreach (var t in _model.Tables)
                _pkOf[t.Name] = t.Columns.FirstOrDefault(c => c.IsKey)?.Name ?? "";
            Place(_model);
            _hint.Text = $"已载入 {_model.Tables.Count} 张表、{_model.Edges.Count} 条关系线。";
        }
        catch (Exception ex) { _hint.Text = "打开失败：" + ex.Message; }
    }
}
