using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TaoDBM.Core;
using Windows.Storage.Pickers;

namespace TaoDBM.UI;

/// <summary>新建 / 编辑连接对话框（代码构建，Ant Design 风格）。</summary>
public class ConnectionDialog
{
    public ConnectionProfile Profile { get; }

    private readonly ComboBox _engine = new() { Header = "数据库类型", HorizontalAlignment = HorizontalAlignment.Stretch, MinWidth = 360 };
    private readonly TextBox _name = new() { Header = "连接名称" };
    private readonly TextBox _host = new() { Header = "主机" };
    private readonly TextBox _port = new() { Header = "端口" };
    private readonly TextBox _user = new() { Header = "用户名" };
    private readonly PasswordBox _pass = new() { Header = "密码" };
    private readonly TextBox _database = new() { Header = "默认数据库" };
    private readonly TextBox _file = new() { Header = "数据库文件路径（SQLite）" };
    private readonly TextBox _params = new() { Header = "附加参数（k=v;k2=v2）" };
    private readonly Button _tnsBtn = new() { Content = "从 tnsnames.ora 解析服务名", BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 4, 10, 4), Visibility = Visibility.Collapsed };
    private readonly ComboBox _tnsList = new() { Header = "TNS 网络服务名（选中后自动填主机/端口/服务名）", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
    private List<TnsNames.Entry> _tnsEntries = new();
    private readonly CheckBox _savePwd = new() { Content = "记住密码（本机加密保存）", IsChecked = true };
    private readonly TextBlock _hint = new() { Foreground = Ant.TextSub, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    // SSH 隧道（可选）
    private readonly CheckBox _ssh = new() { Content = "通过 SSH 跳板机连接（隧道）" };
    private readonly TextBox _sshHost = new() { Header = "SSH 主机", PlaceholderText = "跳板机地址" };
    private readonly TextBox _sshPort = new() { Header = "SSH 端口", PlaceholderText = "22" };
    private readonly TextBox _sshUser = new() { Header = "SSH 用户名" };
    private readonly PasswordBox _sshPass = new() { Header = "SSH 密码" };

    public ConnectionDialog(ConnectionProfile profile)
    {
        Profile = profile;
        foreach (var e in EngineInfo.All) _engine.Items.Add(EngineInfo.Name(e));
        _engine.SelectedIndex = (int)profile.Engine;
        _engine.SelectionChanged += (_, _) => OnEngineChanged();
        _name.Text = profile.DisplayName;
        _host.Text = profile.Host;
        _port.Text = profile.Port > 0 ? profile.Port.ToString() : "";
        _user.Text = profile.User;
        _pass.Password = profile.Password;
        _database.Text = profile.Database;
        _file.Text = profile.Database;
        _params.Text = profile.Parameters;
        _savePwd.IsChecked = profile.SavePassword;
        _sshHost.Text = profile.SshHost;
        _sshPort.Text = profile.SshPort > 0 ? profile.SshPort.ToString() : "22";
        _sshUser.Text = profile.SshUser;
        _sshPass.Password = profile.SshPassword;
        _ssh.IsChecked = !string.IsNullOrEmpty(profile.SshHost);
        _tnsList.SelectionChanged += (_, _) =>
        {
            int i = _tnsList.SelectedIndex;
            if (i < 0 || i >= _tnsEntries.Count) return;
            var e = _tnsEntries[i];
            _host.Text = e.Host;
            if (e.Port > 0) _port.Text = e.Port.ToString();
            if (e.Service.Length > 0) _params.Text = e.Service;
        };
        _ssh.Checked += (_, _) => OnSshChanged(true);
        _ssh.Unchecked += (_, _) => OnSshChanged(false);
        OnSshChanged(_ssh.IsChecked == true);
        OnEngineChanged();
    }

    private void OnSshChanged(bool on)
    {
        var vis = on ? Visibility.Visible : Visibility.Collapsed;
        _sshHost.Visibility = _sshPort.Visibility = _sshUser.Visibility = _sshPass.Visibility = vis;
    }

    private void OnEngineChanged()
    {
        var e = (DbEngine)_engine.SelectedIndex;
        bool fileBased = EngineInfo.IsFileBased(e);
        _host.Visibility = fileBased ? Visibility.Collapsed : Visibility.Visible;
        _port.Visibility = fileBased ? Visibility.Collapsed : Visibility.Visible;
        _user.Visibility = fileBased ? Visibility.Collapsed : Visibility.Visible;
        _pass.Visibility = fileBased ? Visibility.Collapsed : Visibility.Visible;
        _database.Visibility = fileBased ? Visibility.Collapsed : Visibility.Visible;
        _file.Visibility = fileBased ? Visibility.Visible : Visibility.Collapsed;
        _params.Visibility = e == DbEngine.Oracle ? Visibility.Visible : Visibility.Visible; // Oracle service name 也走这一行
        _params.Header = e == DbEngine.Oracle ? "服务名（如 orcl）" : "附加参数（k=v;k2=v2）";
        _tnsBtn.Visibility = e == DbEngine.Oracle ? Visibility.Visible : Visibility.Collapsed;
        if (e != DbEngine.Oracle) _tnsList.Visibility = Visibility.Collapsed;
        if (_port.Text.Length == 0)
        {
            int def = EngineInfo.DefaultPort(e);
            _port.Text = def > 0 ? def.ToString() : "";
        }
        else if (int.TryParse(_port.Text, out var curPort) && curPort > 0
                 && EngineInfo.All.Any(x => EngineInfo.DefaultPort(x) == curPort))
        {
            // 端口还是某个引擎的默认值（未自定义）→ 跟随新引擎换成对应默认端口；自定义端口保持不动
            int def2 = EngineInfo.DefaultPort(e);
            _port.Text = def2 > 0 ? def2.ToString() : "";
        }
        _hint.Text = e switch
        {
            DbEngine.SQLite => "本地文件即数据库，例如 D:\\data\\app.db；不存在时首次连接会自动创建。",
            DbEngine.Redis => "默认扫描 db0-db15；连接后左侧树中可切换库并浏览键。",
            DbEngine.MongoDB => "MongoDB 无需固定表结构，选中集合后可按 JSON 过滤器查询文档。",
            _ => $"默认端口 {EngineInfo.DefaultPort(e)}，可留空自动填充。",
        };
    }

    /// <summary>挂到窗口显示；用户点保存返回 true。</summary>
    public async Task<bool> ShowAsync(Window owner, bool isNew)
    {
        // 右侧留出滚动条带：浮层滚动条不再压在输入框右缘上（2026-11-03 真机截图反馈）
        var panel = new StackPanel { Spacing = 10, MinWidth = 400, Margin = new Thickness(0, 0, 12, 0) };
        panel.Children.Add(_engine);
        panel.Children.Add(_name);
        panel.Children.Add(_host);
        panel.Children.Add(_port);
        panel.Children.Add(_user);
        panel.Children.Add(_pass);
        panel.Children.Add(_database);
        panel.Children.Add(_file);
        panel.Children.Add(_params);
        panel.Children.Add(_tnsBtn);
        panel.Children.Add(_tnsList);
        _tnsBtn.Click += (_, _) => _ = PickTns(owner);
        panel.Children.Add(_savePwd);
        panel.Children.Add(_hint);

        var sshPanel = new StackPanel { Spacing = 8 };
        sshPanel.Children.Add(new TextBlock { Text = "SSH 隧道（可选，经跳板机连接内网数据库）", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        sshPanel.Children.Add(_ssh);
        sshPanel.Children.Add(_sshHost);
        sshPanel.Children.Add(_sshPort);
        sshPanel.Children.Add(_sshUser);
        sshPanel.Children.Add(_sshPass);
        panel.Children.Add(sshPanel);

        var scroll = new ScrollViewer { Content = panel, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var dlg = new ContentDialog
        {
            Title = isNew ? "新建连接" : "编辑连接",
            Content = scroll,
            PrimaryButtonText = "测试连接",
            SecondaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Secondary,
            XamlRoot = owner.Content.XamlRoot,
        };

        while (true)
        {
            var result = await dlg.ShowThemedAsync();
            Apply();
            if (result == ContentDialogResult.Primary)
            {
                bool ok = await TestConn();
                if (!ok) continue;   // 失败留在对话框
                // 测试通过后仍停留，用户可继续点保存
                var ok2 = new ContentDialog
                {
                    Title = "连接成功",
                    Content = new TextBlock { Text = "数据库连接正常，点击“保存”保存该连接。" },
                    CloseButtonText = "好的",
                    XamlRoot = owner.Content.XamlRoot,
                };
                await ok2.ShowThemedAsync();
                continue;
            }
            return result == ContentDialogResult.Secondary;
        }
    }

    /// <summary>选 tnsnames.ora 文件并解析；条目填进下拉，选中即回填主机/端口/服务名（避免嵌套对话框）。</summary>
    private async Task PickTns(Window owner)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".ora");
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
            var file = await picker.PickSingleFileAsync();
            if (file == null) return;
            var text = await Windows.Storage.FileIO.ReadTextAsync(file);
            _tnsEntries = TnsNames.Parse(text);
            _tnsList.Items.Clear();
            if (_tnsEntries.Count == 0)
            {
                _tnsList.Header = "未从该文件解析出网络服务名";
                _tnsList.Visibility = Visibility.Visible;
                return;
            }
            foreach (var e in _tnsEntries)
                _tnsList.Items.Add($"{e.Name} → {e.Host}:{(e.Port > 0 ? e.Port.ToString() : "默认")}/{(e.Service.Length > 0 ? e.Service : "?")}");
            _tnsList.Header = $"TNS 网络服务名（{file.Name}，共 {_tnsEntries.Count} 项）";
            _tnsList.SelectedIndex = -1;
            _tnsList.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            _hint.Text = "tnsnames.ora 解析失败：" + ex.Message;
        }
    }

    private void Apply()
    {
        Profile.Engine = _engine.SelectedIndex >= 0 ? (DbEngine)_engine.SelectedIndex : DbEngine.MySQL;
        Profile.DisplayName = string.IsNullOrWhiteSpace(_name.Text) ? EngineInfo.Name(Profile.Engine) : _name.Text.Trim();
        Profile.Host = _host.Text.Trim();
        Profile.Port = int.TryParse(_port.Text, out var p) ? p : 0;
        Profile.User = _user.Text.Trim();
        Profile.Password = _pass.Password;
        Profile.SavePassword = _savePwd.IsChecked == true;
        Profile.Parameters = _params.Text.Trim();
        Profile.Database = EngineInfo.IsFileBased(Profile.Engine) ? _file.Text.Trim() : _database.Text.Trim();
        if (Profile.Engine == DbEngine.Oracle && _params.Text.Trim().Length > 0 && !_params.Text.Contains('=')) { /* 服务名 */ }
        Profile.SshHost = _ssh.IsChecked == true ? _sshHost.Text.Trim() : "";
        Profile.SshPort = int.TryParse(_sshPort.Text, out var sp) ? sp : 22;
        Profile.SshUser = _sshUser.Text.Trim();
        Profile.SshPassword = _sshPass.Password;
    }

    private async Task<bool> TestConn()
    {
        try
        {
            using var prov = ProviderFactory.Create(Profile);
            await prov.TestConnection();
            return true;
        }
        catch (Exception ex)
        {
            var err = new ContentDialog
            {
                Title = "连接失败",
                Content = new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "知道了",
                XamlRoot = App.Shell!.Content.XamlRoot,
            };
            await err.ShowThemedAsync();
            return false;
        }
    }
}
