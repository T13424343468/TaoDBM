using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TaoDBM.Core;

namespace TaoDBM.UI;

/// <summary>把 QueryResult 渲染成表格（纯代码构建，Ant 风格表头）。</summary>
public static class GridBuilder
{
    public static FrameworkElement Build(QueryResult r, Action<int>? onRowTap = null)
    {
        if (r.Columns.Count == 0)
        {
            string msg = r.Affected >= 0
                ? $"{(r.Message.Length > 0 ? r.Message + "　" : "")}执行成功，影响 {r.Affected} 行 · {r.Elapsed.TotalMilliseconds:N0} ms"
                : (r.Message.Length > 0 ? r.Message : "执行成功，无结果集。");
            var ok = new TextBlock
            {
                Text = msg,
                Margin = new Thickness(16, 12, 16, 12),
                Foreground = Ant.Success,
            };
            return new Border { Child = ok, Background = Ant.AltRow, CornerRadius = new CornerRadius(6), Margin = new Thickness(4, 8, 4, 8) };
        }

        var grid = new Grid { RowSpacing = 0 };
        int nCols = Math.Min(r.Columns.Count, 64);
        for (int i = 0; i < nCols; i++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Clamp(80 + r.Columns[i].Length * 8, 120, 280)) });

        // 表头
        var header = new Grid { Background = Ant.HeadBg };
        for (int i = 0; i < nCols; i++)
        {
            var tb = new TextBlock
            {
                Text = r.Columns[i],
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Ant.Text,
                Margin = new Thickness(10, 8, 10, 8),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var cell = new Border { Child = tb, BorderBrush = Ant.Border, BorderThickness = new Thickness(0, 0, 1, 1) };
            Grid.SetColumn(cell, i);
            header.Children.Add(cell);
        }
        Grid.SetRow(header, grid.RowDefinitions.Count);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(header);

        // 数据行（限 1000 行渲染）
        int shown = Math.Min(r.Rows.Count, 1000);
        for (int rowIdx = 0; rowIdx < shown; rowIdx++)
        {
            var row = r.Rows[rowIdx];
            var rowGrid = new Grid { Background = rowIdx % 2 == 1 ? Ant.AltRow : null };
            for (int i = 0; i < nCols; i++)
            {
                string text = Exporter.Format(i < row.Length ? row[i] : null);
                var tb = new TextBlock
                {
                    Text = text,
                    Margin = new Thickness(10, 6, 10, 6),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Ant.Text,
                };
                var cell = new Border { Child = tb, BorderBrush = Ant.Border, BorderThickness = new Thickness(0, 0, 1, 1) };
                Grid.SetColumn(cell, i);
                rowGrid.Children.Add(cell);
            }
            if (onRowTap != null)
            {
                int idx = rowIdx;
                rowGrid.Tapped += (_, _) => onRowTap(idx);
            }
            Grid.SetRow(rowGrid, grid.RowDefinitions.Count);
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(rowGrid);
        }

        var outer = new Grid();
        var scroll = new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        outer.Children.Add(scroll);

        if (r.Rows.Count > shown)
        {
            var note = new TextBlock
            {
                Text = $"共 {r.Rows.Count} 行，界面渲染前 {shown} 行（导出不受影响）。",
                Margin = new Thickness(12, 4, 4, 4),
                Foreground = Ant.TextSub,
            };
            var sp = new StackPanel { Orientation = Orientation.Vertical };
            sp.Children.Add(scroll);
            sp.Children.Add(note);
            outer.Children.Clear();
            outer.Children.Add(sp);
        }
        return outer;
    }
}
