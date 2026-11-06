using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TaoDBM.UI;

/// <summary>
/// 系统托盘图标 + 原生右键菜单（Shell_NotifyIcon，无第三方依赖）。
/// 左键单击 = 显示主窗口；右键 = 菜单（打开 / 新建连接 / 主题三项 / 设置 / 退出）。
/// 托盘消息托管在主窗句柄上（子类化转发）。2026-11-03 真机教训：自建消息窗口在真机上
/// 创建失败，导致托盘整条链路失效——现已整体移除该环节。
/// 菜单由系统按系统明暗绘制；回调在主窗 UI 线程派发，动作无需再切线程。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint WM_APP_TRAY = 0x8000;   // WM_APP
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_CONTEXTMENU = 0x007B;

    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NIF_SHOWTIP = 0x80;
    private const uint NIIF_INFO = 1;
    private const uint MF_STRING = 0, MF_CHECKED = 8, MF_SEPARATOR = 0x800, MF_GRAYED = 1;
    private const uint TPM_RETURNCMD = 0x100, TPM_RIGHTBUTTON = 2, TPM_BOTTOMALIGN = 0x40;
    private const int GWLP_WNDPROC = -4;

    private const uint ID_SHOW = 1, ID_NEWCONN = 2, ID_INK = 3, ID_GRAY = 4, ID_NIGHT = 5, ID_SETTINGS = 6, ID_EXIT = 7;
    private const uint ID_RECENT0 = 100;   // 「我的连接」动态区起点
    public const int RecentMax = 8;

    private static TrayIcon? Instance;   // 单实例：托盘消息经主窗子类化转发
    private static readonly WndProc WndProcStub = WndProcInner;   // 常驻引用：防回调委托被回收
    private static readonly uint WM_TASKBARCREATED = RegisterWindowMessageW("TaskbarCreated");   // explorer 重启后补挂

    private readonly IntPtr _ownerHwnd;   // 主窗句柄：托盘回调唯一标识 + 消息入口（不再自建窗口）
    private readonly IntPtr _prevProc;    // 子类化前主窗原窗口过程
    private readonly IntPtr _icon;
    private readonly bool _iconFromFile;  // 从文件加载才拥有图标，可销毁；系统共享图标不可销毁
    private readonly Func<string> _themeProvider;
    private bool _added;
    private bool _disposed;

    /// <summary>左键单击 / 菜单「打开」。</summary>
    public event Action? OpenRequested;
    /// <summary>菜单「新建连接」。</summary>
    public event Action? NewConnectionRequested;
    /// <summary>菜单主题切换（ink / gray / night）。</summary>
    public event Action<string>? ThemeRequested;
    /// <summary>菜单「设置」。</summary>
    public event Action? SettingsRequested;
    /// <summary>菜单「退出」。</summary>
    public event Action? ExitRequested;
    /// <summary>菜单「我的连接」点击（参数为连接 Id）。</summary>
    public event Action<string>? RecentRequested;
    /// <summary>右键菜单「我的连接」清单来源（连接 Id + 显示名），由主窗在菜单弹出前提供。</summary>
    public Func<List<(string Id, string Name)>>? RecentProvider { get; set; }

    public TrayIcon(IntPtr ownerHwnd, string iconPath, Func<string> themeProvider)
    {
        _ownerHwnd = ownerHwnd;
        _themeProvider = themeProvider;
        Instance = this;

        bool iconFromFile = true;
        _icon = LoadImageW(IntPtr.Zero, iconPath, 1 /*IMAGE_ICON*/,
            GetSystemMetrics(11) /*SM_CXICON*/, GetSystemMetrics(12) /*SM_CYICON*/, 0x10 /*LR_LOADFROMFILE*/);
        if (_icon == IntPtr.Zero)
        {
            iconFromFile = false;
            _icon = LoadIconW(IntPtr.Zero, (IntPtr)32512 /*IDI_APPLICATION*/);   // 图标文件缺失回落系统应用图标，托盘照常注册
        }
        _iconFromFile = iconFromFile;   // 文件图标须销毁；系统共享图标绝不可销毁
        Core.AppLog.Write(iconFromFile
            ? $"托盘: 图标加载成功 {iconPath}"
            : $"托盘: 图标文件加载失败，回落系统图标 {iconPath}");

        // 子类化主窗接管消息：不再自建消息窗口（真机创建失败根因），托盘回调经主窗过程转发
        _prevProc = SetWindowLongPtrW(_ownerHwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(WndProcStub));
        Core.AppLog.Write(_prevProc == IntPtr.Zero
            ? "托盘: 主窗消息接管失败（取原过程返回空）"
            : $"托盘: 消息托管到主窗 hwnd=0x{_ownerHwnd:X}");
        AddToShell();
    }

    /// <summary>向 Shell 注册托盘图标；hWnd 必须随消息一起上送，否则 NIM_ADD 静默失败（2026-11-03 真机教训）。</summary>
    private void AddToShell()
    {
        if (_disposed || _ownerHwnd == IntPtr.Zero) return;
        var nid = NewData();
        nid.uCallbackMessage = WM_APP_TRAY;
        if (_icon != IntPtr.Zero) nid.hIcon = _icon;
        nid.szTip = "TaoDBM";
        nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | NIF_SHOWTIP;
        _added = Shell_NotifyIconW(NIM_ADD, ref nid);
        var addErr = Marshal.GetLastWin32Error();
        Core.AppLog.Write(_added
            ? "托盘: 向系统注册成功"
            : $"托盘: 向系统注册失败（错误码 {addErr}）");
        if (_added)
        {
            nid.uVersion = 4;   // 通知图标新版回调语义
            Shell_NotifyIconW(NIM_SETVERSION, ref nid);
        }
    }

    /// <summary>托盘图标当前是否真的挂上了（2026-11-03 真机教训：挂失败必须如实可查，不能玩消失）。</summary>
    public bool IsAdded => _added;

    /// <summary>未挂上时再次尝试注册（启动早期桌面未就绪的兜底）；结果如实反映到 IsAdded。</summary>
    public void RetryAdd()
    {
        if (!_disposed && !_added)
        {
            Core.AppLog.Write("托盘: 未挂上，自动重试注册");
            AddToShell();
        }
    }

    /// <summary>托盘气泡提示（首次最小化时告知位置，不弹窗打扰）。</summary>
    public void ShowInfo(string title, string text)
    {
        if (!_added || _disposed) return;
        var nid = NewData();
        nid.uFlags = NIF_INFO;
        nid.szInfoTitle = title;
        nid.szInfo = text;
        nid.dwInfoFlags = NIIF_INFO;
        Shell_NotifyIconW(NIM_MODIFY, ref nid);
    }

    private NOTIFYICONDATAW NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _ownerHwnd,   // 标识托盘项的唯一键：增/改/删/气泡都必须带上，缺了 NIM_ADD 直接静默失败
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private static IntPtr WndProcInner(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        var tray = Instance;
        if (tray is null || tray._disposed)
            return CallWindowProcW(tray?._prevProc ?? IntPtr.Zero, hWnd, msg, wParam, lParam);
        if (msg == WM_APP_TRAY) tray.OnTrayMessage(lParam);
        else if (msg == WM_TASKBARCREATED)
        {
            Core.AppLog.Write("托盘: explorer 重启，自动补挂");
            tray.AddToShell();   // explorer 重启后补挂
        }
        return CallWindowProcW(tray._prevProc, hWnd, msg, wParam, lParam);   // 其余消息链回原过程
    }

    private void OnTrayMessage(IntPtr lParam)
    {
        uint m = (uint)lParam.ToInt64() & 0xFFFF;
        if (m == WM_LBUTTONUP) { OpenRequested?.Invoke(); }
        else if (m == WM_RBUTTONUP || m == WM_CONTEXTMENU) { ShowMenu(); }
    }

    private void ShowMenu()
    {
        var recents = RecentProvider?.Invoke() ?? new List<(string Id, string Name)>();
        var m = CreatePopupMenu();
        AppendMenuW(m, MF_STRING, ID_SHOW, "打开 TaoDBM");
        AppendMenuW(m, MF_STRING, ID_NEWCONN, "新建连接");
        if (recents.Count > 0)
        {
            AppendMenuW(m, MF_SEPARATOR, 0, null);
            AppendMenuW(m, MF_GRAYED, 0, "我的连接");
            for (int i = 0; i < recents.Count && i < RecentMax; i++)
                AppendMenuW(m, MF_STRING, ID_RECENT0 + (uint)i, $"  {recents[i].Name}");
        }
        AppendMenuW(m, MF_SEPARATOR, 0, null);
        string theme = _themeProvider();
        AppendMenuW(m, MF_STRING | (theme == "ink" ? MF_CHECKED : 0), ID_INK, "主题：水墨");
        AppendMenuW(m, MF_STRING | (theme == "gray" ? MF_CHECKED : 0), ID_GRAY, "主题：天青");
        AppendMenuW(m, MF_STRING | (theme == "night" ? MF_CHECKED : 0), ID_NIGHT, "主题：墨夜");
        AppendMenuW(m, MF_SEPARATOR, 0, null);
        AppendMenuW(m, MF_STRING, ID_SETTINGS, "设置");
        AppendMenuW(m, MF_STRING, ID_EXIT, "退出");

        GetCursorPos(out var p);
        SetForegroundWindow(_ownerHwnd);   // 菜单外点击自动收起的惯例前置
        uint cmd = TrackPopupMenuEx(m, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.X, p.Y, _ownerHwnd, IntPtr.Zero);
        PostMessageW(_ownerHwnd, 0, IntPtr.Zero, IntPtr.Zero);   // WM_NULL 收尾
        DestroyMenu(m);

        if (cmd >= ID_RECENT0 && cmd < ID_RECENT0 + (uint)Math.Min(recents.Count, RecentMax))
        {
            RecentRequested?.Invoke(recents[(int)(cmd - ID_RECENT0)].Id);
            return;
        }
        switch (cmd)
        {
            case ID_SHOW: OpenRequested?.Invoke(); break;
            case ID_NEWCONN: NewConnectionRequested?.Invoke(); break;
            case ID_INK: ThemeRequested?.Invoke("ink"); break;
            case ID_GRAY: ThemeRequested?.Invoke("gray"); break;
            case ID_NIGHT: ThemeRequested?.Invoke("night"); break;
            case ID_SETTINGS: SettingsRequested?.Invoke(); break;
            case ID_EXIT: ExitRequested?.Invoke(); break;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added) { var nid = NewData(); Shell_NotifyIconW(NIM_DELETE, ref nid); }
        if (_prevProc != IntPtr.Zero) SetWindowLongPtrW(_ownerHwnd, GWLP_WNDPROC, _prevProc);   // 还原主窗过程
        Instance = null;
        if (_icon != IntPtr.Zero && _iconFromFile) DestroyIcon(_icon);
    }

    // ---- Win32 ----
    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public ulong guidItem1;   // GUID 占位 16 字节（V3 尺寸，Win10/11 直接接受）
        public ulong guidItem2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIconW(uint msg, ref NOTIFYICONDATAW data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint load);
    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInst, IntPtr lpIconName);
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);
    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenuW(IntPtr menu, uint flags, uint id, string? text);
    [DllImport("user32.dll")]
    private static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr ctx);
    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr h);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")]
    private static extern bool PostMessageW(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProcW(IntPtr prevProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
