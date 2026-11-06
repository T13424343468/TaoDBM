using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TaoDBM.UI;

/// <summary>设置弹窗：托盘 / 关闭主窗口行为（三选项归组附说明）。保存即生效并持久化。</summary>
public static class SettingsDialog
{
    public static async Task ShowAsync(Window owner)
    {
        var current = Core.AppPrefs.Load();

        var rbAsk = new RadioButton { GroupName = "CloseBehavior", Margin = new Thickness(0, 2, 0, 0) };
        var rbTray = new RadioButton { GroupName = "CloseBehavior" };
        var rbExit = new RadioButton { GroupName = "CloseBehavior" };
        switch (current)
        {
            case Core.CloseBehavior.Tray: rbTray.IsChecked = true; break;
            case Core.CloseBehavior.Exit: rbExit.IsChecked = true; break;
            default: rbAsk.IsChecked = true; break;
        }

        var desc = new TextBlock { Foreground = Ant.TextSub, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
        void Bind(RadioButton rb, string title, string note, Core.CloseBehavior kind)
        {
            rb.Content = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children =
                {
                    new TextBlock { Text = title },
                    new TextBlock { Text = note, Foreground = Ant.TextSub, FontSize = 12 },
                }
            };
            rb.Checked += (_, _) => desc.Text = note;
        }
        Bind(rbAsk, "每次询问", "点关闭时弹一次选择：最小化到托盘还是退出，可记住选择。", Core.CloseBehavior.Ask);
        Bind(rbTray, "最小化到托盘", "点关闭时收进系统托盘继续运行，点托盘图标随时回来。", Core.CloseBehavior.Tray);
        Bind(rbExit, "直接退出", "点关闭就退出程序，托盘图标一并消失。", Core.CloseBehavior.Exit);

        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        panel.Children.Add(new TextBlock { Text = "关闭主窗口时", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(rbAsk);
        panel.Children.Add(rbTray);
        panel.Children.Add(rbExit);
        panel.Children.Add(desc);
        panel.Children.Add(new TextBlock
        {
            Text = "设置保存即生效。主题、连接、收藏等沿用各自入口。",
            Foreground = Ant.TextSub, FontSize = 12, TextWrapping = TextWrapping.Wrap,
        });

        var dlg = new ContentDialog
        {
            Title = "设置",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = owner.Content.XamlRoot,
        };

        if (await dlg.ShowThemedAsync() == ContentDialogResult.Primary)
        {
            try
            {
                Core.AppPrefs.Save(
                    rbTray.IsChecked == true ? Core.CloseBehavior.Tray :
                    rbExit.IsChecked == true ? Core.CloseBehavior.Exit : Core.CloseBehavior.Ask);
            }
            catch (Exception ex) { Core.AppLog.Write($"设置保存失败: {ex.Message}"); }
        }
    }
}
