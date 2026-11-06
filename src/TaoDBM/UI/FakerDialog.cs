// 拟真数据对话框（v1.4.2 多表模式）：库内全部表逐表设定行数，拓扑顺序生成写入。
// 道法：生成与写入都在后台流转（无为而治）；失败退到"还能用"的提示（上善若水）；
// 被引用表先写入，引用列从库中取值保证外键不悬空（知常曰明）；同库同表同参必同值（可重放）。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MongoDB.Bson;
using TaoDBM.Core;

namespace TaoDBM.UI;

/// <summary>拟真数据 ContentDialog（纯代码构建，多表模式）。</summary>
public static class FakerDialog
{
    public static async Task Show(MainWindow owner, IDbProvider provider, string db, string table)
    {
        if (provider is RedisProvider)
        {
            await new MessageDialogHelper(owner).Show("拟真数据", "Redis 是键值存储，没有表结构，拟真数据功能不适用。");
            return;
        }

        // 读取库内全部表结构（读库在后台，含列读失败的容错）
        var (tables, readErrors) = await CollectTablesAsync(provider, db);
        if (tables.Count == 0)
        {
            string why = readErrors.Count > 0 ? "。\n读取失败的表：\n" + string.Join("\n", readErrors.Take(5)) : "。";
            await new MessageDialogHelper(owner).Show("拟真数据", "没有读到可生成的表结构" + why);
            return;
        }

        var infoText = new TextBlock
        {
            Text = $"目标库：{db} · {tables.Count} 个表（{EngineInfo.Name(provider.Profile.Engine)}）",
            FontSize = 13,
            Foreground = Ant.Text,
            Margin = new Thickness(0, 0, 0, 4),
        };
        if (readErrors.Count > 0)
            infoText.Text += $"\n（{readErrors.Count} 个表读取列结构失败，已跳过：{string.Join("、", readErrors.Select(e => e.Split(':')[0]).Take(3))}…）";

        // 每表一行：表名 + 行数输入（空 = 不生成；初始表默认 50）
        var rowBoxes = new Dictionary<string, TextBox>(StringComparer.OrdinalIgnoreCase);
        var rowsPanel = new StackPanel { Spacing = 4 };
        foreach (var t in tables)
        {
            var box = new TextBox { PlaceholderText = "0 或留空 = 不生成", MinWidth = 110 };
            if (string.Equals(t.Name, table, StringComparison.OrdinalIgnoreCase)) box.Text = "50";
            rowBoxes[t.Name] = box;
            var g = new Grid { ColumnSpacing = 8 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var name = new TextBlock
            {
                Text = $"{t.Name}（{t.Columns.Count} 列）",
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = Ant.Text,
            };
            Grid.SetColumn(name, 0);
            Grid.SetColumn(box, 1);
            g.Children.Add(name);
            g.Children.Add(box);
            rowsPanel.Children.Add(g);
        }
        var listHost = new ScrollViewer
        {
            Content = rowsPanel,
            MaxHeight = 260,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderBrush = Ant.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 6, 8, 6),
        };
        var listTitle = new TextBlock
        {
            Text = "每个表的生成行数（1 – 100000；留空不生成）",
            FontSize = 12,
            Foreground = Ant.TextSub,
        };

        var skipPkBox = new CheckBox { Content = "不写入主键列（自增主键推荐勾选）", IsChecked = true };
        var clearBox = new CheckBox { Content = "执行前先清空要生成的表（危险操作，请确认）", IsChecked = false };
        var previewBox = new TextBox
        {
            Header = "生成预览",
            IsReadOnly = true,
            FontFamily = (FontFamily)Application.Current.Resources["AntMono"],
            FontSize = 12,
            MinHeight = 110,
            MaxHeight = 200,
            TextWrapping = TextWrapping.NoWrap,
            AcceptsReturn = true,
            PlaceholderText = "点「生成预览」查看每表首行样例",
        };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
        var statusText = new TextBlock
        {
            FontSize = 12,
            Foreground = Ant.TextSub,
            TextWrapping = TextWrapping.Wrap,
            Text = provider is MongoProvider
                ? "将按拓扑顺序逐集合生成拟真文档写入。"
                : "将按拓扑顺序逐表生成（被引用表先写入），引用列自动取已写入父表的值。",
        };

        var previewBtn = new Button
        {
            Content = "生成预览",
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 4, 10, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        var panel = new StackPanel { Spacing = 10, MinWidth = 520, Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(infoText);
        panel.Children.Add(listTitle);
        panel.Children.Add(listHost);
        panel.Children.Add(skipPkBox);
        panel.Children.Add(clearBox);
        panel.Children.Add(previewBox);
        panel.Children.Add(previewBtn);
        panel.Children.Add(progress);
        panel.Children.Add(statusText);

        var dlg = new ContentDialog
        {
            Title = "拟真数据",
            Content = panel,
            PrimaryButtonText = "执行写入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.Content.XamlRoot,
        };

        var scroll = new ScrollViewer
        {
            Content = panel,
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        dlg.Content = scroll;

        bool previewed = false;

        // 预览：逐表解析行数并生成首行样例（池走库 Top100，与写入口径一致）
        previewBtn.Click += async (_, _) =>
        {
            var plan = ParsePlan(rowBoxes, tables);
            if (plan.Count == 0)
            {
                statusText.Text = "请先至少给一个表填行数（1 – 100000）。";
                statusText.Foreground = Ant.Warn;
                return;
            }
            previewBtn.IsEnabled = false;
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var (st, count) in plan)
                {
                    var colsOut = skipPkBox.IsChecked == true ? st.Columns.Where(c => !c.IsKey).ToList() : st.Columns;
                    if (colsOut.Count == 0) { sb.AppendLine($"{st.Name} · {count} 行（取消主键后无列可写，跳过）"); continue; }
                    var pools = await LoadPoolsAsync(provider, db, tables, st, null);
                    var genTable = new SeedTable { Name = st.Name, Columns = colsOut, Rows = 1 };
                    var rows = SeedPlan.GenerateRows(provider.Profile.Engine, genTable, _edges, pools, StableSeed(db, st.Name));
                    sb.AppendLine($"{st.Name} · {count} 行　样例：{Sample(colsOut, rows.Count > 0 ? rows[0] : new List<object?>())}");
                }
                previewBox.Text = sb.ToString().TrimEnd();
                statusText.Text = $"已生成 {plan.Count} 个表的预览，共 {plan.Sum(p => p.Count):N0} 行；确认后点「执行写入」。";
                statusText.Foreground = Ant.TextSub;
                previewed = true;
                dlg.IsPrimaryButtonEnabled = true;
            }
            catch (Exception ex)
            {
                statusText.Text = "生成失败：" + ex.Message;
                statusText.Foreground = Ant.Error;
                previewed = false;
                dlg.IsPrimaryButtonEnabled = false;
            }
            finally
            {
                previewBtn.IsEnabled = true;
            }
        };

        // 全部流程自定义：默认关闭一律拦截，成功才手动关
        dlg.PrimaryButtonClick += async (ContentDialog sender, ContentDialogButtonClickEventArgs args) =>
        {
            args.Cancel = true;
            if (!previewed)
            {
                statusText.Text = "请先点「生成预览」再执行。";
                statusText.Foreground = Ant.Warn;
                return;
            }
            var plan = ParsePlan(rowBoxes, tables);
            int total = plan.Sum(p => p.Count);
            if (total == 0)
            {
                statusText.Text = "没有要生成的表。";
                statusText.Foreground = Ant.Warn;
                return;
            }
            if (plan.Count == 0)
            {
                statusText.Text = "行数请填 1 – 100000 之间的整数。";
                statusText.Foreground = Ant.Warn;
                return;
            }
            sender.IsPrimaryButtonEnabled = false;
            progress.Visibility = Visibility.Visible;
            progress.Value = 0;
            statusText.Foreground = Ant.TextSub;
            IProgress<double> report = new Progress<double>(v => progress.Value = v);
            int doneRows = 0;

            try
            {
                var errors = new List<string>();
                await Task.Run(async () =>
                {
                    bool clear = clearBox.IsChecked == true;
                    bool skipPk = skipPkBox.IsChecked == true;
                    var engine = provider.Profile.Engine;
                    foreach (var (st, count) in plan)
                    {
                        if (count <= 0) continue;
                        var colsOut = skipPk ? st.Columns.Where(c => !c.IsKey).ToList() : st.Columns;
                        if (colsOut.Count == 0) { report.Report(100.0 * doneRows / total); continue; }
                        // 池：引用目标表此刻已写入（拓扑序保证）或本来就有数据 → 直接读库 Top100
                        var pools = await LoadPoolsAsync(provider, db, tables, st, null);
                        var genTable = new SeedTable { Name = st.Name, Columns = colsOut, Rows = count };
                        var rows = SeedPlan.GenerateRows(engine, genTable, _edges, pools, StableSeed(db, st.Name));
                        if (clear && provider is not MongoProvider)
                        {
                            var truncate = engine == DbEngine.MySQL
                                ? $"TRUNCATE TABLE {SeedPlan.QuoteIdent(engine, st.Name)};"
                                : $"DELETE FROM {SeedPlan.QuoteIdent(engine, st.Name)};";
                            try { await provider.Execute(db, truncate); }
                            catch (Exception ex) { errors.Add($"{st.Name} 清空失败：{ex.Message}"); }
                        }
                        if (provider is MongoProvider mp)
                        {
                            var docs = ToBsonDocs(colsOut, rows);
                            const int chunk = 500;
                            for (int i = 0; i < docs.Count; i += chunk)
                            {
                                await mp.InsertManyAsync(db, st.Name, docs.Skip(i).Take(chunk).ToList());
                                doneRows += Math.Min(chunk, docs.Count - i);
                                report.Report(100.0 * doneRows / total);
                            }
                        }
                        else
                        {
                            foreach (var s in SeedPlan.BuildInsertScripts(engine, st.Name, colsOut, rows))
                                await provider.Execute(db, s);
                            doneRows += rows.Count;
                            report.Report(100.0 * doneRows / total);
                        }
                    }
                });
                progress.Value = 100;
                sender.Hide();
                string tail = errors.Count > 0 ? "（部分表处理失败：" + string.Join("；", errors.Take(3)) + "）" : "";
                await new MessageDialogHelper(owner).Show("拟真数据", $"已写入 {doneRows:N0} 行拟真数据到 {db}。{tail}");
            }
            catch (Exception ex)
            {
                statusText.Text = "写入失败：" + ex.Message + $"（已完成 {doneRows:N0} 行）";
                statusText.Foreground = Ant.Error;
                sender.IsPrimaryButtonEnabled = true;
            }
        };

        dlg.IsPrimaryButtonEnabled = false;   // 必须先预览
        _ = dlg.ShowThemedAsync();
    }

    private static List<SeedEdge> _edges = new();   // 本次会话内库结构关联（预览与写入共用）

    /// <summary>解析各表行数（1 – 100000 或空 = 不生成），交给核心层按拓扑顺序排出计划。</summary>
    private static List<(SeedTable Table, int Count)> ParsePlan(Dictionary<string, TextBox> rowBoxes, List<SeedTable> tables)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, box) in rowBoxes)
        {
            var s = box.Text.Trim();
            if (s.Length == 0) continue;
            if (!int.TryParse(s, out int n) || n is < 1 or > 100000) continue;
            counts[name] = n;
        }
        return SeedPlan.PlanFromCounts(counts, tables, _edges);
    }

    /// <summary>收集库内全部表结构（读库在后台；读列失败的记录进错误清单）。</summary>
    private static async Task<(List<SeedTable> tables, List<string> errors)> CollectTablesAsync(IDbProvider provider, string db)
    {
        var tables = new List<SeedTable>();
        var errors = new List<string>();
        List<string> children;
        try { children = await provider.GetChildren(db); }
        catch (Exception ex) { return (tables, new List<string> { $"读取表清单失败：{ex.Message}" }); }
        foreach (var child in children.Take(200))
        {
            try
            {
                var cs = (await provider.GetColumns(db, child))
                    .Where(c => !c.Name.StartsWith("(document)", StringComparison.Ordinal)).ToList();
                if (cs.Count > 0) tables.Add(new SeedTable { Name = child, Columns = cs, Rows = 0 });
            }
            catch (Exception ex) { errors.Add($"{child}: {ex.Message}"); }
        }
        if (tables.Count > 0)
        {
            _edges = SeedPlan.BuildRelations(tables, null);
        }
        return (tables, errors);
    }

    /// <summary>为表 st 构建引用值池：引用目标表读库 Top100（写入后即可读到新值；拓扑序由调用方保证）。</summary>
    private static async Task<Dictionary<string, Dictionary<string, List<object?>>>> LoadPoolsAsync(
        IDbProvider provider, string db, List<SeedTable> tables, SeedTable st, Dictionary<string, List<List<object?>>>? generated)
    {
        var pools = new Dictionary<string, Dictionary<string, List<object?>>>(StringComparer.OrdinalIgnoreCase);
        var targets = _edges.Where(x => string.Equals(x.FromTable, st.Name, StringComparison.OrdinalIgnoreCase))
                            .Select(x => x.ToTable).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var e in targets)
        {
            var refTable = tables.FirstOrDefault(t => string.Equals(t.Name, e, StringComparison.OrdinalIgnoreCase));
            var pkCol = refTable?.Columns.FirstOrDefault(c => c.IsKey);
            if (refTable is null || pkCol is null) continue;
            try
            {
                var sql = SeedPlan.SelectTopSql(provider.Profile.Engine, e, pkCol.Name, 100);
                var qr = await provider.Execute(db, sql);
                var vals = new List<object?>();
                foreach (var row in qr.SelectMany(r => r.Rows).Take(100))
                    vals.Add(row.Length > 0 ? row[0] : null);
                if (vals.Count > 0)
                    pools[e] = new Dictionary<string, List<object?>>(StringComparer.OrdinalIgnoreCase) { [pkCol.Name] = vals };
            }
            catch { }
        }
        return pools;
    }

    private static string Sample(List<ColumnSpec> cols, List<object?> row)
    {
        if (row.Count == 0) return "（无）";
        var parts = new List<string>();
        for (int i = 0; i < cols.Count && i < row.Count && parts.Count < 6; i++)
        {
            var v = row[i] is null ? "NULL" : row[i]!.ToString();
            if (v is { Length: > 24 }) v = v[..24] + "…";
            parts.Add($"{cols[i].Name}={v}");
        }
        if (cols.Count > 6) parts.Add("…");
        return string.Join(", ", parts);
    }

    /// <summary>把拟真行转 Bson 文档（Mongo 专用；值类型逐列映射）。</summary>
    private static List<BsonDocument> ToBsonDocs(List<ColumnSpec> cols, List<List<object?>> rows)
    {
        var docs = new List<BsonDocument>();
        foreach (var row in rows)
        {
            var d = new BsonDocument();
            for (int i = 0; i < cols.Count && i < row.Count; i++)
            {
                var v = row[i];
                d[cols[i].Name] = v switch
                {
                    null => BsonNull.Value,
                    int n => new BsonInt32(n),
                    long l => new BsonInt64(l),
                    decimal dec => new BsonDouble((double)dec),
                    double dbl => new BsonDouble(dbl),
                    float f => new BsonDouble(f),
                    bool bb => new BsonBoolean(bb),
                    string s => new BsonString(s),
                    _ => new BsonString(v.ToString() ?? ""),
                };
            }
            docs.Add(d);
        }
        return docs;
    }

    /// <summary>稳定种子：同库同表必同值（可重放）。</summary>
    internal static int StableSeed(string db, string table)
    {
        var s = db + "." + table;
        int h = 17;
        foreach (var c in s) h = h * 31 + c;
        return h;
    }
}
