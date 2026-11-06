using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaoDBM.Core;

namespace TaoDBM.UI;

/// <summary>数据传输与结构同步：源连接/库/表 → 目标连接/库，可选补结构、传数据。</summary>
public static class DataSyncDialog
{
    public static async Task Show(MainWindow owner, List<ConnectionProfile> profiles, ConnectionProfile? preselect)
    {
        var relational = profiles.Where(p => p.Engine is not (DbEngine.MongoDB or DbEngine.Redis)).ToList();
        if (relational.Count < 2)
        {
            await new MessageDialogHelper(owner).Show("数据传输",
                "需要至少两个关系型连接（MySQL / PostgreSQL / SQL Server / SQLite / Oracle）。请先在左侧新建连接。");
            return;
        }

        var srcBox = new ComboBox { MinWidth = 260, PlaceholderText = "源连接" };
        var dstBox = new ComboBox { MinWidth = 260, PlaceholderText = "目标连接" };
        foreach (var p in relational)
        {
            srcBox.Items.Add(p);
            dstBox.Items.Add(p);
        }
        if (preselect is not null && relational.Contains(preselect)) srcBox.SelectedItem = preselect;

        var srcDbBox = new ComboBox { MinWidth = 200, PlaceholderText = "源库" };
        var dstDbBox = new ComboBox { MinWidth = 200, PlaceholderText = "目标库" };
        async Task LoadDbs(ComboBox box, ComboBox dbBox, TextBox outBox)
        {
            dbBox.Items.Clear();
            var p = box.SelectedItem as ConnectionProfile;
            if (p is null) return;
            try
            {
                using var prov = ProviderFactory.Create(p);
                foreach (var d in await prov.GetDatabases()) dbBox.Items.Add(d);
                if (dbBox.Items.Count > 0) dbBox.SelectedIndex = 0;
            }
            catch (Exception ex) { outBox.Text = "读取库列表失败：" + ex.Message; }
        }

        var tableList = new ListView { SelectionMode = ListViewSelectionMode.None, Height = 200 };
        var chkAll = new CheckBox { Content = "全选", IsChecked = true };
        var syncStruct = new CheckBox { Content = "结构同步（补缺失列，可选对齐类型）", IsChecked = true };
        var alignTypes = new CheckBox { Content = "对齐共有列类型（会改目标表列，谨慎）", IsChecked = false, Margin = new Thickness(20, 0, 0, 0) };
        var sendData = new CheckBox { Content = "传输数据（全量，不清空目标表）", IsChecked = true };
        alignTypes.IsEnabled = false;
        syncStruct.Checked += (_, _) => alignTypes.IsEnabled = true;
        syncStruct.Unchecked += (_, _) => alignTypes.IsEnabled = false;

        var loadBtn = new Button { Content = "加载表列表", CornerRadius = new CornerRadius(4) };
        var runBtn = new Button
        {
            Content = "开始传输",
            Background = Ant.Primary,
            Foreground = Ant.OnPrimary,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 170,
            FontFamily = (FontFamily)Application.Current.Resources["AntMono"],
            FontSize = 12,
            Text = "选好源 / 目标连接与库，加载表列表后点「开始传输」。",
        };

        IDbProvider? SrcProv()
        {
            var p = srcBox.SelectedItem as ConnectionProfile;
            return p is null ? null : ProviderFactory.Create(p);
        }
        IDbProvider? DstProv()
        {
            var p = dstBox.SelectedItem as ConnectionProfile;
            return p is null ? null : ProviderFactory.Create(p);
        }

        srcBox.SelectionChanged += async (_, _) => await LoadDbs(srcBox, srcDbBox, output);
        dstBox.SelectionChanged += async (_, _) => await LoadDbs(dstBox, dstDbBox, output);
        if (srcBox.SelectedItem is not null) await LoadDbs(srcBox, srcDbBox, output);
        if (dstBox.SelectedItem is not null) await LoadDbs(dstBox, dstDbBox, output);

        loadBtn.Click += async (_, _) =>
        {
            var prov = SrcProv();
            var db = srcDbBox.SelectedItem as string;
            if (prov is null || string.IsNullOrEmpty(db)) { output.Text = "请先选好源连接与源库。"; return; }
            tableList.Items.Clear();
            try
            {
                foreach (var t in await prov.GetChildren(db))
                {
                    var cb = new CheckBox { Content = t, IsChecked = true };
                    tableList.Items.Add(cb);
                }
                output.Text = $"已加载 {(tableList.Items.Count)} 个表 / 对象。";
            }
            catch (Exception ex) { output.Text = "读取表列表失败：" + ex.Message; }
        };
        chkAll.Checked += (_, _) => { foreach (var i in tableList.Items) if (i is CheckBox cb) cb.IsChecked = true; };
        chkAll.Unchecked += (_, _) => { foreach (var i in tableList.Items) if (i is CheckBox cb) cb.IsChecked = false; };

        runBtn.Click += async (_, _) =>
        {
            var src = SrcProv();
            var dst = DstProv();
            var sdb = srcDbBox.SelectedItem as string ?? "";
            var ddb = dstDbBox.SelectedItem as string ?? "";
            var tables = tableList.Items.OfType<CheckBox>().Where(c => c.IsChecked == true)
                .Select(c => (string)c.Content).ToList();
            if (src is null || dst is null || sdb.Length == 0 || ddb.Length == 0 || tables.Count == 0)
            {
                output.Text = "请把源 / 目标连接、库和要传的表都选好。";
                return;
            }
            runBtn.IsEnabled = false;
            int done = 0, failed = 0;
            output.Text = "开始传输…\n";
            foreach (var t in tables)
            {
                try
                {
                    var line = new List<string> { $"▶ {t}" };
                    // 1) 结构同步
                    List<ColumnSpec> srcCols;
                    try { srcCols = await src.GetColumns(sdb, t); }
                    catch { srcCols = new(); }
                    if (syncStruct.IsChecked == true && srcCols.Count > 0)
                    {
                        List<string> dstNames = new();
                        bool targetExists = true;
                        try { dstNames = (await dst.GetColumns(ddb, t)).Select(c => c.Name).ToList(); }
                        catch { targetExists = false; }
                        if (!targetExists)
                        {
                            var create = DataSync.CreateForTarget(t, srcCols, ((RelationalProvider)dst).Profile.Engine, dst.Quote);
                            await dst.Execute(ddb, create);
                            line.Add("  目标表不存在，已按源结构创建");
                        }
                        else
                        {
                            var (stmts, skipped) = DataSync.SyncColumns(t, srcCols, dstNames, ((RelationalProvider)dst).Profile.Engine, dst.Quote, alignTypes.IsChecked == true);
                            foreach (var s in stmts) await dst.Execute(ddb, s);
                            line.Add($"  结构同步：执行 {stmts.Count} 条语句" + (skipped.Count > 0 ? $"，跳过 {skipped.Count} 条（{skipped[0]}）" : ""));
                        }
                    }
                    // 2) 数据传输
                    if (sendData.IsChecked == true)
                    {
                        var qr = (await src.Execute(sdb, $"SELECT * FROM {src.Quote(t)}")).FirstOrDefault();
                        if (qr is null || qr.Rows.Count == 0) line.Add("  数据：源无行，跳过");
                        else
                        {
                            int inserted = 0;
                            foreach (var b in DataSync.InsertBatches(qr, dst.Quote(t), dst.Quote))
                            {
                                await dst.Execute(ddb, b);
                                inserted += b.Count(c => c == '\n');
                            }
                            line.Add($"  数据：写入 {inserted} 行");
                        }
                    }
                    output.Text += string.Join("\n", line) + "\n";
                    done++;
                }
                catch (Exception ex)
                {
                    failed++;
                    output.Text += $"▶ {t}  失败：{ex.Message}\n";
                }
            }
            output.Text += $"完成：成功 {done}，失败 {failed}。";
            runBtn.IsEnabled = true;
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 620, Margin = new Thickness(0, 0, 12, 0) };
        var rowA = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rowA.Children.Add(new TextBlock { Text = "源", VerticalAlignment = VerticalAlignment.Center });
        rowA.Children.Add(srcBox);
        rowA.Children.Add(srcDbBox);
        content.Children.Add(rowA);
        var rowB = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rowB.Children.Add(new TextBlock { Text = "目标", VerticalAlignment = VerticalAlignment.Center });
        rowB.Children.Add(dstBox);
        rowB.Children.Add(dstDbBox);
        content.Children.Add(rowB);
        var rowC = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        rowC.Children.Add(loadBtn);
        rowC.Children.Add(chkAll);
        content.Children.Add(rowC);
        content.Children.Add(tableList);
        content.Children.Add(syncStruct);
        content.Children.Add(alignTypes);
        content.Children.Add(sendData);
        content.Children.Add(runBtn);
        content.Children.Add(output);

        var dlg = new ContentDialog
        {
            Title = "数据传输与结构同步",
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            CloseButtonText = "关闭",
            XamlRoot = owner.Content.XamlRoot,
        };
        _ = dlg.ShowThemedAsync();
    }
}
