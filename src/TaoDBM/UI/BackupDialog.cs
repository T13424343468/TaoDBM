using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Diagnostics;
using TaoDBM.Core;
using Windows.Storage.Pickers;

namespace TaoDBM.UI;

/// <summary>备份 / 还原对话框：按引擎分流（官方 CLI、T-SQL、文件复制）。</summary>
public static class BackupDialog
{
    /// <summary>info 须为连接节点（携带 Profile 与 Provider）。</summary>
    public static async Task Show(MainWindow owner, NodeInfo info)
    {
        var profile = info.Profile;
        var prov = info.Provider ?? ProviderFactory.Create(profile);
        if (!BackupTools.Supported(profile.Engine))
        {
            await new MessageDialogHelper(owner).Show("备份 / 还原", BackupTools.UnsupportedReason(profile.Engine));
            return;
        }

        List<string> dbs = new();
        try { dbs = await prov.GetDatabases(); }
        catch { dbs = new List<string> { profile.Database }; }

        var dbBox = new ComboBox { MinWidth = 240, PlaceholderText = "选择数据库" };
        foreach (var d in dbs) dbBox.Items.Add(d);
        if (profile.Database.Length > 0 && dbs.Contains(profile.Database)) dbBox.SelectedItem = profile.Database;
        else if (dbs.Count > 0) dbBox.SelectedIndex = 0;

        var isBackup = new RadioButton { Content = "备份到文件", IsChecked = true, GroupName = "brMode" };
        var isRestore = new RadioButton { Content = "从文件还原", GroupName = "brMode" };
        var modePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        modePanel.Children.Add(isBackup);
        modePanel.Children.Add(isRestore);

        var fileBox = new TextBox { MinWidth = 360, PlaceholderText = "选择或输入文件路径" };
        var pickBtn = new Button { Content = "选择文件…", CornerRadius = new CornerRadius(4) };
        pickBtn.Click += async (_, _) =>
        {
            try
            {
                if (isBackup.IsChecked == true)
                {
                    var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                    var ext = profile.Engine == DbEngine.SqlServer ? ".bak" : profile.Engine == DbEngine.SQLite ? ".db" : ".sql";
                    picker.FileTypeChoices.Add(ext == ".db" ? "SQLite 库文件" : ext == ".bak" ? "SQL Server 备份" : "SQL 备份", new List<string> { ext });
                    picker.SuggestedFileName = $"{dbBox.SelectedItem ?? "backup"}-{DateTime.Now:yyyyMMdd-HHmmss}";
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                    var f = await picker.PickSaveFileAsync();
                    if (f != null) fileBox.Text = f.Path;
                }
                else
                {
                    var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                    picker.FileTypeFilter.Add(profile.Engine == DbEngine.SqlServer ? ".bak" : profile.Engine == DbEngine.SQLite ? ".db" : ".sql");
                    picker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
                    var f = await picker.PickSingleFileAsync();
                    if (f != null) fileBox.Text = f.Path;
                }
            }
            catch { /* 用户取消 */ }
        };

        var runBtn = new Button
        {
            Content = "执行",
            Background = Ant.Primary,
            Foreground = Ant.OnPrimary,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
        };
        var output = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 150,
            FontFamily = (FontFamily)Application.Current.Resources["AntMono"],
            FontSize = 12,
            Text = "选好数据库和文件后点「执行」。",
        };

        string note = profile.Engine switch
        {
            DbEngine.MySQL or DbEngine.PostgreSQL => "使用官方命令行工具（mysqldump / pg_dump + mysql / psql），需已安装并加入 PATH；密码经环境变量传递，不会出现在命令行。",
            DbEngine.SqlServer => "用 T-SQL BACKUP / RESTORE 执行；磁盘路径是数据库服务器上的路径（本机连接时即本机路径）。",
            _ => "直接复制 / 覆盖 .db 文件；还原会覆盖当前库文件，请确认没有其他程序占用。",
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 560 };
        content.Children.Add(new TextBlock { Text = $"引擎：{EngineInfo.Name(profile.Engine)} · {profile.DisplayName}", FontSize = 12, Foreground = Ant.TextSub });
        var row1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row1.Children.Add(new TextBlock { Text = "数据库", VerticalAlignment = VerticalAlignment.Center });
        row1.Children.Add(dbBox);
        content.Children.Add(row1);
        content.Children.Add(modePanel);
        var row2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row2.Children.Add(fileBox);
        row2.Children.Add(pickBtn);
        content.Children.Add(row2);
        content.Children.Add(new TextBlock { Text = note, FontSize = 12, Foreground = Ant.TextSub, TextWrapping = TextWrapping.Wrap });
        content.Children.Add(runBtn);
        content.Children.Add(output);

        var dlg = new ContentDialog
        {
            Title = "备份 / 还原",
            Content = content,
            CloseButtonText = "关闭",
            XamlRoot = owner.Content.XamlRoot,
        };

