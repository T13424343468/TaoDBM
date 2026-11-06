// 类型检查桩：只补 XAML 代码生成的字段与方法体，真实 WinUI API 来自引用的投影程序集。
namespace TaoDBM
{
    public sealed partial class MainWindow
    {
        public void InitializeComponent() { }

        // MainWindow.xaml 中 x:Name 的字段（真实工程由 XAML 编译器生成）
        internal Microsoft.UI.Xaml.Controls.Image LogoImg = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnNewConn = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnCenter = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnAbout = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnLogo = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnTheme = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnRefresh = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnQuery = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnData = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnDesign = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnBackup = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnSync = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnErd = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnEdit = null!;
        internal Microsoft.UI.Xaml.Controls.Button BtnDel = null!;
        internal Microsoft.UI.Xaml.Controls.TreeView ConnTree = null!;
        internal Microsoft.UI.Xaml.Controls.TabView WorkTabs = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock AppTitleText = null!;
        internal Microsoft.UI.Xaml.Controls.TextBlock StatusText = null!;
        internal Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem ThemeInk = null!;
        internal Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem ThemeSky = null!;
        internal Microsoft.UI.Xaml.Controls.ToggleMenuFlyoutItem ThemeNight = null!;
    }

    public partial class App
    {
        public void InitializeComponent() { }
    }
}
