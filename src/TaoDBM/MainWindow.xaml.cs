using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using TaoDBM.Core;
using TaoDBM.UI;

namespace TaoDBM;

public enum NodeKind { Connection, Database, Table, Column, Key }

/// <summary>树节点附加信息。</summary>
public class NodeInfo
{
    public NodeKind Kind;
    public ConnectionProfile Profile = new();
    public IDbProvider? Provider;
    public string Db = "";
    public string Name = "";
    public bool Loaded;
}

public partial class MainWindow : Window
{
    private readonly List<ConnectionProfile> _profiles = new();
    private readonly Dictionary<TreeViewNode, NodeInfo> _nodeMap = new();
    /// <summary>打开着的工作标签（会话保存用；欢迎页不在内）。</summary>
    private readonly Dictionary<TabViewItem, UI.WorkTab> _workTabs = new();

    /// <summary>版本号来自程序集版本，让「装的是哪版」一眼可见（窗口标题与顶栏同源）。</summary>
    private static string VersionLabel()
    {
        var v = typeof(MainWindow).Assembly.GetName().Version;
        return v == null ? "" : $" v{v.Major}.{v.Minor}.{v.Build}";
    }

    public MainWindow()
    {
        InitializeComponent();
        Title = "TaoDBM" + VersionLabel();
        AppTitleText.Text = Title;
        Core.AppLog.Write($"TaoDBM 启动 {Title}");
        UI.Ant.ThemeApplied += PinTitleBar;
        UI.Ant.ThemeApplied += PinRootTheme;
        PinTitleBar(Core.ThemePalette.Get(Core.ThemeStore.Load()));
        PinRootTheme(Core.ThemePalette.Get(Core.ThemeStore.Load()));
        // 顶栏两处下拉菜单是弹层，挂窗口内容之外，须逐个钉明暗象（换肤后再开也随象）
        BtnLogo.Flyout.PinTheme();
        BtnTheme.Flyout.PinTheme();
        UpdateThemeChecks();
        var trayIcoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TaoDBM.ico");
        try
        {
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "TaoDBM.png");
            if (System.IO.File.Exists(iconPath))
            {
                var bmp = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(iconPath));
                LogoImg.Source = bmp;
            }
            if (System.IO.File.Exists(trayIcoPath))
                AppWindow.SetIcon(trayIcoPath);
        }
        catch { }
        // 托盘创建不再以图标文件存在为前提（2026-11-03 真机二轮反馈）：图标缺失时 TrayIcon 内部回落系统图标，
        // 绝不因资源缺失整段静默跳过导致托盘整个没有。
        try { StartTray(trayIcoPath); }
        catch (Exception ex) { Core.AppLog.Write($"托盘: 创建异常 {ex.Message}"); }
        // 标题栏保持系统默认（Standard）；在此显式设置会触发 WindowsAppSDK 0x8007139F 崩溃，勿再加回。
        AppWindow.Closing += OnAppWinClosing;

        LoadConnections();
        AddWelcomeTab();
        RestoreSession();
        Closed += (_, _) => { SaveSession(); SshTunnel.StopAll(); };
    }

    // ---------- 托盘与关闭行为 ----------

    private UI.TrayIcon? _tray;
    private bool _allowClose;
    private bool _balloonShown;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _trayRetry;

    /// <summary>创建托盘：消息托管在主窗句柄上（子类化转发），回调在本 UI 线程派发，动作无需再切线程。</summary>
    private void StartTray(string icoPath)
    {
        _tray = new UI.TrayIcon(WinRT.Interop.WindowNative.GetWindowHandle(this), icoPath, () => Core.ThemeStore.Load());
        _tray.RecentProvider = () => _profiles.Take(UI.TrayIcon.RecentMax)
            .Select(p => (p.Id, p.DisplayName)).ToList();
        _tray.OpenRequested += ShowFromTray;
        _tray.NewConnectionRequested += () => OnNewConn(this, new RoutedEventArgs());
        _tray.ThemeRequested += name => SwitchTheme(name);
        _tray.SettingsRequested += () => _ = UI.SettingsDialog.ShowAsync(this);
        _tray.ExitRequested += ExitApp;
        _tray.RecentRequested += TrayConnect;
        if (!_tray.IsAdded) StartTrayRetry();   // 没一次挂上就短周期重试，不静默放弃
    }

    /// <summary>托盘未挂上时的自动重试（启动早期桌面可能未就绪）：最多 3 次、每次 2 秒，结果如实反映到 IsAdded。</summary>
    private void StartTrayRetry()
    {
        _trayRetry?.Stop();
        var timer = DispatcherQueue.CreateTimer();
        _trayRetry = timer;
        int tries = 0;
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (_, _) =>
        {
            if (_tray is null || _tray.IsAdded || tries >= 3) { timer.Stop(); return; }
            tries++;
            Core.AppLog.Write($"托盘: 自动重试 {tries}/3");
            _tray.RetryAdd();
        };
        timer.Start();
    }

    /// <summary>托盘「我的连接」：回主窗并选中、展开该连接（展开即连库拉库列表）。</summary>
    private void TrayConnect(string id)
    {
        ShowFromTray();
        foreach (var kv in _nodeMap)
        {
            if (kv.Value.Kind != NodeKind.Connection || kv.Value.Profile.Id != id) continue;
            ConnTree.SelectedItem = kv.Key;
            kv.Key.IsExpanded = true;
            return;
        }
    }

    private void ShowFromTray()
    {
        AppWindow.Show();
        Activate();
    }

    private void HideToTray()
    {
        try { AppWindow.Hide(); }
        catch { return; }   // 隐藏失败就留在原地，绝不玩消失
        if (!_balloonShown)
        {
            _balloonShown = true;
            _tray?.ShowInfo("TaoDBM 已最小化到托盘", "点托盘图标可随时回来。");
        }
    }

    private void ExitApp()
    {
        _allowClose = true;
        _tray?.Dispose();
        _tray = null;
        Close();
    }

    /// <summary>关闭拦截：按设置走「最小化到托盘 / 直接退出 / 每次询问」；托盘没挂上时不拦——关窗即退出，绝不玩消失。</summary>
    private void OnAppWinClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose || _tray is null || !_tray.IsAdded)
        {
            // 诊断信息一律走日志不弹窗（2026-11-03 用户定调）：托盘没挂上时如实记录，关窗按退出处理
            Core.AppLog.Write(_tray is null
                ? "关窗: 托盘未创建，按退出处理"
                : !_tray.IsAdded ? "关窗: 托盘未挂上，按退出处理（原因见前序托盘日志）" : "关窗: 退出");
            return;
        }
        args.Cancel = true;
        switch (Core.AppPrefs.Load())
        {
            case Core.CloseBehavior.Tray: HideToTray(); break;
            case Core.CloseBehavior.Exit: ExitApp(); break;
            default: _ = AskCloseAsync(); break;
        }
    }

    private async Task AskCloseAsync()
    {
        var remember = new CheckBox { Content = "记住我的选择，下次不再问" };
        var dlg = new ContentDialog
        {
            Title = "要最小化到托盘，还是退出？",
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "最小化到托盘后 TaoDBM 在后台继续运行，点托盘图标随时回来。", TextWrapping = TextWrapping.Wrap },
                    remember,
                }
            },
            PrimaryButtonText = "最小化到托盘",
            SecondaryButtonText = "退出",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot,
        };
        var result = await dlg.ShowThemedAsync();
        if (result == ContentDialogResult.None) return;   // 取消：留在原状
        if (remember.IsChecked == true)
            Core.AppPrefs.Save(result == ContentDialogResult.Primary ? Core.CloseBehavior.Tray : Core.CloseBehavior.Exit);
        if (result == ContentDialogResult.Primary) HideToTray();
        else ExitApp();
    }

    private void OnSettings(object sender, RoutedEventArgs e)
    {
        _ = UI.SettingsDialog.ShowAsync(this);
    }

    public void SetStatus(string s) => StatusText.Text = s;

    // ---------- 连接管理 ----------

    private void LoadConnections()
    {
        _profiles.Clear();
        _profiles.AddRange(ConnectionStore.Load());
        RebuildRoots();
    }

    private void RebuildRoots()
    {
        ConnTree.RootNodes.Clear();
        _nodeMap.Clear();
        foreach (var p in _profiles)
        {
            var info = new NodeInfo { Kind = NodeKind.Connection, Profile = p, Name = p.DisplayName };
            var node = new TreeViewNode
            {
                Content = $"🗄️ {p.DisplayName}（{EngineInfo.Name(p.Engine)}）",
                IsExpanded = false,
            };
            node.Children.Add(new TreeViewNode { Content = "…" }); // 占位，展开时填充
            _nodeMap[node] = info;
            ConnTree.RootNodes.Add(node);
        }
        SetStatus($"共 {_profiles.Count} 个连接");
    }

    private async void OnNewConn(object sender, RoutedEventArgs e)
    {
        var dlg = new UI.ConnectionDialog(new ConnectionProfile());
        bool saved = await dlg.ShowAsync(this, isNew: true);
        if (saved)
        {
            _profiles.Add(dlg.Profile);
            ConnectionStore.Save(_profiles);
            RebuildRoots();
            SetStatus($"已添加连接 {dlg.Profile.DisplayName}");
        }
    }

    private async void OnEditConn(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        if (info?.Kind != NodeKind.Connection) { await InfoAsync("请先在左侧树中选中一个连接。"); return; }
        var dlg = new UI.ConnectionDialog(info.Profile);
        bool saved = await dlg.ShowAsync(this, isNew: false);
        if (saved)
        {
            ConnectionStore.Save(_profiles);
            RebuildRoots();
        }
    }

    private void OnThemeInk(object sender, RoutedEventArgs e) => SwitchTheme("ink");

    /// <summary>非客户区随象而染：系统标题栏与三颗门钮钉成当前主题色（TaoZip v60 教训）。</summary>
    private void PinTitleBar(IReadOnlyDictionary<string, string> pal)
    {
        try
        {
            var bar = AppWindow.TitleBar;
            bar.BackgroundColor = UI.Ant.Brush(pal["AntHeadBg"]).Color;
            bar.ForegroundColor = UI.Ant.Brush(pal["AntText"]).Color;
            bar.ButtonBackgroundColor = UI.Ant.Brush(pal["AntHeadBg"]).Color;
            bar.ButtonForegroundColor = UI.Ant.Brush(pal["AntText"]).Color;
            bar.ButtonHoverBackgroundColor = UI.Ant.Brush(pal["AntBorder"]).Color;
            bar.ButtonHoverForegroundColor = UI.Ant.Brush(pal["AntText"]).Color;
        }
        catch { }
    }

    /// <summary>客户区根元素重钉系统明暗象：内容对话框、菜单、下拉、输入框等系统控件随象而染
    /// （弹层从所在元素取象，根钉住即全树继承，切肤即时生效无需重启）。</summary>
    private void PinRootTheme(IReadOnlyDictionary<string, string> pal)
    {
        if (Content is FrameworkElement root)
            root.RequestedTheme = UI.Ant.ElementTheme;
    }

    private void OnThemeGray(object sender, RoutedEventArgs e) => SwitchTheme("gray");
    private void OnThemeNight(object sender, RoutedEventArgs e) => SwitchTheme("night");

    /// <summary>切主题：改画刷颜色即时全窗口生效，选择持久化（Tao 系列水墨默认）。</summary>
    private void SwitchTheme(string name)
    {
        try
        {
            UI.Ant.ApplyTheme(name);
            Core.ThemeStore.Save(name);
            SetStatus(name switch { "gray" => "主题：天青", "night" => "主题：墨夜", _ => "主题：水墨" });
            UpdateThemeChecks();
        }
        catch { }
    }

    /// <summary>主题菜单对勾随象刷新（TaoPad 同款：当前主题项左侧带勾选）。</summary>
    private void UpdateThemeChecks()
    {
        var cur = Core.ThemeStore.Load();
        ThemeInk.IsChecked = cur == "ink";
        ThemeSky.IsChecked = cur == "gray";
        ThemeNight.IsChecked = cur == "night";
    }

    private async void OnDelConn(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        if (info?.Kind != NodeKind.Connection) { await InfoAsync("请先选中一个连接。"); return; }
        await DeleteConnection(info);
    }

    /// <summary>删除连接：确认 → 断开隧道 → 关闭该连接已打开的页签 → 移除并重建树。</summary>
    private async Task DeleteConnection(NodeInfo info)
    {
        bool ok = await ConfirmAsync($"确定删除连接“{info.Profile.DisplayName}”吗？");
        if (!ok) return;
        SshTunnel.StopProfile(info.Profile.Id);
        info.Provider?.Dispose();
        var dead = _workTabs.Where(kv => kv.Value.SessionConnectionId == info.Profile.Id).Select(kv => kv.Key).ToList();
        foreach (var tab in dead) { _workTabs.Remove(tab); WorkTabs.TabItems.Remove(tab); }
        _profiles.Remove(info.Profile);
        ConnectionStore.Save(_profiles);
        RebuildRoots();
        SetStatus($"已删除连接 {info.Profile.DisplayName}");
    }

    // ---------- 树交互 ----------

    private NodeInfo? SelectedInfo()
    {
        var node = ConnTree.SelectedNode;
        return node is null ? null : _nodeMap.GetValueOrDefault(node);
    }

    private TreeViewNode? SelectedNode => ConnTree.SelectedNode;

    /// <summary>树节点右键菜单：按节点类型给最常用操作（v1.4.2 新增，连接可在此关闭 / 删除）。</summary>
    private void OnTreeRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var node = (e.OriginalSource as FrameworkElement)?.DataContext as TreeViewNode;
        if (node is null || !_nodeMap.TryGetValue(node, out var info)) return;
        ConnTree.SelectedItem = node;   // 右键即选中，菜单动作与选中态一致
        var fly = new MenuFlyout();
        fly.PinTheme();   // 右键菜单是弹层，挂窗口内容之外，钉当前明暗象
        void Item(string text, RoutedEventHandler h, bool danger = false)
        {
            var mi = new MenuFlyoutItem { Text = text };
            if (danger) mi.Foreground = Ant.Error;   // 跟主题走的警示红，不写死颜色
            mi.Click += h;
            fly.Items.Add(mi);
        }
        switch (info.Kind)
        {
            case NodeKind.Connection:
                Item("展开连接", (_, _) => { if (!node.IsExpanded) node.IsExpanded = true; });
                Item("编辑连接", async (_, _) => await EditConnection(info));
                Item("关闭连接", (_, _) => CloseConnection(info, node));
                Item("删除连接", async (_, _) => await DeleteConnection(info), danger: true);
                break;
            case NodeKind.Database:
                Item("刷新", (_, _) => RefreshNode(node));
                Item("新建查询", (_, _) => OpenQueryTab(info));
                Item("查看数据", (_, _) => OpenTableTab(info));
                Item("设计表", (_, _) => OnDesignSel(this, new RoutedEventArgs()));
                Item("备份 / 还原", (_, _) => OnBackupSel(this, new RoutedEventArgs()));
                Item("ER 图", (_, _) => OnErdSel(this, new RoutedEventArgs()));
                break;
            case NodeKind.Table:
                Item("刷新", (_, _) => RefreshNode(node));
                Item("新建查询", (_, _) => OpenQueryTab(info));
                Item("查看数据", (_, _) => OpenTableTab(info));
                Item("设计表", (_, _) => OnDesignSel(this, new RoutedEventArgs()));
                Item("拟真数据", (_, _) => OpenFaker(info));
                break;
        }
        fly.ShowAt(e.OriginalSource as FrameworkElement ?? ConnTree);
    }

    private void RefreshNode(TreeViewNode node)
    {
        node.Children.Clear();
        if (_nodeMap.TryGetValue(node, out var i)) i.Loaded = false;
        node.Children.Add(new TreeViewNode { Content = "…" });
    }

    /// <summary>编辑连接（树右键与 Logo 菜单共用）。</summary>
    private async Task EditConnection(NodeInfo info)
    {
        var dlg = new UI.ConnectionDialog(info.Profile);
        bool saved = await dlg.ShowAsync(this, isNew: false);
        if (saved)
        {
            ConnectionStore.Save(_profiles);
            RebuildRoots();
        }
    }

    /// <summary>关闭连接：断开 provider、收起子树、关闭该连接的 SSH 隧道，连接配置保留可再展开。</summary>
    private void CloseConnection(NodeInfo info, TreeViewNode node)
    {
        SshTunnel.StopProfile(info.Profile.Id);
        info.Provider?.Dispose();
        info.Provider = null;
        info.Loaded = false;
        node.Children.Clear();
        node.Children.Add(new TreeViewNode { Content = "…" });
        node.IsExpanded = false;
        SetStatus($"已关闭连接 {info.Profile.DisplayName}（配置保留，展开即重新连接）");
    }

    /// <summary>树右键打开拟真数据（多表模式）。</summary>
    private void OpenFaker(NodeInfo info) => _ = UI.FakerDialog.Show(this, info.Provider!, info.Db, info.Name);

    private async void OnNodeExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        var info = _nodeMap.GetValueOrDefault(node);
        if (info is null || info.Loaded) return;

        // 占位子节点 → 清空并懒加载
        bool wasPlaceholder = node.Children.Count == 1 && !_nodeMap.ContainsKey(node.Children[0]);
        if (!wasPlaceholder) return;
        node.Children.Clear();
        info.Loaded = true;
        try
        {
            switch (info.Kind)
            {
                case NodeKind.Connection:
                    SetStatus($"正在连接 {info.Profile.DisplayName}…");
                    info.Provider ??= ProviderFactory.Create(info.Profile);
                    var dbs = await info.Provider.GetDatabases();
                    foreach (var db in dbs)
                    {
                        var childInfo = new NodeInfo
                        {
                            Kind = NodeKind.Database,
                            Profile = info.Profile, Provider = info.Provider, Name = db, Db = db,
                        };
                        var childNode = new TreeViewNode { Content = $"📁 {db}" };
                        childNode.Children.Add(new TreeViewNode { Content = "…" });
                        _nodeMap[childNode] = childInfo;
                        node.Children.Add(childNode);
                    }
                    if (dbs.Count == 0) node.Children.Add(new TreeViewNode { Content = "（空）" });
                    SetStatus($"{info.Profile.DisplayName}：{dbs.Count} 个库");
                    break;

                case NodeKind.Database:
                    var prov = info.Provider!;
                    var children = await prov.GetChildren(info.Db);
                    foreach (var c in children)
                    {
                        var cInfo = new NodeInfo
                        {
                            Kind = NodeKind.Table, // Mongo 集合 / Redis 键统一走 Table 语义
                            Profile = info.Profile, Provider = prov, Name = c, Db = info.Db,
                        };
                        var cNode = new TreeViewNode { Content = (prov.IsNoSql ? "🗂️ " : "📋 ") + c };
                        _nodeMap[cNode] = cInfo;
                        node.Children.Add(cNode);
                    }
                    if (children.Count == 0) node.Children.Add(new TreeViewNode { Content = "（空）" });
                    SetStatus($"{info.Name}：{children.Count} 个{(prov.IsNoSql ? prov.BrowseLabel : "表")}");
                    break;
            }
        }
        catch (Exception ex)
        {
            info.Loaded = false;
            node.Children.Clear();
            node.Children.Add(new TreeViewNode { Content = "⚠ " + ex.Message + "（点击刷新重试）" });
            SetStatus("加载失败：" + ex.Message);
        }
    }

    private void OnTreeSelection(TreeView sender, TreeViewSelectionChangedEventArgs args)
    {
        var info = SelectedInfo();
        if (info is null) return;
        switch (info.Kind)
        {
            case NodeKind.Connection:
                if (info.Profile.Engine == DbEngine.Redis)
                    (info.Provider as RedisProvider)!.CurrentKey = null;
                break;
            case NodeKind.Table when info.Provider is RedisProvider rp:
                rp.CurrentKey = info.Name;
                break;
            case NodeKind.Table when info.Provider is MongoProvider mp:
                mp.CurrentCollection = info.Name;
                break;
        }
    }

    // ---------- 工具栏动作 ----------

    private void OnRefreshSel(object sender, RoutedEventArgs e)
    {
        var node = SelectedNode;
        if (node is null) { RebuildRoots(); return; }
        if (_nodeMap.TryGetValue(node, out var info) && info.Kind == NodeKind.Connection) { RebuildRoots(); return; }
        if (!_nodeMap.TryGetValue(node, out var i2)) return;
        i2.Loaded = false;
        node.Children.Clear();
        node.Children.Add(new TreeViewNode { Content = "…" });
    }

    private void OnNewQuerySel(object sender, RoutedEventArgs e) => OpenQueryTab(SelectedInfo());

    private void OnBrowseSel(object sender, RoutedEventArgs e) => OpenTableTab(SelectedInfo());

    /// <summary>选中表 → 改表结构；选中库 → 新建表；其他提示。</summary>
    private async void OnDesignSel(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        if (info?.Provider is null) { await InfoAsync("请先选中库或表。"); return; }
        if (info.Provider.IsNoSql) { await InfoAsync("MongoDB / Redis 无表结构，不支持可视化设计。"); return; }
        if (info.Kind == NodeKind.Table)
        {
            List<ColumnSpec> cols;
            try { cols = await info.Provider.GetColumns(info.Db, info.Name); }
            catch (Exception ex) { await InfoAsync("读取表结构失败：" + ex.Message); return; }
            _ = UI.TableDesignerDialog.Show(this, info.Provider, info.Db, info.Name, cols);
        }
        else if (info.Kind == NodeKind.Database)
        {
            _ = UI.TableDesignerDialog.Show(this, info.Provider, info.Name, null, null);
        }
        else await InfoAsync("请选中库或表后再设计表结构。");
    }

    /// <summary>备份 / 还原：MySQL/PG 走官方 CLI，SQL Server 走 T-SQL，SQLite 文件复制。</summary>
    private async void OnBackupSel(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        if (info is null || info.Provider is null) { await InfoAsync("请先选中一个连接（或库、表）再备份 / 还原。"); return; }
        await UI.BackupDialog.Show(this, info);
    }

    /// <summary>数据传输与结构同步：源连接 → 目标连接，补结构、搬数据。</summary>
    private async void OnSyncSel(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        await UI.DataSyncDialog.Show(this, _profiles, info?.Profile);
    }

    /// <summary>模型设计（ER 图）：库内表结构成图，拖拽排布，导出 PNG，保存 / 打开布局。</summary>
    private async void OnErdSel(object sender, RoutedEventArgs e)
    {
        var info = SelectedInfo();
        await UI.ErdDialog.Show(this, _profiles, info?.Profile);
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        _ = new UI.MessageDialogHelper(this).Show(
            "TaoDBM v1.0",
            "WinUI3 + .NET 8 · Ant Design 视觉\n支持 MySQL / PostgreSQL / SQL Server / SQLite / Oracle / MongoDB / Redis\n\n道生一，一生二，二生三，三生万物。数据即万物。");
    }

    // ---------- 打开标签页 ----------

    private void AddWelcomeTab()
    {
        var welcome = new TextBlock
        {
            Margin = new Thickness(24),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = UI.Ant.TextSub,
            Text = "欢迎使用 TaoDBM。\n\n1. 点击右上角“＋ 新建连接”，填好主机、账号后先点“测试连接”。\n2. 连接成功后，在左侧树展开库与表，双击表可浏览数据。\n3. 选中库或表后点“新建查询”，写 SQL 按 F5 执行。\n\n支持：MySQL · PostgreSQL · SQL Server · SQLite · Oracle · MongoDB · Redis",
        };
        var tab = new TabViewItem
        {
            Header = "开始",
            IsClosable = false,
            Content = new ScrollViewer { Content = welcome, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
        };
        WorkTabs.TabItems.Add(tab);
        WorkTabs.SelectedItem = tab;
    }

    private void OpenQueryTab(NodeInfo? info)
    {
        if (info is null) { _ = InfoAsync("请先在左侧树中选中库、表或连接。"); return; }
        var (db, hint) = info.Kind switch
        {
            NodeKind.Database => (info.Name, ""),
            NodeKind.Table => (info.Db, DefaultSnippet(info)),
            _ => (info.Provider?.IsNoSql == true ? "" : (info.Profile.Database.Length > 0 && info.Profile.Engine == DbEngine.SQLite ? "main" : "" ), ""),
        };
        if (info.Provider is MongoProvider m && info.Kind == NodeKind.Table) m.CurrentCollection = info.Name;
        if (info.Provider is RedisProvider r && info.Kind == NodeKind.Table) r.CurrentKey = info.Name;

        var tab = new TabViewItem { Header = $"查询 · {info.Name}" };
        var wt = new UI.WorkTab(this, info.Provider!, db, info.Kind == NodeKind.Table ? info.Name : "", UI.WorkMode.Query, hint.Length > 0 ? hint : null);
        tab.Content = wt;
        WorkTabs.TabItems.Add(tab);
        _workTabs[tab] = wt;
        SaveSession();
        WorkTabs.SelectedItem = tab;
    }

    private static string DefaultSnippet(NodeInfo info) => info.Provider switch
    {
        MongoProvider => "{ }",
        RedisProvider => "GET ",
        _ => $"SELECT * FROM {info.Name} LIMIT 50;",
    };

    public void OpenTableTab(NodeInfo? info)
    {
        if (info is null || info.Kind != NodeKind.Table)
        {
            // Redis 库节点 → 直接打开键列表（键值视图从库级开始）；其余库仍走新建查询
            if (info is not null && info.Kind == NodeKind.Database && info.Provider is RedisProvider)
            {
                var redisTab = new TabViewItem { Header = $"键 · {info.Name}" };
                var redisWt = new UI.WorkTab(this, info.Provider, info.Name, "", UI.WorkMode.TableData, null);
                redisTab.Content = redisWt;
                WorkTabs.TabItems.Add(redisTab);
                _workTabs[redisTab] = redisWt;
                SaveSession();
                WorkTabs.SelectedItem = redisTab;
                return;
            }
            if (info is not null && info.Kind == NodeKind.Database) { OpenQueryTab(info); return; }
            _ = InfoAsync("请选中一个表 / 集合 / 键后查看数据。");
            return;
        }
        var prov = info.Provider!;
        var tab = new TabViewItem
        {
            Header = $"{(prov.IsNoSql ? prov.BrowseLabel : "表")} · {info.Name}",
        };
        if (prov is MongoProvider mp) mp.CurrentCollection = info.Name;
        if (prov is RedisProvider rp) rp.CurrentKey = info.Name;
        var wt = new UI.WorkTab(this, prov, info.Db, info.Name, UI.WorkMode.TableData, null);
        tab.Content = wt;
        WorkTabs.TabItems.Add(tab);
        _workTabs[tab] = wt;
        SaveSession();
        WorkTabs.SelectedItem = tab;
    }

    private void OnTabClose(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        sender.TabItems.Remove(args.Tab);
        if (_workTabs.Remove(args.Tab)) SaveSession();
    }

    // ---------- 标签会话保持 ----------

    /// <summary>把当前打开的工作标签写盘（连接已删的自动剔除）。</summary>
    private void SaveSession()
    {
        try
        {
            var items = _workTabs.Values.Select(t => new TabSessionItem
            {
                ConnectionId = t.SessionConnectionId,
                Mode = t.SessionMode,
                Db = t.SessionDb,
                Child = t.SessionChild,
                Sql = t.SessionSql,
            }).ToList();
            TabSession.Save(TabSession.FilterValid(items, _profiles));
        }
        catch { }
    }

    /// <summary>启动时恢复上次的查询标签（表数据页不自动恢复，避免启动即连库）。</summary>
    private void RestoreSession()
    {
        try
        {
            foreach (var item in TabSession.FilterValid(TabSession.Load(), _profiles))
            {
                if (item.ConnectionId.Length == 0 || item.Mode != "Query") continue;
                var p = _profiles.FirstOrDefault(x => x.Id == item.ConnectionId);
                if (p is null) continue;
                var tab = new TabViewItem { Header = $"查询 · {p.DisplayName}" };
                var wt = new UI.WorkTab(this, ProviderFactory.Create(p), item.Db, "", UI.WorkMode.Query,
                    item.Sql.Length > 0 ? item.Sql : null);
                tab.Content = wt;
                WorkTabs.TabItems.Add(tab);
                _workTabs[tab] = wt;
            }
        }
        catch { }
    }

    // ---------- 对话框辅助 ----------

    private async Task InfoAsync(string msg)
    {
        var dlg = new ContentDialog
        {
            Content = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "好的",
            XamlRoot = Content.XamlRoot,
        };
        await dlg.ShowThemedAsync();
    }

    public async Task<bool> ConfirmAsync(string msg)
    {
        var dlg = new ContentDialog
        {
            Content = new TextBlock { Text = msg, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
        };
        return await dlg.ShowThemedAsync() == ContentDialogResult.Primary;
    }
}