        async void OnRun()
        {
            string db = dbBox.SelectedItem as string ?? "";
            string file = fileBox.Text.Trim();
            if (db.Length == 0) { output.Text = "请先选择数据库。"; return; }
            if (file.Length == 0) { output.Text = "请先选择文件。"; return; }
            bool backup = isBackup.IsChecked == true;
            runBtn.IsEnabled = false;
            output.Text = "执行中…";
            try
            {
                switch (profile.Engine)
                {
                    case DbEngine.MySQL:
                    {
                        var (h, p) = SshTunnel.Resolve(profile, profile.Host, profile.Port > 0 ? profile.Port : 3306);
                        var spec = backup ? BackupTools.MysqlDump(h, p, profile.User, profile.Password, db, file)
                                          : BackupTools.MysqlRestore(h, p, profile.User, profile.Password, db, file);
                        output.Text = "> " + spec.Describe + "\n执行中…";
                        var (code, text) = await RunAsync(spec);
                        output.Text = backup && code == 0 && text.Trim().Length == 0
                            ? $"备份完成：{file}（{new FileInfo(file).Length / 1024.0:N1} KB）"
                            : $"退出码 {code}\n{text}";
                        break;
                    }
                    case DbEngine.PostgreSQL:
                    {
                        var (h, p) = SshTunnel.Resolve(profile, profile.Host, profile.Port > 0 ? profile.Port : 5432);
                        var spec = backup ? BackupTools.PgDump(h, p, profile.User, profile.Password, db, file)
                                          : BackupTools.PgRestore(h, p, profile.User, profile.Password, db, file);
                        output.Text = "> " + spec.Describe + "\n执行中…";
                        var (code, text) = await RunAsync(spec);
                        output.Text = backup && code == 0 && text.Trim().Length == 0
                            ? $"备份完成：{file}（{new FileInfo(file).Length / 1024.0:N1} KB）"
                            : $"退出码 {code}\n{text}";
                        break;
                    }
                    case DbEngine.SqlServer:
                    {
                        var sql = backup ? BackupTools.SqlServerBackupSql(db, file) : BackupTools.SqlServerRestoreSql(db, file);
                        output.Text = "-- " + sql;
                        var list = await prov.Execute("", sql);
                        var err = list.FirstOrDefault(r => r.Message.StartsWith("错误", StringComparison.Ordinal));
                        output.Text = err is null
                            ? (backup ? $"备份完成：服务器已写入 {file}" : $"还原完成：{db} 已从 {file} 恢复")
                            : err.Message;
                        break;
                    }
                    case DbEngine.SQLite:
                    {
                        if (backup)
                        {
                            BackupTools.SqliteBackup(profile.Database, file);
                            output.Text = $"备份完成：{profile.Database} → {file}";
                        }
                        else
                        {
                            var confirm = new ContentDialog
                            {
                                Title = "确认还原",
                                Content = new TextBlock { Text = $"将用 {file} 覆盖当前库文件：\n{profile.Database}\n\n请确认已关闭其他占用该文件的程序。", TextWrapping = TextWrapping.Wrap },
                                PrimaryButtonText = "还原",
                                CloseButtonText = "取消",
                                XamlRoot = owner.Content.XamlRoot,
                            };
                            if (await confirm.ShowThemedAsync() != ContentDialogResult.Primary) { output.Text = "已取消。"; runBtn.IsEnabled = true; return; }
                            BackupTools.SqliteRestore(profile.Database, file);
                            output.Text = $"还原完成：{file} → {profile.Database}";
                        }
                        break;
                    }
                }
                QueryHistory.Add(prov.EngineName, db, $"-- {(backup ? "备份" : "还原")}：{file}");
            }
            catch (Exception ex)
            {
                output.Text = (ex is System.ComponentModel.Win32Exception
                    ? "未找到命令行工具。请安装 MySQL / PostgreSQL 客户端工具并加入 PATH 后重试。\n"
                    : "") + "失败：" + ex.Message;
            }
            finally { runBtn.IsEnabled = true; }
        }
        runBtn.Click += (_, _) => OnRun();

        _ = dlg.ShowThemedAsync();
    }

    private static async Task<(int Code, string Output)> RunAsync(ToolSpec spec)
    {
        var psi = new ProcessStartInfo(spec.FileName, spec.Arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var kv in spec.Env) psi.Environment[kv.Key] = kv.Value;
        if (spec.InputFile != null) psi.RedirectStandardInput = true;

        using var p = new Process { StartInfo = psi };
        p.Start();
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (spec.InputFile != null)
        {
            var content = await File.ReadAllTextAsync(spec.InputFile);
            await p.StandardInput.WriteAsync(content);
            p.StandardInput.Close();
        }
        await p.WaitForExitAsync();
        var text = await outTask;
        var err = await errTask;
        return (p.ExitCode, (text + "\n" + err).Trim());
    }
}
