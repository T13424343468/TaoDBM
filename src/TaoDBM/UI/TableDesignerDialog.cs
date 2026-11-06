using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaoDBM.Core;

namespace TaoDBM.UI;

/// <summary>可视化表结构设计器：编辑列定义 → 预览 DDL → 执行。</summary>
public static class TableDesignerDialog
{
    private sealed class RowWidgets
    {
        public TextBox Name = new() { MinWidth = 90 };
        public TextBox Type = new() { MinWidth = 110 };
        public CheckBox Pk = new() { Content = "主键", MinWidth = 56, VerticalAlignment = VerticalAlignment.Center };
        public CheckBox Null = new() { Content = "可空", MinWidth = 56, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        public CheckBox Inc = new() { Content = "自增", MinWidth = 56, VerticalAlignment = VerticalAlignment.Center };
        public Button Del = new() { Content = "✕", Padding = new Thickness(6, 2, 6, 2), CornerRadius = new CornerRadius(4) };
    }

    /// <summary>table 为 null 表示新建表；existing 传入旧列定义（改表用）。</summary>
    public static async Task Show(MainWindow owner, IDbProvider prov, string db, string? table, List<ColumnSpec>? existing)
    {
        if (prov is not RelationalProvider)
        {
            await new MessageDialogHelper(owner).Show("设计表", "仅关系型数据库支持可视化建表 / 改表。");
            return;
        }

        var rows = new List<RowWidgets>();
        var rowsHost = new StackPanel { Spacing = 4 };

        void AddRow(TableColumnDesign? c)
        {
            var w = new RowWidgets();
            if (c != null)
            {
                w.Name.Text = c.Name;
                w.Type.Text = c.SqlType;
                w.Pk.IsChecked = c.IsPrimaryKey;
                w.Null.IsChecked = c.Nullable;
                w.Inc.IsChecked = c.AutoIncrement;
            }
            w.Del.Click += (_, _) =>
            {
                if (rows.Count > 1 && w.Name.Parent is FrameworkElement fe)
                {
                    rowsHost.Children.Remove(fe);
                    rows.Remove(w);
                }
            };
            rows.Add(w);
            rowsHost.Children.Add(ColumnRow(w));
        }

        if (existing is { Count: > 0 })
            foreach (var c in existing)
                AddRow(new TableColumnDesign { Name = c.Name, SqlType = c.DataType, Nullable = c.Nullable, IsPrimaryKey = c.IsKey });
        else AddRow(null);

        var nameBox = new TextBox
        {
            PlaceholderText = "表名",
            Text = table ?? "",
            IsReadOnly = table != null,
            MinWidth = 220,
        };

        var preview = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = (FontFamily)Application.Current.Resources["AntMono"],
            FontSize = 12,
            Height = 170,
            Text = "点「生成 SQL」预览将执行的语句。",
        };

        var addBtn = new Button { Content = "＋ 加一列", CornerRadius = new CornerRadius(4) };
        addBtn.Click += (_, _) => AddRow(null);

        var genBtn = new Button
        {
            Content = "生成 SQL",
            Background = Ant.Primary,
            Foreground = Ant.OnPrimary,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        var runBtn = new Button { Content = "执行", CornerRadius = new CornerRadius(4) };

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(addBtn);
        actions.Children.Add(genBtn);
        actions.Children.Add(runBtn);

        var head = new TextBlock
        {
            Text = $"列定义（引擎：{prov.EngineName}{(table != null ? " · 改表模式" : " · 新建表")}）",
            FontSize = 12,
            Foreground = Ant.TextSub,
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 640, Margin = new Thickness(0, 4, 0, 0) };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        nameRow.Children.Add(new TextBlock { Text = "表名", VerticalAlignment = VerticalAlignment.Center });
        nameRow.Children.Add(nameBox);
        content.Children.Add(nameRow);
        content.Children.Add(head);
        content.Children.Add(rowsHost);
        content.Children.Add(actions);
        content.Children.Add(preview);

        var dlg = new ContentDialog
        {
            Title = table == null ? $"新建表 · {db}" : $"改表结构 · {table}",
            Content = content,
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.Content.XamlRoot,
        };

        TableDesign Read()
        {
            var t = new TableDesign { TableName = nameBox.Text.Trim() };
            foreach (var w in rows)
                t.Columns.Add(new TableColumnDesign
                {
                    Name = w.Name.Text.Trim(),
                    SqlType = w.Type.Text.Trim(),
                    Nullable = w.Null.IsChecked != false,
                    IsPrimaryKey = w.Pk.IsChecked == true,
                    AutoIncrement = w.Inc.IsChecked == true,
                    DefaultValue = "",
                });
            return t;
        }

        async Task<(List<string> Stmts, List<string> Skipped, string Preview)> BuildAsync()
        {
            var t = Read();
            if (t.TableName.Length == 0 || t.Columns.All(c => c.Name.Length == 0))
                return (new(), new(), "请先填写表名和至少一列。");
            if (table == null)
            {
                var sql = TableDdl.BuildCreate(t, prov.Profile.Engine, s => prov.Quote(s));
                return (TableDdl.AsStatements(sql), new(), sql);
            }
            try
            {
                var olds = (await prov.GetColumns(db, table)).Select(c => c.Name).ToList();
                var (stmts, skipped) = TableDdl.BuildAlter(t, olds, prov.Profile.Engine, s => prov.Quote(s));
                var text = stmts.Count == 0
                    ? "没有需要执行的变更。\n" + string.Join("\n", skipped)
                    : string.Join(";\n\n", stmts) + ";\n" + (skipped.Count > 0 ? "\n提示：\n  " + string.Join("\n  ", skipped) : "");
                return (stmts, skipped, text);
            }
            catch (Exception ex)
            {
                return (new(), new(), "读取现有结构失败：" + ex.Message);
            }
        }

        async void OnGenerate()
        {
            var (_, _, text) = await BuildAsync();
            preview.Text = text;
        }

        async void OnRun()
        {
            var (stmts, _, text) = await BuildAsync();
            preview.Text = text;
            if (stmts.Count == 0) return;
            runBtn.IsEnabled = false;
            try
            {
                var list = await prov.Execute(db, string.Join(";\n", stmts) + ";");
                var errs = list.Where(r => r.Message.StartsWith("错误", StringComparison.Ordinal)).ToList();
                preview.Text = errs.Count == 0
                    ? $"已执行 {stmts.Count} 条语句，成功。请在左侧树点「刷新」查看结果。"
                    : $"执行出错（{errs.Count} 条失败）：\n" + string.Join("\n", errs.Select(r => r.Message));
                QueryHistory.Add(prov.EngineName, db, "-- 表结构设计：" + Read().TableName + "\n" + string.Join(";\n", stmts));
            }
            catch (Exception ex)
            {
                preview.Text = "执行失败：" + ex.Message;
            }
            finally { runBtn.IsEnabled = true; }
        }

        genBtn.Click += (_, _) => OnGenerate();
        runBtn.Click += (_, _) => OnRun();
        _ = dlg.ShowThemedAsync();
    }

    private static Grid ColumnRow(RowWidgets w)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Put(FrameworkElement el, int col)
        {
            el.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }
        Put(w.Name, 0);
        Put(w.Type, 1);
        Put(w.Pk, 2);
        Put(w.Null, 3);
        Put(w.Inc, 4);
        Put(w.Del, 5);
        return g;
    }
}
