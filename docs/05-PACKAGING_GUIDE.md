# 05 · 打包与发布指南

## 1. 一键打包（Windows）

双击仓库根目录 `build-installer.bat`，或命令行执行。脚本流程：

1. 整体清空 `out` 重建（被占用则提示手动删除后重试）
2. 安装 / 固定 WiX 5.0.2（dotnet tool，首次约 1 分钟）
3. `dotnet publish` 主程序（self-contained + WindowsAppSDK 自包含，x64）
4. WiX 编译 `src/Installer/Package.wxs` → `out/TaoDBM-1.0.0-x64.msi`（含杀软占用自动重试 ×3）
5. 自检 `out`：最终只允许有 1 个产物（MSI），多于或缺失即报错

产物唯一交付形态：

| 产物 | 用途 |
| --- | --- |
| MSI | 双击出微信式单页安装界面；也支持静默部署（`msiexec /i ... /qn`） |

## 2. 安装器组成

- `Package.wxs`：MSI。微信式单页 UI（许可勾选 + 目录 + 三个组件勾选），
  功能树：主程序（必装）/ 桌面快捷方式 / 开始菜单 / 加入系统 PATH。
  不再提供 EXE Bundle 与绿色版文件夹，安装体验全部由 MSI 自带 UI 完成

## 3. 修改版本号

改两处保持一致：

1. `build-installer.bat` 里的 `set "VER=1.0.0"`
2. `Package.wxs` 的 `Version="1.0.0"` 与许可文案中的版本号
3. 默认安装路径（D 盘长期标准）：`Package.wxs` 中 `SetInstallFolderUI` / `SetInstallFolderExec` 两个 CustomAction 的 `Value="D:\Program Files\TaoDBM\"`（标准目录树 + Type-51 在 CostFinalize 前赋绝对值；目录 Name 不允许直接写绝对路径；**值必须以目录分隔符结尾**——MSI 官方规则：自定义动作设置的目录属性可能不带尾分隔符，`[INSTALLFOLDER]TaoDBM.exe` 拼接会黏连成坏路径，快捷方式「已更改或移动」即此因）。两条 Shortcut 必须显式带 `Icon="TaoDBMIco"`；开始菜单目录组件须带 RemoveFolder 卸载清理

升级安装：MSI 的 UpgradeCode 不变即可覆盖升级；降级会提示先卸载。

## 4. 非 Windows 环境的验证边界（如实说明）

本仓库在 Linux 沙箱开发，WiX 在 Linux 上的已知限制：

- `Directory/@Name` 与 `Payload/@Name` 一律报 `WIX0389`（路径判定平台差异）
- MSI 最终封装需 Windows 的 `msi.dll`（`WIX0001`）

因此沙箱验证的基线是：**两份 .wxs 能被真实 WiX 解析到打包前的最后一步，
除上述平台性报错外无任何其他错误**。完整的安装包产物在 Windows 上执行
`build-installer.bat` 即可产出，脚本已处理扩展安装与文件占用重试。

## 5. C# 编译验证（非 Windows 环境可重复）

- `typecheck/`：引用真实 `Microsoft.WinUI.dll` 投影 + 桩（XAML 生成成员），
  `dotnet build` 0 错 0 警告
- `logictest/`：`dotnet run --project logictest` 110/110

## 6. 图标再生成

```bash
cd assets && python3 make_icons.py
```

输出 `src/TaoDBM/Assets/TaoDBM.png` 与 `TaoDBM.ico`（16-256 全尺寸）。
设计稿参数：256 画布、4x 超采样、玄蓝对角渐变（#0D52BF→#4096FF）、
太极居中（半径 30%）、金色 DB 圆柱右下压角。
