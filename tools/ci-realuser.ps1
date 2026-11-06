# 真人鼠标路径全链验证：启动→真实鼠标坐标点击→键盘路径→关窗入托盘→日志核验
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Force -Path evidence | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint cButtons, UIntPtr dwExtraInfo);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
  public const uint LEFTDOWN = 0x02, LEFTUP = 0x04, WM_CLOSE = 0x0010;
  public static void Click(int x, int y) {
    SetCursorPos(x, y); System.Threading.Thread.Sleep(150);
    mouse_event(LEFTDOWN, 0, 0, 0, UIntPtr.Zero); System.Threading.Thread.Sleep(80);
    mouse_event(LEFTUP, 0, 0, 0, UIntPtr.Zero);
  }
}
"@
function Snap($path) {
  $b = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Left, $b.Top, 0, 0, $bmp.Size)
  $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
}

# 定位编译产物 TaoDBM.exe
$exe = Get-ChildItem -Recurse -Filter TaoDBM.exe -Path src/TaoDBM/bin |
  Where-Object { $_.FullName -notmatch '\\publish\\' -or $true } | Select-Object -First 1
if (-not $exe) { throw "编译产物里找不到 TaoDBM.exe" }
Write-Host "使用产物: $($exe.FullName)"

Start-Process $exe.FullName
$hwnd = [IntPtr]::Zero
for ($i = 0; $i -lt 40; $i++) {
  Start-Sleep -Milliseconds 500
  $p = Get-Process TaoDBM -ErrorAction SilentlyContinue
  if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "30 秒内主窗口未出现" }
Write-Host "主窗已出现，句柄 $hwnd"
Start-Sleep -Seconds 3
Snap "evidence\realuser-launch.png"

# 真实鼠标路径：按坐标点击窗口内多个位置（相对窗口左上角）
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
[Win32.RECT]$r = New-Object Win32.RECT
if (-not [Win32]::GetWindowRect($hwnd, [ref]$r)) { throw "取窗口矩形失败" }
$cx = [int](($r.Left + $r.Right) / 2); $cy = [int](($r.Top + $r.Bottom) / 2)
$points = @(
  @($cx, $r.Top + 40),        # 顶部工具栏区
  @($r.Left + 80, $r.Top + 120),
  @($cx, $cy),                # 窗口中央
  @($r.Right - 100, $r.Bottom - 80)
)
foreach ($pt in $points) {
  [Mouse]::Click($pt[0], $pt[1]); Start-Sleep -Milliseconds 400
  if (-not (Get-Process TaoDBM -ErrorAction SilentlyContinue)) { throw "真实鼠标点击（$($pt[0]),$($pt[1])）后进程崩溃" }
}
Snap "evidence\realuser-clicked.png"
Write-Host "真实鼠标坐标点击 4 处，进程存活"

# 键盘路径对照：激活窗口后发送真实按键
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Fore {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
}
"@
[Fore]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 500
[System.Windows.Forms.SendKeys]::SendWait("{TAB}{TAB}")
Start-Sleep -Milliseconds 600
[System.Windows.Forms.SendKeys]::SendWait("{F5}")
Start-Sleep -Milliseconds 600
if (-not (Get-Process TaoDBM -ErrorAction SilentlyContinue)) { throw "键盘路径操作后进程崩溃" }
Snap "evidence\realuser-keys.png"
Write-Host "键盘路径（Tab/F5）对照通过，进程存活"

# 关窗分流：关窗后进程应仍在（托盘托管成立，不再退出）
[Mouse]::PostMessage($hwnd, [Mouse]::WM_CLOSE, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
Start-Sleep -Seconds 6
$alive = Get-Process TaoDBM -ErrorAction SilentlyContinue
if (-not $alive) { throw "关窗后进程退出——托盘托管链路在云端真机不成立" }
Write-Host "关窗后进程存活，托盘托管链路成立"

# 日志核验：数据根回落 %LOCALAPPDATA%\TaoDBM\logs（云端无 D:\TaoDBMData）
$logCandidates = @(
  "D:\TaoDBMData\TaoDBM\logs\taodbm.log",
  (Join-Path $env:LOCALAPPDATA "TaoDBM\logs\taodbm.log")
)
$log = $logCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $log) { throw "两处日志路径都没找到日志文件" }
$content = Get-Content $log -Raw
Write-Host "日志位置: $log"
foreach ($mark in @("托管", "注册")) {
  if ($content -notmatch $mark) { throw "日志缺少关键埋点「$mark」——托盘注册链路证据不足" }
}
Write-Host "日志含「托管」「注册」埋点，托盘整链在云端真机过线"

Get-Process TaoDBM -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Write-Host "真人鼠标路径全链验证通过"
