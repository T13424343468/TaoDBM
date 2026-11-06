using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TaoDBM.UI;

/// <summary>轻量信息弹窗。</summary>
public class MessageDialogHelper
{
    private readonly Window _owner;
    public MessageDialogHelper(Window owner) => _owner = owner;

    public async Task Show(string title, string text)
    {
        var dlg = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "关闭",
            XamlRoot = _owner.Content.XamlRoot,
        };
        await dlg.ShowThemedAsync();
    }
}
