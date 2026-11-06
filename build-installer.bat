@echo off

rem TaoDBM 一键打包：发布程序 -> 生成 MSI（唯一安装交付形态）

rem 产物输出到仓库根目录的 out 文件夹，打包完成时 out 里只有一个 MSI

setlocal

cd /d "%~dp0"



set "VER=1.0.8"

set "PUB=%~dp0src\TaoDBM\bin\x64\Release\net8.0-windows10.0.19041.0\win-x64\publish"

set "MSI=%~dp0out\TaoDBM-%VER%-x64.msi"

set "WIXEXE=%USERPROFILE%\.dotnet\tools\wix.exe"



rem 交付标准：开局整体清空 out 重建，最终自检 out 只有一个 MSI

if exist "%~dp0out" rmdir /s /q "%~dp0out"

if exist "%~dp0out" (

    echo 旧 out 文件夹被占用无法清空（通常是杀毒软件或资源管理器开着安装包），

    echo 请手动删除 out 文件夹后重试，或把项目文件夹加入杀毒软件白名单。

    pause

    exit /b 1

)



rem 关键源码在场检查：解压不完整时明确拦截，不带病编译
if not exist "src\TaoDBM\Core\Providers\RelationalProvider.cs" goto :unpackbad
if not exist "src\TaoDBM\UI\Ant.cs" goto :unpackbad
if not exist "src\TaoDBM\Core\QueryResult.cs" goto :unpackbad

echo [自检] 源码 BOM/命名铁律检查...
if not exist "tools\Check-Source.ps1" (
    echo [提醒] 未找到源码自检脚本，可能被杀毒软件隔离或解压不全。
    echo 构建继续，但建议重新完整解压整包后再试一次。
) else (
    powershell -NoProfile -ExecutionPolicy Bypass -File "tools\Check-Source.ps1"
    if errorlevel 1 goto :checkbad
)
rem 三关全过，放行进入正式构建
goto :gateok

:unpackbad
echo.
echo [拦截] 解压不完整：关键源码文件缺失，无法继续构建。
echo 请删掉整个项目文件夹，把新文件夹加入杀毒软件白名单，
echo 然后右键压缩包，用 Windows 自带的「全部解压缩」重新解压后再跑。
pause
exit /b 1

:checkbad
echo.
echo [拦截] 源码自检未通过：按上面列出的项修复后再跑（多为文件缺失或被改动）。
pause
exit /b 1

:gateok

:err

where wix >nul 2>nul

if errorlevel 1 (

    echo [1/4] 首次运行，安装 WiX 打包工具（5.0.2，约一分钟）...

    dotnet tool install --global wix --version 5.0.2

    if errorlevel 1 goto :err

) else (

    echo [1/4] 检测到 WiX 已安装，直接使用本机版本...

)

set "PATH=%PATH%;%USERPROFILE%\.dotnet\tools"



echo [2/4] 编译主程序（独立模式，全量自包含可直接运行）...

dotnet publish "src\TaoDBM\TaoDBM.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true

if errorlevel 1 goto :err



if not exist "%~dp0out" mkdir "%~dp0out"

pushd src\Installer



echo [3/4] 生成 MSI 安装包（微信式单页界面，内含全部运行库，装完即用）...

set RETRY=0

:msi_retry

set /a RETRY+=1

wix build Package.wxs -arch x64 -d PublishDir="%PUB%" -o "%MSI%" -pdbtype none

if not errorlevel 1 goto :msi_ok

rem 新版 WiX 不认 -pdbtype 时，退回默认参数再试一次

wix build Package.wxs -arch x64 -d PublishDir="%PUB%" -o "%MSI%"

if not errorlevel 1 goto :msi_ok

if %RETRY% GEQ 3 (popd & goto :err)

echo     第 %RETRY% 次写入被占用（通常是杀毒软件正在扫描新文件），2 秒后自动重试...

ping -n 3 127.0.0.1 >nul

del /f /q "%MSI%" >nul 2>nul

goto :msi_retry

:msi_ok

popd



del /f /q "%~dp0out\*.wixpdb" >nul 2>nul

echo [4/4] 自检 out（应只有 1 个 MSI）...

set CNT=0

for /f %%i in ('dir /b "%~dp0out\*.msi" ^| find /c /v ""') do set CNT=%%i

if not "%CNT%"=="1" goto :err

if not exist "%MSI%" goto :err



echo.

echo 完成！产物：out\TaoDBM-%VER%-x64.msi

echo 双击安装：微信式单页界面（协议勾选 + 目录 + 组件勾选），

echo 同版本或更高版本可直接覆盖安装。

pause

exit /b 0



:err

echo.

echo 打包失败或自检未过。常见原因：out 里的旧文件被杀毒软件或资源管理器占用，

echo 可手动删除 out 文件夹后重试，或把项目文件夹加入杀毒软件白名单。

pause

exit /b 1

