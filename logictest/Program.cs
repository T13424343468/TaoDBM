using System;
using System.Text;
using TaoDBM.Core;
using TaoDBM.UI;

int pass = 0, fail = 0;
var layerPass = new Dictionary<string, int>();
var layerFail = new Dictionary<string, int>();
string layer = "单元";
void Layer(string name)
{
    layer = name;
    layerPass.TryAdd(name, 0);
    layerFail.TryAdd(name, 0);
    Console.WriteLine($"\n========== {name}层 ==========");
}
void Check(string name, bool ok, string detail = "")
{
    layerPass.TryAdd(layer, 0);
    layerFail.TryAdd(layer, 0);
    if (ok) { pass++; layerPass[layer]++; Console.WriteLine($"  ✓ {name}"); }
    else { fail++; layerFail[layer]++; Console.WriteLine($"  ✗ {name}  {detail}"); }
}
Layer("单元");

Console.WriteLine("== SqlSplitter ==");
{
    var parts = SqlSplitter.Split("SELECT 1; SELECT 2;");
    Check("普通拆分", parts.Count == 2 && parts[0] == "SELECT 1" && parts[1] == "SELECT 2", string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("SELECT 'a;b' AS v;\nSELECT 2");
    Check("字符串内分号不拆", parts.Count == 2 && parts[0] == "SELECT 'a;b' AS v", string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("SELECT 1 -- comment; select\n; SELECT 2");
    Check("行注释内分号不拆", parts.Count == 2, string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("/* ; ; */ SELECT 1");
    Check("块注释内分号不拆", parts.Count == 1 && parts[0] == "/* ; ; */ SELECT 1", string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("SELECT `we;ird` FROM t");
    Check("反引号内分号不拆", parts.Count == 1, string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("SELECT '''; SELECT 2");
    Check("转义引号", parts.Count == 1 && parts[0].StartsWith("SELECT '''"), string.Join("|", parts));
}
{
    var parts = SqlSplitter.Split("  ;  ;  ");
    Check("空脚本返回空", parts.Count == 0, string.Join("|", parts));
}

Console.WriteLine("== Exporter ==");
{
    var r = new QueryResult { Columns = { "id", "name", "note" } };
    r.Rows.Add(new object?[] { 1, "张三", "有,逗号" });
    r.Rows.Add(new object?[] { 2, "李\"四", "换\n行" });
    var csv = Exporter.Csv(r);
    Check("CSV 表头", csv.Contains("id,name,note"));
    Check("CSV 逗号转义", csv.Contains("\"有,逗号\""), csv);
    Check("CSV 引号转义", csv.Contains("\"李\"\"四\""), csv);
    Check("CSV BOM", csv.StartsWith('\uFEFF'));
}
{
    var r = new QueryResult { Columns = { "id", "name" } };
    r.Rows.Add(new object?[] { 1, "O'Brien" });
    r.Rows.Add(new object?[] { 2, DBNull.Value });
    r.Rows.Add(new object?[] { 3, new byte[] { 0xDE, 0xAD } });
    var sql = Exporter.InsertSql(r, "users");
    Check("INSERT 引号转义", sql.Contains("'O''Brien'"), sql);
    Check("INSERT NULL", sql.Contains("NULL"), sql);
    Check("INSERT 十六进制", sql.Contains("X'DEAD'"), sql);
}

Console.WriteLine("== EngineInfo ==");
{
    Check("默认端口", EngineInfo.DefaultPort(DbEngine.MySQL) == 3306 && EngineInfo.DefaultPort(DbEngine.Redis) == 6379);
    Check("文件库判断", EngineInfo.IsFileBased(DbEngine.SQLite) && !EngineInfo.IsFileBased(DbEngine.MySQL));
    Check("NoSql 判断", EngineInfo.IsRelational(DbEngine.MongoDB) == false && EngineInfo.IsRelational(DbEngine.Oracle));
    Check("引擎数量", EngineInfo.All.Count() == 7);
}

Console.WriteLine("== CsvImporter ==");
{
    var (headers, rows) = CsvImporter.Parse("id,name,note\n1,\"有,逗号\",\"带\"\"引号\"\"的\"\n2,李四,\"跨\n行值\"");
    Check("CSV 表头", headers.SequenceEqual(new[] { "id", "name", "note" }), string.Join("|", headers));
    Check("CSV 逗号转义还原", rows[0][1] == "有,逗号");
    Check("CSV 双引号转义还原", rows[0][2] == "带\"引号\"的");
    Check("CSV 跨行值", rows[1][2] == "跨\n行值");
}
{
    var (headers, rows) = CsvImporter.Parse("\uFEFFa,b\n,x\n");
    Check("CSV BOM 剥离", headers.SequenceEqual(new[] { "a", "b" }));
    Check("CSV 空单元格为 null", rows[0][0] == null);
}
{
    var prov = new TestProv();
    var cols = new List<ColumnSpec>
    {
        new() { Name = "id", DataType = "int", IsKey = true },
        new() { Name = "name", DataType = "varchar(50)" },
    };
    var stmts = CsvImporter.BuildInserts(prov, "db", "t", cols, new List<string> { "id", "name" },
        new List<string?[]> { new string?[] { "1", "O'Brien" }, new string?[] { "", "李四" } }, batch: 10);
    Check("导入数字不加引号", stmts[0].Contains("VALUES (1, 'O''Brien')"), stmts[0]);
    Check("导入空值→NULL", stmts[0].Contains("VALUES (NULL, '李四')"), stmts[0]);
    Check("导入标识符引用", stmts[0].Contains("INSERT INTO `t` (`id`, `name`)"), stmts[0]);
}

Console.WriteLine("== QueryHistory ==");
{
    QueryHistory.Add("MySQL", "db1", "SELECT 1");
    QueryHistory.Clear();
    QueryHistory.Add("MySQL", "db1", "SELECT 'x'");
    Check("历史记录与清空", QueryHistory.Items.Count == 1 && QueryHistory.Items[0].Sql == "SELECT 'x'");
}

Console.WriteLine("== Quote（schema 支持）==");
{
    var prov = new TestProv('"');
    Check("PG 逐段引用", prov.Quote("public.users") == "\"public\".\"users\"", prov.Quote("public.users"));
    var my = new TestProv('`');
    Check("MySQL 反引号", my.Quote("mydb.t") == "`mydb`.`t`");
}

Console.WriteLine("== SqlHighlighter ==");
{
    var sql = "SELECT id, 'a;b' FROM `t1` -- 注释\nWHERE x = 1.5e-3 AND n = 0x1F";
    var toks = SqlHighlighter.Tokenize(sql);
    string[] Of(TokenKind k) => toks.Where(t => t.Kind == k).Select(t => SqlHighlighter.Text(sql, t)).ToArray();
    Check("关键字识别", Of(TokenKind.Keyword).Contains("SELECT") && Of(TokenKind.Keyword).Contains("FROM")
        && Of(TokenKind.Keyword).Contains("WHERE") && Of(TokenKind.Keyword).Contains("AND"), string.Join(",", Of(TokenKind.Keyword)));
    Check("字符串含分号整体", Of(TokenKind.String).SequenceEqual(new[] { "'a;b'" }), string.Join(",", Of(TokenKind.String)));
    Check("行注释整体", Of(TokenKind.Comment).SequenceEqual(new[] { "-- 注释" }), string.Join(",", Of(TokenKind.Comment)));
    Check("反引号为引用标识符", Of(TokenKind.QuotedIdent).SequenceEqual(new[] { "`t1`" }));
    Check("科学计数与十六进制数字", Of(TokenKind.Number).SequenceEqual(new[] { "1.5e-3", "0x1F" }), string.Join(",", Of(TokenKind.Number)));
    Check("VARCHAR 识别为类型", SqlHighlighter.Tokenize("VARCHAR(50)").Any(t => t.Kind == TokenKind.TypeName));
    Check("块注释未闭合到结尾", SqlHighlighter.Text("/* abc", SqlHighlighter.Tokenize("/* abc").Single(t => t.Kind == TokenKind.Comment)) == "/* abc");
    Check("空串安全", SqlHighlighter.Tokenize("").Count == 0);
    Check("偏移可还原原文", toks.All(t => t.Start + t.Length <= sql.Length));
}
{
    var sql = "SELECT 'it''s', \"quoted ident\";";
    var toks = SqlHighlighter.Tokenize(sql);
    Check("双引号转义字符串完整", SqlHighlighter.Text(sql, toks.First(t => t.Kind == TokenKind.String)) == "'it''s'");
    Check("PG 引号标识符", SqlHighlighter.Text(sql, toks.First(t => t.Kind == TokenKind.QuotedIdent)) == "\"quoted ident\"");
}

Console.WriteLine("== TableDdl ==");
{
    var my = (Func<string, string>)(s => "`" + s + "`");
    var t = new TableDesign
    {
        TableName = "users",
        Columns =
        {
            new TableColumnDesign { Name = "id", SqlType = "BIGINT", IsPrimaryKey = true, AutoIncrement = true, Nullable = false },
            new TableColumnDesign { Name = "name", SqlType = "VARCHAR(50)", DefaultValue = "无名" },
            new TableColumnDesign { Name = "created", SqlType = "DATETIME", DefaultValue = "CURRENT_TIMESTAMP" },
        },
    };
    var sql = TableDdl.BuildCreate(t, DbEngine.MySQL, my);
    Check("MySQL 建表反引号", sql.Contains("CREATE TABLE `users`"), sql);
    Check("MySQL 自增", sql.Contains("`id` BIGINT AUTO_INCREMENT PRIMARY KEY"), sql);
    Check("默认值字符串加引号", sql.Contains("DEFAULT '无名'"), sql);
    Check("默认值表达式原样", sql.Contains("DEFAULT CURRENT_TIMESTAMP"), sql);
}
{
    var pg = (Func<string, string>)(s => "\"" + s + "\"");
    var t = new TableDesign
    {
        TableName = "orders",
        Columns =
        {
            new TableColumnDesign { Name = "id", SqlType = "int", IsPrimaryKey = true, AutoIncrement = true, Nullable = false },
            new TableColumnDesign { Name = "memo", SqlType = "text" },
        },
    };
    var sql = TableDdl.BuildCreate(t, DbEngine.PostgreSQL, pg);
    Check("PG 身份列", sql.Contains("\"id\" int GENERATED BY DEFAULT AS IDENTITY"), sql);
    var ss = TableDdl.BuildCreate(t, DbEngine.SqlServer, pg);
    Check("SqlServer IDENTITY", ss.Contains("\"id\" int IDENTITY(1,1) PRIMARY KEY"), ss);
    var lite = TableDdl.BuildCreate(t, DbEngine.SQLite, pg);
    Check("SQLite 自增主键", lite.Contains("\"id\" INTEGER PRIMARY KEY AUTOINCREMENT"), lite);
    var ora = TableDdl.BuildCreate(t, DbEngine.Oracle, pg);
    Check("Oracle 身份列", ora.Contains("\"id\" int GENERATED ALWAYS AS IDENTITY PRIMARY KEY"), ora);
    var ora2 = TableDdl.BuildCreate(t, DbEngine.Oracle, s => s);
    Check("Oracle 无引号", ora2.Contains("CREATE TABLE orders ("), ora2);
}
{
    // 复合主键走表级约束
    var t = new TableDesign
    {
        TableName = "rel",
        Columns =
        {
            new TableColumnDesign { Name = "a", SqlType = "INT", IsPrimaryKey = true },
            new TableColumnDesign { Name = "b", SqlType = "INT", IsPrimaryKey = true },
        },
    };
    var sql = TableDdl.BuildCreate(t, DbEngine.MySQL, s => s);
    Check("复合主键表级约束", sql.Contains("PRIMARY KEY (a, b)") && !sql.Contains("a INT PRIMARY KEY"), sql);
}
{
    var my = (Func<string, string>)(s => "`" + s + "`");
    var t = new TableDesign
    {
        TableName = "t",
        Columns =
        {
            new TableColumnDesign { Name = "c1", SqlType = "INT", Nullable = false },
            new TableColumnDesign { Name = "old1", SqlType = "INT" },
        },
    };
    var (stmts, skipped) = TableDdl.BuildAlter(t, new[] { "old1", "dead" }, DbEngine.MySQL, my);
    Check("ALTER 新增列", stmts.Any(s => s == "ALTER TABLE `t` ADD `c1` INT NOT NULL"), string.Join(" | ", stmts));
    Check("ALTER 修改列", stmts.Any(s => s == "ALTER TABLE `t` MODIFY COLUMN `old1` INT"), string.Join(" | ", stmts));
    Check("ALTER 删除列", stmts.Contains("ALTER TABLE `t` DROP COLUMN `dead`"));
    Check("MySQL 改表无跳过", skipped.Count == 0, string.Join("|", skipped));

    var (liteStmts, liteSkip) = TableDdl.BuildAlter(t, new[] { "old1" }, DbEngine.SQLite, s => s);
    Check("SQLite 改列跳过", liteStmts.All(s => !s.Contains("ALTER COLUMN")) && liteSkip.Count > 0, string.Join("|", liteStmts));
    Check("SQLite 仍可加列", liteStmts.Contains("ALTER TABLE t ADD c1 INT NOT NULL"), string.Join("|", liteStmts));

    var (pgStmts, _) = TableDdl.BuildAlter(t, new[] { "old1" }, DbEngine.PostgreSQL, s => s);
    Check("PG 改列三条", pgStmts.Any(s => s == "ALTER TABLE t ALTER COLUMN old1 TYPE INT")
        && pgStmts.Any(s => s == "ALTER TABLE t ALTER COLUMN old1 DROP NOT NULL"), string.Join(" | ", pgStmts));
}
{
    Check("默认值 NULL 原样", TableDdl.FormatDefault("null") == "NULL");
    Check("默认值数字原样", TableDdl.FormatDefault("42") == "42" && TableDdl.FormatDefault("1.5") == "1.5");
    Check("默认值文本加引号", TableDdl.FormatDefault("O'Brien") == "'O''Brien'");
    Check("建表语句包装", TableDdl.AsStatements("CREATE TABLE a (id INT);").SequenceEqual(new[] { "CREATE TABLE a (id INT)" }));
}

Console.WriteLine("== BackupTools ==");
{
    Check("支持矩阵", BackupTools.Supported(DbEngine.MySQL) && BackupTools.Supported(DbEngine.PostgreSQL)
        && BackupTools.Supported(DbEngine.SqlServer) && BackupTools.Supported(DbEngine.SQLite)
        && !BackupTools.Supported(DbEngine.Oracle) && !BackupTools.Supported(DbEngine.MongoDB)
        && !BackupTools.Supported(DbEngine.Redis));
    var spec = BackupTools.MysqlDump("127.0.0.1", 3306, "root", "pw123", "mydb", "c:\\dir with space\\dump.sql");
    Check("mysqldump 参数", spec.Arguments.Contains("--host=127.0.0.1") && spec.Arguments.Contains("--port=3306")
        && spec.Arguments.Contains("--user=root") && spec.Arguments.Contains("--result-file=\"c:\\dir with space\\dump.sql\"")
        && spec.Arguments.EndsWith("mydb"), spec.Arguments);
    Check("密码走环境变量", spec.Env["MYSQL_PWD"] == "pw123" && !spec.Describe.Contains("pw123"));
    var rst = BackupTools.MysqlRestore("h", 3306, "u", "p", "db", "f.sql");
    Check("mysql 还原走 stdin", rst.FileName == "mysql" && rst.InputFile == "f.sql", rst.Describe);
    var pg = BackupTools.PgDump("h", 5432, "u", "p", "db", "o.sql");
    Check("pg_dump 参数", pg.FileName == "pg_dump" && pg.Arguments.Contains("--username=u")
        && pg.Arguments.Contains("--file=o.sql") && pg.Arguments.EndsWith("db"), pg.Arguments);
    Check("PG 密码环境变量", pg.Env["PGPASSWORD"] == "p" && !pg.Describe.Contains("p]"));
    Check("psql 还原", BackupTools.PgRestore("h", 5432, "u", "", "db", "f.sql").FileName == "psql");
    Check("参数加引号", BackupTools.QuoteArg("a b") == "\"a b\"" && BackupTools.QuoteArg("abc") == "abc");
    Check("SqlServer 备份语句", BackupTools.SqlServerBackupSql("my db", "d:\\x.bak")
        == "BACKUP DATABASE [my db] TO DISK = 'd:\\x.bak' WITH FORMAT, INIT");
    Check("SqlServer 还原语句", BackupTools.SqlServerRestoreSql("db", "d:\\x.bak")
        == "RESTORE DATABASE [db] FROM DISK = 'd:\\x.bak' WITH REPLACE");
    Check("SqlServer 单引号转义", BackupTools.SqlServerBackupSql("db", "d:\\o'brien.bak").Contains("'d:\\o''brien.bak'"));
}

Console.WriteLine("== Favorites / TabSession ==");
{
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var favFile = Path.Combine(dir, "favorites.json");
    var tabFile = Path.Combine(dir, "tabsession.json");
    Favorites.UseFile(favFile);
    TabSession.UseFile(tabFile);

    Check("收藏初始为空", Favorites.Load().Count == 0);
    var favs = new List<FavoriteItem>();
    Favorites.Add(favs, "日报", "MySQL", "shop", "SELECT * FROM orders;");
    Favorites.Add(favs, "同名覆盖", "PG", "shop", "SELECT 1;");
    Favorites.Add(favs, "同名覆盖", "PG", "shop", "SELECT 2;"); // 同名覆盖
    Check("同名覆盖保留单条", favs.Count(i => i.Name == "同名覆盖") == 1 && favs.Count == 2);
    Check("覆盖后内容更新", favs.First(i => i.Name == "同名覆盖").Sql == "SELECT 2;");
    Favorites.Save(favs);
    var reloaded = Favorites.Load();
    Check("收藏往返", reloaded.Count == 2 && reloaded[0].Name == "日报" && reloaded[0].Sql == "SELECT * FROM orders;");
    Check("删除收藏", Favorites.Remove(reloaded, reloaded[0].Id) && reloaded.Count == 1);
    Check("删除不存在的收藏返回 false", !Favorites.Remove(reloaded, "nope"));

    Check("会话初始为空", TabSession.Load().Count == 0);
    var items = new List<TabSessionItem>
    {
        new() { ConnectionId = "c1", Mode = "Query", Db = "shop", Sql = "SELECT 1;" },
        new() { ConnectionId = "c2", Mode = "TableData", Db = "db", Child = "t1" },
    };
    TabSession.Save(items);
    var rt = TabSession.Load();
    Check("会话往返", rt.Count == 2 && rt[0].ConnectionId == "c1" && rt[0].Sql == "SELECT 1;" && rt[1].Child == "t1");
    var profiles = new List<ConnectionProfile> { new() { Id = "c1" } };
    Check("会话按连接过滤", TabSession.FilterValid(rt, profiles).Count == 1);
    Check("空连接Id保留", TabSession.FilterValid(new List<TabSessionItem> { new() { ConnectionId = "" } }, profiles).Count == 1);

    Directory.Delete(dir, true);
}

Console.WriteLine("== DataSync ==");
{
    Func<string, string> bt = n => "`" + n + "`";
    var srcCols = new List<ColumnSpec>
    {
        new() { Name = "id", DataType = "int", Nullable = false, IsKey = true },
        new() { Name = "name", DataType = "varchar(50)" },
        new() { Name = "extra", DataType = "datetime" },
    };
    var dstNames = new List<string> { "id", "name", "legacy" };
    var (stmts, skipped) = DataSync.SyncColumns("orders", srcCols, dstNames, DbEngine.MySQL, bt, alignTypes: false);
    Check("同步只加缺失列", stmts.Count == 1 && stmts[0].Contains("ADD `extra`") && stmts[0].StartsWith("ALTER TABLE `orders`"), string.Join(" | ", stmts));
    Check("同步不删目标多余列", !string.Join("|", stmts).Contains("DROP") && !string.Join("|", stmts).Contains("legacy"));
    Check("默认不对齐类型", !string.Join("|", stmts).Contains("MODIFY"));
    var (stmts2, _) = DataSync.SyncColumns("orders", srcCols, dstNames, DbEngine.MySQL, bt, alignTypes: true);
    Check("对齐类型生成 MODIFY", stmts2.Any(s => s.Contains("MODIFY COLUMN")), string.Join(" | ", stmts2));
    Check("SQLite 对齐类型入跳过", DataSync.SyncColumns("t", srcCols, dstNames, DbEngine.SQLite, n => "\"" + n + "\"", true).Skipped.Count > 0);
    var create = DataSync.CreateForTarget("orders", srcCols, DbEngine.PostgreSQL, n => "\"" + n + "\"");
    Check("目标缺表生成 CREATE", create.StartsWith("CREATE TABLE \"orders\"") && create.Contains("\"id\" int") && create.Contains("PRIMARY KEY"), create);

    var qr = new QueryResult { Title = "orders" };
    qr.Columns.AddRange(new[] { "id", "name", "flag", "created", "blob" });
    qr.Rows.Add(new object[] { 1, "it's", true, new System.DateTime(2026, 10, 31, 8, 0, 0), new byte[] { 0x01, 0xAB } });
    qr.Rows.Add(new object[] { 2, null, DBNull.Value, null, null });
    qr.Rows.Add(new object[] { 3, "x", false, null, null });
    var batches = DataSync.InsertBatches(qr, "`orders`", bt, batch: 2);
    Check("分批 INSERT 语句数", batches.Count == 2);
    Check("INSERT 列名引用", batches[0].StartsWith("INSERT INTO `orders` (`id`, `name`, `flag`, `created`, `blob`)"), batches[0].Split('\n')[0]);
    Check("字符串转义", batches[0].Contains("'it''s'"));
    Check("布尔转 TRUE/FALSE", batches[0].Contains("TRUE") && batches[1].Contains("FALSE"));
    Check("日期格式", batches[0].Contains("'2026-10-31 08:00:00'"));
    Check("二进制 X''", batches[0].Contains("X'01AB'"));
    Check("null 与 DBNull 转 NULL", batches[1].Contains("NULL") && !batches[1].Contains("DBNull"));
    Check("空结果零语句", DataSync.InsertBatches(new QueryResult { Title = "t" }, "t", bt).Count == 0);
}

Console.WriteLine("== ErdModel ==");
{
    var m = new ErdModel
    {
        Tables =
        {
            new ErdTable { Name = "user", Columns = { new() { Name = "id", Type = "int", IsKey = true }, new() { Name = "name", Type = "varchar(50)" } } },
            new ErdTable { Name = "order", Columns = { new() { Name = "id", Type = "int", IsKey = true }, new() { Name = "user_id", Type = "int" }, new() { Name = "amount", Type = "decimal" } } },
            new ErdTable { Name = "items", Columns = { new() { Name = "id", Type = "int", IsKey = true }, new() { Name = "orderId", Type = "int" } } },
        },
    };
    ErdModel.AutoLayout(m);
    Check("网格布局", m.Tables[0].X == 40 && m.Tables[1].X == 340 && m.Tables[2].X == 640 && m.Tables[2].Y == 40);
    var edges = ErdModel.InferEdges(m);
    Check("推断下划线外键", edges.Count == 2
        && edges.Any(e => e.FromTable == "order" && e.FromCol == "user_id" && e.ToTable == "user")
        && edges.Any(e => e.FromTable == "items" && e.FromCol == "orderId" && e.ToTable == "order"),
        string.Join(",", edges.Select(e => e.FromTable + "." + e.FromCol + "->" + e.ToTable)));
    var m2 = new ErdModel
    {
        Tables = {
            new ErdTable { Name = "t", Columns = { new() { Name = "id", Type = "int", IsKey = true }, new() { Name = "t_id", Type = "int" } } },
        },
    };
    Check("自引用不成边", ErdModel.InferEdges(m2).Count == 0);
    Check("无主键目标不成边", ErdModel.InferEdges(new ErdModel
    {
        Tables = {
            new ErdTable { Name = "a", Columns = { new() { Name = "x_id", Type = "int" } } },
            new ErdTable { Name = "x", Columns = { new() { Name = "code", Type = "int" } } },
        },
    }).Count == 0);
    var json = m.ToJson();
    var back = ErdModel.FromJson(json);
    Check("JSON 往返", back is not null && back.Tables.Count == 3 && back.Tables[1].Columns[1].Name == "user_id"
        && back.Tables[1].X == m.Tables[1].X);
    Check("无效 JSON 返回 null", ErdModel.FromJson("{oops") is null);
}

Console.WriteLine("== ReportBuilder ==");
{
    var qr = new QueryResult { Title = "订单" };
    qr.Columns.AddRange(new[] { "id", "city", "amount", "created" });
    qr.Rows.Add(new object[] { 1, "上海", 120.5, new System.DateTime(2026, 10, 1) });
    qr.Rows.Add(new object[] { 2, "北京", 80, new System.DateTime(2026, 10, 2) });
    qr.Rows.Add(new object[] { 3, "上海", DBNull.Value, new System.DateTime(2026, 10, 2) });
    var (rows, stats) = ReportBuilder.Build(qr);
    Check("报表行数", rows == 3 && stats.Count == 4);
    var idStat = stats[0];
    Check("数值列极值均值", idStat.Min == 1 && idStat.Max == 3 && Math.Abs(idStat.Avg!.Value - 2.0) < 1e-9);
    var cityStat = stats[1];
    Check("非数值列 Top 与空值", cityStat.NullCount == 0 && cityStat.Top[0].Value == "上海" && cityStat.Top[0].Count == 2);
    var amtStat = stats[2];
    Check("DBNull 不进数值统计", amtStat.NullCount == 1 && Math.Abs(amtStat.Avg!.Value - 100.25) < 1e-9 && amtStat.Min == 80);
    var html = ReportBuilder.ToHtml(qr, "月度报表");
    Check("HTML 报表含标题与统计", html.Contains("<title>月度报表</title>") && html.Contains("月度报表") && html.Contains("上海") && html.Contains("100.25"));
    Check("HTML 转义", ReportBuilder.ToHtml(new QueryResult { Title = "a<b" }, "a<b").Contains("a&lt;b"));
    var esc = ReportBuilder.ToHtml(new QueryResult { Title = "t" }, "t");
    Check("HTML 头部完整", esc.StartsWith("<!DOCTYPE html>") && esc.EndsWith("</html>"));
}

Console.WriteLine("== JsonImporter ==");
{
    var (headers, rows) = JsonImporter.Parse("""[{"id":1,"name":"张三","active":true},{"id":2,"name":"李'd四","active":false,"extra":null}]""");
    Check("JSON 数组表头与行数", headers.Count == 4 && rows.Count == 2 && headers[0] == "id" && headers[3] == "extra", string.Join(",", headers));
    Check("JSON 值归一（数字/字符串/布尔/null）", rows[0][0] == "1" && rows[0][1] == "张三" && rows[0][2] == "TRUE" && rows[1][3] == null,
        $"{rows[0][0]},{rows[0][1]},{rows[0][2]},{rows[1][3]}");
    var prov = new TestProv();
    var stmts = CsvImporter.BuildInserts(prov, "db", "t",
        new List<ColumnSpec> { new() { Name = "id", DataType = "INT" }, new() { Name = "name", DataType = "VARCHAR(50)" }, new() { Name = "active", DataType = "INT" }, new() { Name = "extra", DataType = "INT" } },
        headers, rows);
    Check("JSON 生成 INSERT（数字不带引号、字符串转义）",
        stmts.Count == 1 && stmts[0].Contains("INSERT INTO `t` (`id`, `name`, `active`, `extra`) VALUES (1, '张三', 'TRUE', NULL);", StringComparison.Ordinal)
        && stmts[0].Contains("'李''d四'"), stmts[0]);
}
{
    var (headers, rows) = JsonImporter.Parse("{\"id\":1}\n{\"id\":2,\"name\":\"b\"}");
    Check("行式 JSONL 合并表头", headers.Count == 2 && rows.Count == 2 && rows[0][1] == null && rows[1][1] == "b", string.Join(",", headers));
}
{
    var (headers, rows) = JsonImporter.Parse("""[{"id":1,"meta":{"k":1},"tags":[1,2]}]""");
    Check("嵌套对象/数组序列化为文本", headers.Count == 3 && rows[0][1] == """{"k":1}""" && rows[0][2] == "[1,2]", $"{rows[0][1]}|{rows[0][2]}");
}
{
    var (headers, rows) = JsonImporter.Parse("[1,2,3]");
    Check("非对象数组为空", headers.Count == 0 && rows.Count == 0);
}

Console.WriteLine("== TnsNames ==");
{
    const string ora = """
# 数据库连接描述符
ORCL =
  (DESCRIPTION =
    (ADDRESS = (PROTOCOL = TCP)(HOST = dbhost1.example.com)(PORT = 1521))
    (CONNECT_DATA =
      (SERVER = DEDICATED)
      (SERVICE_NAME = orcl.example.com)
    )
  )

ERP, ERP_OLD = (DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=10.0.0.8)(PORT=1522))(CONNECT_DATA=(SERVICE_NAME=erp)))
BAD = (DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(PORT=1523)))
""";
    var list = TnsNames.Parse(ora);
    Check("解析条目数（BAD 无 HOST 跳过）", list.Count == 3, string.Join(",", list.Select(e => e.Name)));
    var orcl = list.FirstOrDefault(e => e.Name == "ORCL");
    Check("ORCL 主机端口服务名", orcl != null && orcl.Host == "dbhost1.example.com" && orcl.Port == 1521 && orcl.Service == "orcl.example.com", orcl?.ToString() ?? "missing");
    Check("多别名拆分", list.Any(e => e.Name == "ERP") && list.Any(e => e.Name == "ERP_OLD") && list.All(e => e.Name != " ERP"));
    var erp = list.First(e => e.Name == "ERP");
    Check("ERP 条目字段", erp.Host == "10.0.0.8" && erp.Port == 1522 && erp.Service == "erp");
    Check("注释被剔除", list.All(e => e.Host != "注释主机") && TnsNames.Parse("# ORCL = (HOST=x)\nREAL = (HOST=h)(PORT=1)").Count == 1);
}

{
    // 主题三象：配色完整性、铁律与回落规则（水墨/天青/墨夜，ant 退役映射天青；天青 2026-11-06 对齐 TaoAgent 色板）
    Check("三象恰三套且同键集",
        ThemePalette.Names.Length == 3
        && ThemePalette.Names[0] == "ink" && ThemePalette.Names[1] == "gray" && ThemePalette.Names[2] == "night"
        && new[] { ThemePalette.Ink, ThemePalette.Gray, ThemePalette.Night }
            .All(pal => pal.Count == ThemePalette.Keys.Length && ThemePalette.Keys.All(k => pal.ContainsKey(k))));
    bool HexOk(string h) => h.StartsWith('#') && h.Length == 7;
    Check("三象色值格式合法",
        new[] { ThemePalette.Ink, ThemePalette.Gray, ThemePalette.Night }
            .All(pal => ThemePalette.Keys.All(k => HexOk(pal[k]))));
    (int, int, int) Rgb(string h) => (Convert.ToInt32(h.Substring(1, 2), 16), Convert.ToInt32(h.Substring(3, 2), 16), Convert.ToInt32(h.Substring(5, 2), 16));
    var inkBg = Rgb(ThemePalette.Ink["AntBg"]);
    var grayBg = Rgb(ThemePalette.Gray["AntBg"]);
    var nightBg = Rgb(ThemePalette.Night["AntBg"]);
    // 亮度阶梯定律：水墨浅色为主 / 天青釉底浅调（与水墨同为浅色象，带内次序水墨>天青）/ 墨夜以黑为主，三带 L 单调递减
    double RelL(string h)
    {
        var (r, g, b) = Rgb(h);
        double F(int c) { var v = c / 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        return 0.2126 * F(r) + 0.7152 * F(g) + 0.0722 * F(b);
    }
    double LInk = RelL(ThemePalette.Ink["AntBg"]), LGray = RelL(ThemePalette.Gray["AntBg"]), LNight = RelL(ThemePalette.Night["AntBg"]);
    Check("亮度阶梯：三带单调递减", LInk > LGray && LGray > LNight && LInk > 0.8 && LGray > 0.8 && LGray < LInk && LNight < 0.05,
        $"L={LInk:F2}/{LGray:F2}/{LNight:F2}");
    Check("墨夜铁律：以黑为主且暖序", nightBg.Item1 == 0x13 && nightBg.Item2 == 0x12 && nightBg.Item3 == 0x11);
    Check("墨夜母本锚点：宣纸白与黛青",
        ThemePalette.Night["AntText"] == "#E8E3D5" && ThemePalette.Night["AntTextSub"] == "#ABA595"
        && ThemePalette.Night["AntPrimary"] == "#6FA3BC" && ThemePalette.Night["AntCard"] == "#211E1B");
    Check("天青铁律：釉底浅调 G>B>R 且卡片面亮于底",
        grayBg.Item2 > grayBg.Item3 && grayBg.Item3 > grayBg.Item1
        && ThemePalette.Gray["AntBg"] == "#EDF2EF" && ThemePalette.Gray["AntCard"] == "#FAFCFA"
        && RelL(ThemePalette.Gray["AntCard"]) > LGray);
    Check("天青锚点值：主字与青瓷点缀",
        ThemePalette.Gray["AntText"] == "#2B3A36" && ThemePalette.Gray["AntPrimary"] == "#34655C");
    Check("水墨基调未漂移", ThemePalette.Ink["AntBg"] == "#F7F5EE" && ThemePalette.Ink["AntPrimary"] == "#52708A");
    // WCAG 体检（视觉观测验收定律第一层）：正文 4.5 / 次要 3.5 / 点缀 3.0 / 描边 2.0 / 按钮字 4.0
    double Contrast(string a, string b)
    {
        double la = RelL(a), lb = RelL(b);
        var hi = Math.Max(la, lb); var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }
    foreach (var (tname, pal) in new[] { ("水墨", ThemePalette.Ink), ("天青", ThemePalette.Gray), ("墨夜", ThemePalette.Night) })
    {
        var bg = pal["AntBg"];
        var cText = Contrast(pal["AntText"], bg);
        var cSub = Contrast(pal["AntTextSub"], bg);
        var cPri = Contrast(pal["AntPrimary"], bg);
        var cBor = Contrast(pal["AntBorder"], bg);
        Check(tname + " WCAG：正文/次要/点缀/描边",
            cText >= 4.5 && cSub >= 3.5 && cPri >= 3.0 && cBor >= 2.0,
            tname + $" 正文 {cText:F2} 次要 {cSub:F2} 点缀 {cPri:F2} 描边 {cBor:F2}");
        // 按钮字：主色底上的文字（水墨/天青白字，墨夜近黑字）
        var btnText = tname == "墨夜" ? "#131211" : "#FFFFFF";
        var cBtn = Contrast(btnText, pal["AntPrimary"]);
        Check(tname + " WCAG：按钮字", cBtn >= 4.0, tname + $" 按钮字 {cBtn:F2}");
    }
    Check("未知主题回落水墨", ThemePalette.Get("nope") == ThemePalette.Ink && ThemePalette.Normalize("nope") == "ink" && ThemePalette.Normalize(null) == "ink");
    Check("ant 退役映射天青", ThemePalette.Get("ant") == ThemePalette.Gray && ThemePalette.Normalize("ANT") == "gray" && ThemePalette.Normalize("ant") == "gray");
    Check("三象大小写归一", ThemePalette.Normalize("Night") == "night" && ThemePalette.Normalize(" GRAY ") == "gray" && ThemePalette.Normalize("INK") == "ink");
}

Layer("集成");
{
    // CSV 全链路：导出 → 解析 → 生成 INSERT，行列数与关键值一致
    var r = new QueryResult { Columns = { "id", "name", "note" } };
    r.Rows.Add(new object?[] { 1, "张三", "有,\"逗号\"\n第二行" });
    r.Rows.Add(new object?[] { 2, DBNull.Value, 3.5 });
    var csv = Exporter.Csv(r);
    var (heads, rows) = CsvImporter.Parse(csv);
    Check("CSV 导出→解析往返", heads.SequenceEqual(r.Columns) && rows.Count == 2, string.Join(",", heads));
    Check("CSV 往返值一致", rows[0][1] == "张三" && rows[1][1] == null && rows[1][2] == "3.5", string.Join("|", rows[1]));
    var cols = new List<ColumnSpec> { new() { Name = "id", DataType = "int" }, new() { Name = "name", DataType = "varchar(50)" }, new() { Name = "note", DataType = "text" } };
    var ins = CsvImporter.BuildInserts(new TestProv(), "shop", "t", cols, heads, rows);
    Check("CSV→INSERT 全链路", ins.Count >= 1 && ins.Sum(s => s.Split("VALUES (").Length - 1) == 2 && ins[0].Contains("INSERT INTO `t`") && ins[0].Contains("'张三'"), ins.FirstOrDefault() ?? "");
}
{
    // JSON 全链路：解析（含畸形行跳过）→ 生成 INSERT
    var (h, rr) = JsonImporter.Parse("{\n{\"bad\";\n} not json line\n{\"a\":1,\"b\":null}");
    Check("JSONL 畸形行跳过", rr.Count == 1 && rr[0][0] == "1", string.Join(",", h));
    var (h2, r2) = JsonImporter.Parse("[{\"id\":1,\"extra\":{\"x\":2}}]");
    var jc = new List<ColumnSpec> { new() { Name = "id", DataType = "int" }, new() { Name = "extra", DataType = "text" } };
    var ji = JsonImporter.BuildInserts(new TestProv(), "db", "t", jc, h2, r2);
    Check("JSON→INSERT 嵌套序列化", ji.Count == 1 && ji[0].Contains("\"x\":2"), ji.FirstOrDefault() ?? "");
}
{
    // ER 模型 JSON 往返 + 报表 HTML 转义
    var m = new ErdModel { Tables = { new ErdTable { Name = "users", Columns = { new() { Name = "id", Type = "int", IsKey = true }, new() { Name = "user_id", Type = "int" } } } } };
    ErdModel.AutoLayout(m);
    var rt = ErdModel.FromJson(m.ToJson());
    Check("ER 模型 JSON 往返", rt != null && rt.Tables.Count == m.Tables.Count && rt.Tables[0].Name == "users", rt?.Tables.Count.ToString() ?? "null");
    var rep = ReportBuilder.ToHtml(new QueryResult { Columns = { "c" }, Rows = { new object?[] { "<script>alert(1)</script>" } } }, "安全");
    Check("报表 HTML 转义", rep.Contains("&lt;script&gt;") && !rep.Contains("<script>alert"), "");
}
{
    // 传输三段组合：建表 → 补列 → 分批插入 语句顺序完整
    var cols = new List<ColumnSpec> { new() { Name = "id", DataType = "int", IsKey = true } };
    var create = DataSync.CreateForTarget("t", cols, DbEngine.MySQL, n => "`" + n + "`");
    var (sync, _) = DataSync.SyncColumns("t", cols, new List<string> { "id" }, DbEngine.MySQL, n => "`" + n + "`", alignTypes: false);
    var qr = new QueryResult { Title = "t" }; qr.Columns.Add("id"); qr.Rows.Add(new object[] { 7 });
    var ins = DataSync.InsertBatches(qr, "`t`", n => "`" + n + "`");
    Check("传输语句组合完整", create.Contains("CREATE TABLE") && sync.Count == 0 && ins.Count == 1 && ins[0].Contains("VALUES (7)"), create);
}

Layer("系统");
{
    // 持久化完整工作流（临时目录）：收藏 + 会话 文件往返、损坏文件容错、失败路径不残留
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-sys-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        Favorites.UseFile(Path.Combine(dir, "favorites.json"));
        TabSession.UseFile(Path.Combine(dir, "tabsession.json"));
        var favs = Favorites.Add(new List<FavoriteItem>(), "系统层", "MySQL", "db", "SELECT 1;");
        Favorites.Save(favs);
        Check("系统层：收藏文件往返", Favorites.Load().Count == 1 && Favorites.Load()[0].Name == "系统层");

        var badFav = Path.Combine(dir, "favorites.json");
        var good = File.ReadAllText(badFav);
        File.WriteAllText(badFav, "{corrupted json!!");
        Check("损坏收藏文件安全降级", Favorites.Load().Count == 0);
        Check("损坏文件原样保留", File.ReadAllText(badFav) == "{corrupted json!!");
        Check("失败路径无临时残留", Directory.GetFiles(dir).Length == 1);
        File.WriteAllText(badFav, good);

        File.WriteAllText(Path.Combine(dir, "tabsession.json"), "not-json-at-all");
        Check("损坏会话文件安全降级", TabSession.Load().Count == 0);
    }
    finally
    {
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
    }
}
{
    // 主题选择持久化：往返、损坏文件安全回落、未知值回落、失败路径无残留
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-theme-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        ThemeStore.UseFile(Path.Combine(dir, "theme.json"));
        Check("主题默认水墨", ThemeStore.Load() == "ink");
        ThemeStore.Save("night");
        Check("主题选择往返", ThemeStore.Load() == "night");
        ThemeStore.Save("ant");
        Check("旧 ant 选择映射天青", ThemeStore.Load() == "gray");
        File.WriteAllText(Path.Combine(dir, "theme.json"), "{oops");
        Check("损坏主题文件回落水墨", ThemeStore.Load() == "ink");
        Check("主题文件原样保留", File.ReadAllText(Path.Combine(dir, "theme.json")) == "{oops");
        Check("失败路径无临时残留", Directory.GetFiles(dir).Length == 1);
    }
    finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
{
    // 托盘关闭偏好持久化：默认询问、tray/exit 往返、损坏文件安全回落、失败路径无残留
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-prefs-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        AppPrefs.UseFile(Path.Combine(dir, "appprefs.json"));
        Check("关闭行为默认询问", AppPrefs.Load() == CloseBehavior.Ask);
        AppPrefs.Save(CloseBehavior.Tray);
        Check("托盘最小化偏好往返", AppPrefs.Load() == CloseBehavior.Tray);
        AppPrefs.Save(CloseBehavior.Exit);
        Check("直接退出偏好往返", AppPrefs.Load() == CloseBehavior.Exit);
        File.WriteAllText(Path.Combine(dir, "appprefs.json"), "{broken");
        Check("损坏偏好文件回落询问", AppPrefs.Load() == CloseBehavior.Ask);
        Check("偏好文件原样保留", File.ReadAllText(Path.Combine(dir, "appprefs.json")) == "{broken");
        Check("失败路径无临时残留", Directory.GetFiles(dir).Length == 1);
    }
    finally { AppPrefs.UseFile(""); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
{
    // 托盘健在断言（2026-11-03 真机托盘不见的教训常驻）：窗口句柄必须随消息上送，增改删同源；
    // 添加结果不得被掩盖；explorer 重启后自动补挂；右键必须带「我的连接」常用区且主窗已接线。
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var root = ProjRoot();
    var tray = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", "TrayIcon.cs"));
    Check("托盘注册带主窗句柄（NIM_ADD 成败的唯一键）", tray.Contains("hWnd = _ownerHwnd"));
    Check("托盘添加结果如实记录不被掩盖", tray.Contains("_added = Shell_NotifyIconW(NIM_ADD"));
    Check("explorer 重启后托盘自动补挂", tray.Contains("TaskbarCreated") && tray.Contains("AddToShell"));
    Check("托盘右键带我的连接动态区", tray.Contains("RecentProvider") && tray.Contains("RecentRequested") && tray.Contains("我的连接"));
    var mwCs3 = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml.cs"));
    Check("主窗接线托盘连接快捷区", mwCs3.Contains("RecentProvider") && mwCs3.Contains("TrayConnect"));
    // 关窗分流铁律（2026-11-03 真机反馈常驻）：托盘没挂上时绝不隐藏玩消失——要么真挂上再拦，要么如实退出
    Check("托盘挂载状态如实可查", tray.Contains("public bool IsAdded"));
    Check("托盘未挂上时关窗不拦截", mwCs3.Contains("!_tray.IsAdded") && mwCs3.Contains("try { AppWindow.Hide(); }"));
    // 2026-11-03 二轮兜底（真机复测仍看不到托盘）：资源缺失不连坐、失败自动重试、版本可验装
    Check("托盘创建不以图标文件存在为前提", mwCs3.Contains("StartTray(trayIcoPath);"));
    Check("托盘图标缺失回落系统图标", tray.Contains("LoadIconW"));
    Check("托盘未挂上自动重试", tray.Contains("public void RetryAdd") && mwCs3.Contains("StartTrayRetry()"));
    // 2026-11-03 三轮（真机日志实锤：自建消息窗口 CreateWindowExW 返回空，托盘整链失效）：改托管主窗句柄
    Check("托盘消息托管主窗不再自建窗口", tray.Contains("SetWindowLongPtrW") && !tray.Contains("CreateWindowExW"));
    Check("子类化其余消息链回原过程", tray.Contains("CallWindowProcW"));
    Check("退出时还原主窗过程", tray.Contains("GWLP_WNDPROC") && tray.Contains("_prevProc"));
    Check("托盘诊断写入日志且带错误码", tray.Contains("Core.AppLog.Write") && tray.Contains("GetLastWin32Error"));
    Check("日志目录固定在数据根 logs", File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "Core", "AppLog.cs")).Contains("\"logs\""));
    // 陶瓷质感（2026-11-06 用户定稿，对齐 TaoAgent/TaoPad）：卡片面 XAML 资源换纵向釉面微渐变，
    // 三套主题统一生效；异常退回纯色不阻断换肤；墨夜配色本体不被触碰（母本锚点断言在上文主题块）。
    var antCs = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", "Ant.cs"));
    Check("陶瓷质感：釉面微渐变挂载到卡片资源且随主题重涂",
        antCs.Contains("ApplyCeramicCard(pal)") && antCs.Contains("LinearGradientBrush") && antCs.Contains("Offset = 0.55"));
    Check("陶瓷质感：通道增减受钳制且异常退回纯色画刷",
        antCs.Contains("Math.Min(255, v + 15)") && antCs.Contains("Math.Max(0, v - 12)")
        && antCs.Contains("Application.Current.Resources[\"AntCard\"] = brush") && antCs.Contains("Application.Current.Resources[\"AntCard\"] = Card"));
    var csprojText = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "TaoDBM.csproj"));
    var wxsText = File.ReadAllText(Path.Combine(root, "src", "Installer", "Package.wxs"));
    Check("版本号升级可验装（标题栏 v1.0.8）", csprojText.Contains("<Version>1.0.8<") && wxsText.Contains("Version=\"1.0.8\""));
    // 弹窗表单右缘让出滚动条带（2026-11-03 真机截图：浮层滚动条压在输入框右缘像一条越界竖线）
    int bandOk = 0;
    foreach (var f in new[] { "ConnectionDialog.cs", "DataSyncDialog.cs", "FakerDialog.cs", "WorkTab.cs" })
        if (File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", f)).Contains("Margin = new Thickness(0, 0, 12, 0)")) bandOk++;
    Check("弹窗表单统一让出滚动条带（4 处）", bandOk == 4, $"{bandOk}/4");
}
{
    // 存储落 D 盘（Tao 系列）：回退目录契约 + 旧数据迁移（已有同名不覆盖）+ 失败安全
    Check("存储根目录可用", Directory.Exists(TaoPaths.Root) && Path.IsPathRooted(TaoPaths.Root));
    Check("存储根即回退目录（非 Windows）", OperatingSystem.IsWindows() || TaoPaths.Root.Contains("TaoDBM"));
    var fp = TaoPaths.FilePath("probe-test.json");
    Check("FilePath 自动建目录", File.Exists(fp) == false && Directory.Exists(TaoPaths.Root));
    var migDir = Path.Combine(Path.GetTempPath(), "taodbm-mig-" + Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(Path.Combine(migDir, "old"));
        Directory.CreateDirectory(Path.Combine(migDir, "new"));
        File.WriteAllText(Path.Combine(migDir, "old", "favorites.json"), "[{\"Name\":\"迁移\"}]");
        File.WriteAllText(Path.Combine(migDir, "old", "connections.json"), "[]");
        var newRoot = Path.Combine(migDir, "new");
        File.WriteAllText(Path.Combine(newRoot, "favorites.json"), "[{\"Name\":\"已有\"}]");
        var moved = TaoPaths.MigrateLegacyFiles([Path.Combine(migDir, "old"), Path.Combine(migDir, "new")], targetRoot: newRoot);
        Check("迁移复制缺失文件", File.ReadAllText(Path.Combine(newRoot, "connections.json")) == "[]" && moved.Contains("connections.json"), string.Join(",", moved));
        Check("迁移不覆盖已有文件", File.ReadAllText(Path.Combine(newRoot, "favorites.json")) == "[{\"Name\":\"已有\"}]" && !moved.Contains("favorites.json"));
        Check("迁移失败路径安全（源目录缺失不抛）", TaoPaths.MigrateLegacyFiles([Path.Combine(migDir, "no-such-dir")], targetRoot: newRoot).Count == 0);
    }
    finally { if (Directory.Exists(migDir)) Directory.Delete(migDir, true); }
}
{
    // 大数据分块往返：5 万行 CSV → 解析 → INSERT，行数字节级一致
    var sb = new StringBuilder("id,tag\n");
    for (int i = 0; i < 50000; i++) sb.Append(i).Append(",tag_").Append(i % 7).Append('\n');
    var (bh, br) = CsvImporter.Parse(sb.ToString());
    var bc = new List<ColumnSpec> { new() { Name = "id", DataType = "int" }, new() { Name = "tag", DataType = "varchar(20)" } };
    var inserts = CsvImporter.BuildInserts(new TestProv(), "db", "big", bc, bh, br, batch: 500);
    var total = inserts.Sum(x => x.Split("VALUES (").Length - 1);
    Check("5 万行往返行数一致", br.Count == 50000 && total == 50000, $"{br.Count}/{total}");
}

{
    // 层色底定律（TaoZip v62 母本）断言锁：页底显式、空态禁三级灰、标签根板层色
    string SrcRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var srcRoot = SrcRoot();
    var xaml = File.ReadAllText(Path.Combine(srcRoot, "src", "TaoDBM", "MainWindow.xaml"));
    Check("主窗根板钉主题底色", xaml.Contains("<Grid Background=\"{ThemeResource AntBg}\">"));
    var tab = File.ReadAllText(Path.Combine(srcRoot, "src", "TaoDBM", "UI", "WorkTab.cs"));
    Check("标签根板层色底在位", tab.Contains("Background = Ant.Bg;"));
    bool EmptyHintOk(string marker)
    {
        int i = tab.IndexOf(marker, StringComparison.Ordinal);
        return i >= 0 && tab[i..Math.Min(i + 300, tab.Length)].Contains("Foreground = Ant.TextSub");
    }
    Check("空态提示用次要前景", EmptyHintOk("还没有收藏") && EmptyHintOk("还没有历史记录"));
}
{
    // 命名与 BOM 铁律（2026-11-01 全系避坑常驻，源自 TaoPad v74 审计）：源码树本身必须干净
    string TreeRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var treeRoot = TreeRoot();
    bool clean = true; string? hit = null;
    foreach (var p in Directory.EnumerateFiles(treeRoot, "*", SearchOption.AllDirectories))
    {
        var rel = p.Substring(treeRoot.Length + 1).Replace('\\', '/');
        if (rel.Contains("/obj/") || rel.Contains("/bin/") || rel.Contains("/.git/") || rel.Contains("/.vs/")
            || rel.StartsWith("typecheck/") || rel.StartsWith("logictest/") || rel.StartsWith("docs/")) continue;
        if (rel.Any(c => c > 127)) { clean = false; hit = rel; break; }
        if (p.EndsWith(".cs") || p.EndsWith(".xaml"))
        {
            var b = File.ReadAllBytes(p);
            if (b.Length < 3 || b[0] != 0xEF || b[1] != 0xBB || b[2] != 0xBF) { clean = false; hit = rel; break; }
        }
    }
    Check("源码树全 ASCII 且 .cs/.xaml 带 BOM（首违规: " + (hit ?? "无") + "）", clean);
}
{
    // 工程文件结构铁律（2026-11-01 真机 MSB4066 教训）：PropertyGroup 内只许属性，条目元素必须在 ItemGroup
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var csproj = Path.Combine(ProjRoot(), "src", "TaoDBM", "TaoDBM.csproj");
    var doc = System.Xml.Linq.XDocument.Load(csproj);
    string[] itemTags = { "Content", "Page", "None", "PackageReference", "ProjectReference", "Reference", "EmbeddedResource", "Compile", "ApplicationDefinition", "Resource", "Folder" };
    var stray = doc.Descendants("PropertyGroup")
        .SelectMany(pg => pg.Elements())
        .Where(e => itemTags.Contains(e.Name.LocalName))
        .Select(e => e.Name.LocalName)
        .ToList();
    Check("csproj PropertyGroup 内无条目元素（发现: " + string.Join(",", stray) + "）", stray.Count == 0);
    // 批处理换行铁律（2026-11-01 真机乱码嚼行教训）：.bat 必须 CRLF；根目录=GBK 编码，build/*.utf8.bat=UTF-8 编码
    var projRoot = ProjRoot();
    int batBad = 0; string batHit = "";
    foreach (var p in Directory.EnumerateFiles(projRoot, "*.bat", SearchOption.AllDirectories))
    {
        var rel = p.Substring(projRoot.Length + 1).Replace('\\', '/');
        if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
        var b = File.ReadAllBytes(p);
        int bareLf = 0;
        for (int i = 0; i < b.Length; i++)
            if (b[i] == 10 && (i == 0 || b[i - 1] != 13)) bareLf++;
        bool isUtf8Variant = rel.Contains(".utf8.");
        bool utf8Clean = !System.Text.Encoding.UTF8.GetString(b).Contains('\uFFFD');
        bool hasHighBytes = b.Any(x => x >= 0x80);
        bool encOk = isUtf8Variant ? utf8Clean : (hasHighBytes ? !utf8Clean : true);
        if (bareLf > 0 || !encOk) { batBad++; batHit = rel; }
    }
    Check("全部 .bat 为 CRLF 且编码合规（首违规: " + batHit + "）", batBad == 0);
}
{
    // 换肤三闸（2026-11-01 对照 TaoDown v2.36 换肤失效教训）：页面笔刷消费 + 标题栏钉色 + 强调色随象
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var root = ProjRoot();
    var mwXaml = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml"));
    Check("页面层无写死旧蓝 #1677FF", !mwXaml.Contains("#1677FF"));
    var mwCs = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml.cs"));
    Check("标题栏随象而染（AppWindow.TitleBar 钉色在位）", mwCs.Contains("AppWindow.TitleBar") && mwCs.Contains("ButtonHoverBackgroundColor"));
    var ant = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", "Ant.cs"));
    Check("系统强调色族随主题（accent 挂会变色画刷）", ant.Contains("SystemControlForegroundAccentBrush") && ant.Contains("ThemeApplied"));
}
{
    // 换肤收尾（2026-11-02 同步全系最新要求）：accent 全族随象 + 主色底字随象 + 高亮/报告随象
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var root = ProjRoot();
    var appXaml = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "App.xaml"));
    Check("App.xaml 不写死 accent 色族（含浅深变体全由 Ant 运行时管）", !appXaml.Contains("SystemAccentColor") && !appXaml.Contains("#1677FF"));
    var antCs = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", "Ant.cs"));
    Check("主色底字随象（OnPrimary 画刷，墨夜近黑，accent 浅深变体同源）",
        antCs.Contains("OnPrimary") && antCs.Contains("131211") && antCs.Contains("SystemAccentColorLight1") && antCs.Contains("SystemAccentColorDark1"));
    string uiHit = "";
    foreach (var f in Directory.GetFiles(Path.Combine(root, "src", "TaoDBM", "UI"), "*.cs"))
    {
        var s = File.ReadAllText(f);
        bool hardWhite = s.Contains("Colors.White");
        bool hardBlue = Path.GetFileName(f) != "Ant.cs" && s.Contains("#1677FF");
        bool hlOld = s.Contains("Ant.Color(\"1677FF\")");
        if (hardWhite || hardBlue || hlOld) { uiHit = Path.GetFileName(f); break; }
    }
    Check("界面层无写死旧蓝与死白字（首违规: " + uiHit + "）", uiHit == "");
    var mwXaml = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml"));
    Check("顶栏文字随象（AntOnPrimary 资源在位）", mwXaml.Contains("AntOnPrimary") && !mwXaml.Contains("Foreground=\"White\""));
    var wtCs = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "UI", "WorkTab.cs"));
    Check("SQL 高亮关键字取主题主色且换肤重刷", wtCs.Contains("Ant.Primary.Color") && wtCs.Contains("Ant.Text.Color") && wtCs.Contains("ThemeApplied"));
    var rb = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "Core", "ReportBuilder.cs"));
    Check("HTML 报告取色随主题", !rb.Contains("#1677FF") && rb.Contains("AntPrimary"));
}
{
    // 构建闸流程铁律（2026-11-02 真机教训：成功路径掉进失败块，闸把完整好包全拦了）：
    // 两份 bat 必须三段式——探针失败→:unpackbad，自检失败→:checkbad，全过→显式 goto :gateok 放行
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    static int BCount(byte[] arr, byte[] pat)
    {
        int c = 0;
        for (int i = 0; i <= arr.Length - pat.Length; i++)
        {
            bool m = true;
            for (int j = 0; j < pat.Length; j++) if (arr[i + j] != pat[j]) { m = false; break; }
            if (m) c++;
        }
        return c;
    }
    static int BIndex(byte[] arr, byte[] pat)
    {
        for (int i = 0; i <= arr.Length - pat.Length; i++)
        {
            bool m = true;
            for (int j = 0; j < pat.Length; j++) if (arr[i + j] != pat[j]) { m = false; break; }
            if (m) return i;
        }
        return -1;
    }
    var root = ProjRoot();
    int badFlow = 0; string batHit = "";
    foreach (var rel in new[] { "build-installer.bat", "build/build-installer.utf8.bat" })
    {
        var bb = File.ReadAllBytes(Path.Combine(root, rel));
        byte[] P(string s) => System.Text.Encoding.UTF8.GetBytes(s);
        bool hasGates = BCount(bb, P("goto :gateok")) == 1 && BCount(bb, P("goto :checkbad")) == 1
            && BCount(bb, P("goto :unpackbad")) == 3 && BCount(bb, P("if errorlevel 1 goto :checkbad")) == 1
            && BCount(bb, P("-pdbtype none")) == 1 && BCount(bb, P("out\\*.msi")) == 1;
        int iUn = BIndex(bb, P("\r\n:unpackbad\r\n")), iCh = BIndex(bb, P("\r\n:checkbad\r\n")), iGo = BIndex(bb, P("\r\n:gateok\r\n"));
        bool ordered = iUn >= 0 && iUn < iCh && iCh < iGo;
        if (!hasGates || !ordered) { badFlow++; batHit = rel; }
    }
    Check("构建闸三段式流程齐备（首违规: " + batHit + "）", badFlow == 0);
}
{
    // 快捷方式与顶栏铁律（2026-11-02 真机教训）：
    // ①自定义动作设置的目录属性值必须以目录分隔符结尾（MSI 官方规则），否则快捷方式 Target 拼接黏连成坏路径；
    // ②两条快捷方式必须显式带 Icon 引用（TaoZip/TaoDown 同款）；③开始菜单目录须带卸载清理；
    // ④主题菜单独立成右上角按钮（关于左侧），图标菜单不再含主题项；
    // ⑤图标菜单贴图标左缘防卡片溢出主窗，主题菜单右缘对齐防右溢。
    string ProjRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && d != null; i++, d = d.Parent!)
            if (File.Exists(Path.Combine(d.FullName, "src", "TaoDBM", "MainWindow.xaml"))) return d.FullName;
        return AppContext.BaseDirectory;
    }
    var root = ProjRoot();
    var wxs = File.ReadAllText(Path.Combine(root, "src", "Installer", "Package.wxs"));
    Check("目录属性赋值以分隔符结尾（快捷方式目标防黏连）", wxs.Contains("Value=\"D:\\Program Files\\TaoDBM\\\""));
    Check("两条快捷方式显式带安装包图标引用", System.Text.RegularExpressions.Regex.Matches(wxs, "Icon=\"TaoDBMIco\"").Count == 2);
    Check("开始菜单目录卸载清理在位", wxs.Contains("RemoveStartMenuDir"));
    var mwXaml = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml"));
    Check("主题菜单独立成钮且在关于左侧（全系勾选样式）",
        mwXaml.Contains("BtnTheme") && mwXaml.IndexOf("BtnTheme") < mwXaml.IndexOf("BtnAbout")
        && System.Text.RegularExpressions.Regex.Matches(mwXaml, "ToggleMenuFlyoutItem").Count == 3
        && !mwXaml.Contains("主题："));
    var mwCs2 = File.ReadAllText(Path.Combine(root, "src", "TaoDBM", "MainWindow.xaml.cs"));
    Check("主题对勾随象刷新（切象同步勾选+启动初始化）",
        mwCs2.Contains("UpdateThemeChecks") && mwCs2.Contains("ThemeInk.IsChecked")
        && mwCs2.Contains("ThemeSky.IsChecked") && mwCs2.Contains("ThemeNight.IsChecked"));
    Check("菜单卡片贴边防溢出（图标左缘对齐/主题右缘对齐）",
        mwXaml.Contains("Placement=\"BottomEdgeAlignedLeft\"") && mwXaml.Contains("Placement=\"BottomEdgeAlignedRight\""));
}
{
    // 全测三件套之真人模拟操作链：拟人完整路径（切主题→查历史→收藏→会话恢复→退出重载）
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-human-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    ThemeStore.UseFile(Path.Combine(dir, "theme.json"));
    Favorites.UseFile(Path.Combine(dir, "favorites.json"));
    TabSession.UseFile(Path.Combine(dir, "tabsession.json"));
    QueryHistory.UseFile(Path.Combine(dir, "history.json"));
    try
    {
        // ① 挑主题（三象各切一遍，退出前停在水墨）
        foreach (var th in ThemePalette.Names) ThemeStore.Save(th);
        Check("拟人：换装一轮后停在墨夜", ThemeStore.Load() == "night");
        ThemeStore.Save("ink");
        // ② 查历史（连续 5 条，最新在前）
        for (int i = 0; i < 5; i++) QueryHistory.Add("mysql", "db1", $"SELECT {i}");
        var hist = QueryHistory.Items;
        Check("拟人：历史最新在前且全在位", hist.Count >= 5 && hist[0].Sql == "SELECT 4");
        // ③ 收藏一条常用 SQL 并能找回
        var favs = Favorites.Load();
        Favorites.Save(Favorites.Add(favs, "常用-用户", "mysql", "db1", "SELECT * FROM users WHERE name LIKE '%tao%'"));
        Check("拟人：收藏往返", Favorites.Load().Any(f => f.Name == "常用-用户"));
        // ④ 会话恢复（两个标签）
        var tabs = new List<TabSessionItem> { new() { ConnectionId = "c1", Mode = "Query", Db = "db1", Sql = "SELECT 1" }, new() { ConnectionId = "c2", Mode = "Browse", Db = "db0", Child = "users" } };
        TabSession.Save(tabs);
        var back = TabSession.Load();
        Check("拟人：会话标签完整恢复", back.Count == 2 && back[0].ConnectionId == "c1" && back[1].Child == "users");
    }
    finally
    {
        ThemeStore.UseFile(""); Favorites.UseFile(""); TabSession.UseFile(""); QueryHistory.UseFile("");
        Directory.Delete(dir, true);
    }
}

Layer("暴力");

// A1 App 工程引用自洽（2026-11-01 TaoPad 真机 CS0103 避坑，全系同型）：
// 沙箱不编译 WinUI App 工程，界面侧编译错误只有真机能看见——
// 源码层兜底：App 工程所有 .cs 对 ThemePalette/ThemeStore 的静态成员引用必须有定义。
{
    string coreDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "src", "TaoDBM"));
    var defs = File.ReadAllText(Path.Combine(coreDir, "Core", "ThemePalette.cs"))
        + File.ReadAllText(Path.Combine(coreDir, "Core", "ThemeStore.cs"));
    var orphans = new List<string>();
    foreach (var f in Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories)
        .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                 && !f.EndsWith("ThemePalette.cs") && !f.EndsWith("ThemeStore.cs")))
    {
        var src = File.ReadAllText(f);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(src, @"(?:ThemePalette|ThemeStore)\.(\w+)"))
            if (!System.Text.RegularExpressions.Regex.IsMatch(defs, @"\b" + m.Groups[1].Value + @"\b"))
                orphans.Add($"{Path.GetFileName(f)}:{m.Groups[1].Value}");
    }
    Check("A1 App工程引用自洽：ThemePalette/ThemeStore 成员引用皆有定义",
        orphans.Count == 0, orphans.Count > 0 ? $"孤儿引用 {string.Join(",", orphans)}" : "自洽");
}

{
    // 模糊输入 1 万条 × 全部解析入口，全都不允许抛异常
    var rnd = new Random(20261101);
    string Nasty()
    {
        var pool = "'\"; --/*\n { } []() {},:0aA#\u4e2d\u6587\ud83d\ude00\t\\x00', ' OR 1=1--, DROP TABLE t; {}{{ }}[, ]";
        var n = rnd.Next(1, 40);
        var cs = new char[n];
        for (int i = 0; i < n; i++) cs[i] = pool[rnd.Next(pool.Length)];
        return new string(cs);
    }
    int exceptions = 0;
    for (int i = 0; i < 10000; i++)
    {
        var s = Nasty();
        try { SqlSplitter.Split(s); } catch { exceptions++; }
        try { TnsNames.Parse(s); } catch { exceptions++; }
        try { CsvImporter.Parse(s); } catch { exceptions++; }
        try { JsonImporter.Parse(s); } catch { exceptions++; }
        try { SqlHighlighter.Tokenize(s); } catch { exceptions++; }
    }
    Check("1 万条模糊输入全入口零异常", exceptions == 0, $"异常 {exceptions} 次");
    var inject = SqlSplitter.Split("'; DROP TABLE users; --");
    Check("注入载荷不产生可执行边界", inject.All(x => x.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || !x.Contains("DROP", StringComparison.OrdinalIgnoreCase)) || inject.Count <= 2, string.Join("|", inject));
}
{
    // 并发压测：8 线程 × 500 次，四个解析入口并行无异常无串扰
    int errs = 0;
    Parallel.For(0, 8, _ =>
    {
        var rnd = new Random(Environment.CurrentManagedThreadId);
        string N() { var pool = "';\"/*\n{}[]:,"; var n = rnd.Next(1, 30); var cs = new char[n]; for (int i = 0; i < n; i++) cs[i] = pool[rnd.Next(pool.Length)]; return new string(cs); }
        for (int i = 0; i < 500; i++)
        {
            var s = N();
            try { SqlSplitter.Split(s); } catch { Interlocked.Increment(ref errs); }
            try { CsvImporter.Parse(s); } catch { Interlocked.Increment(ref errs); }
            try { TnsNames.Parse(s); } catch { Interlocked.Increment(ref errs); }
        }
    });
    Check("8 线程并发解析零异常", errs == 0, $"异常 {errs} 次");
}
{
    // 模糊主题文件 2000 次：Load 永不抛、恒返回合法主题名
    var dir = Path.Combine(Path.GetTempPath(), "taodbm-themefuzz-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    ThemeStore.UseFile(Path.Combine(dir, "theme.json"));
    var rnd = new Random(77);
    int bad = 0;
    for (int i = 0; i < 2000; i++)
    {
        var bytes = new byte[rnd.Next(0, 40)];
        rnd.NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(dir, "theme.json"), bytes);
        try
        {
            var v = ThemeStore.Load();
            if (v != "ink" && v != "gray" && v != "night") bad++;
        }
        catch { bad++; }
    }
    Check("2000 条模糊主题文件零异常", bad == 0, $"异常 {bad} 次");
    ThemeStore.UseFile("");   // 恢复默认路径，避免影响后续
    Directory.Delete(dir, true);
}


// ================= 拟真数据（v1.4.0 全测；v1.4.2 多表化） =================
Console.WriteLine("== Faker 语义推断 ==");
{
    var cases = new (string name, string type, SemanticKind kind)[]
    {
        ("id", "BIGINT", SemanticKind.Id), ("user_id", "INT", SemanticKind.Id),
        ("order_guid", "CHAR(36)", SemanticKind.Guid), ("order_no", "VARCHAR(32)", SemanticKind.OrderNo),
        ("user_name", "VARCHAR(50)", SemanticKind.UserName), ("nickname", "VARCHAR(50)", SemanticKind.NickName),
        ("real_name", "VARCHAR(20)", SemanticKind.PersonName), ("email", "VARCHAR(64)", SemanticKind.Email),
        ("mobile_phone", "VARCHAR(20)", SemanticKind.Phone), ("id_card_no", "VARCHAR(18)", SemanticKind.IdCard),
        ("passwd", "VARCHAR(64)", SemanticKind.PasswordHash), ("access_token", "VARCHAR(128)", SemanticKind.Token),
        ("avatar_url", "VARCHAR(255)", SemanticKind.ImageUrl), ("home_page", "VARCHAR(255)", SemanticKind.Url),
        ("client_ip", "VARCHAR(15)", SemanticKind.Ip), ("listen_port", "INT", SemanticKind.Port),
        ("age", "INT", SemanticKind.Age), ("gender", "ENUM('男','女')", SemanticKind.Gender),
        ("total_amount", "DECIMAL(10,2)", SemanticKind.Money), ("unit_price", "DECIMAL(8,2)", SemanticKind.Price),
        ("stock_num", "INT", SemanticKind.Quantity), ("pay_rate", "DECIMAL(5,2)", SemanticKind.Percent),
        ("order_status", "TINYINT", SemanticKind.Status), ("member_type", "VARCHAR(20)", SemanticKind.Category),
        ("tags", "VARCHAR(100)", SemanticKind.Tag), ("article_title", "VARCHAR(120)", SemanticKind.Title),
        ("content", "TEXT", SemanticKind.ContentText), ("remark", "VARCHAR(255)", SemanticKind.Remark),
        ("address_detail", "VARCHAR(200)", SemanticKind.Address), ("city_name", "VARCHAR(30)", SemanticKind.City),
        ("province_name", "VARCHAR(30)", SemanticKind.Province), ("company_name", "VARCHAR(80)", SemanticKind.CompanyName),
        ("birthday", "DATE", SemanticKind.Birthday), ("created_at", "DATETIME", SemanticKind.DateTime),
        ("deleted", "TINYINT(1)", SemanticKind.Boolean), ("is_enabled", "BIT", SemanticKind.Boolean),
        ("color_hex", "CHAR(7)", SemanticKind.ColorCode), ("job_title", "VARCHAR(40)", SemanticKind.Job),
        ("education_level", "VARCHAR(20)", SemanticKind.Education), ("version_code", "VARCHAR(20)", SemanticKind.Version),
        ("lang", "VARCHAR(10)", SemanticKind.Language), ("tag_json", "JSON", SemanticKind.Json),
    };
    int wrong = 0;
    foreach (var (nm, ty, k) in cases)
        if (Faker.InferSemantic(nm, ty) != k) { wrong++; Console.WriteLine($"    · {nm}({ty}) → {Faker.InferSemantic(nm, ty)} 期望 {k}"); }
    Check($"语义推断 {cases.Length} 例全中", wrong == 0, $"错 {wrong} 例");
}
{
    Check("长度解析", Faker.ParseLen("VARCHAR(50)") == 50 && Faker.ParseLen("CHAR(36)") == 36 && Faker.ParseLen("TEXT") == 0);
    Check("精度解析", Faker.ParsePrecision("DECIMAL(10,2)") == (10, 2) && Faker.ParsePrecision("INT") == (0, 0));
    Check("枚举解析", string.Join("|", Faker.ParseEnumValues("ENUM('男','女','未知')")) == "男|女|未知");
    Check("类型判断", Faker.IsNumericType("BIGINT") && Faker.IsDateType("DATETIME") && Faker.IsTextType("NVARCHAR(50)") &&
        Faker.IsUuidType("UUID") && Faker.IsEnumType("SET('a','b')") && Faker.IsTinyInt("BIT"));
}
{
    // 各槽位生成值类型合法 + 确定性
    var col = new ColumnCtx { Name = "email", RawType = "VARCHAR(64)", Nullable = false, MaxLen = 64 };
    var r1 = new Random(42);
    var v1 = Faker.Gen(Faker.InferSemantic("email", "VARCHAR(64)"), col, r1, 1);
    var r2 = new Random(42);
    var v2 = Faker.Gen(Faker.InferSemantic("email", "VARCHAR(64)"), col, r2, 1);
    Check("同 seed 同值", v1 as string == v2 as string, $"{v1}|{v2}");
    var vMail = v1 as string ?? "";
    Check("邮箱合法", vMail.Contains('@') && vMail.Contains('.') && vMail.Length <= 64, vMail);

    var moneyCol = new ColumnCtx { Name = "amount", RawType = "DECIMAL(10,2)", Nullable = false, Scale = 2 };
    var mv = Faker.Gen(SemanticKind.Money, moneyCol, new Random(7), 1);
    Check("金额两位小数", mv is decimal d && decimal.Round(d, 2) == d, mv?.ToString());
    var dtCol = new ColumnCtx { Name = "created_at", RawType = "DATETIME", Nullable = false };
    var dv = Faker.Gen(SemanticKind.DateTime, dtCol, new Random(9), 1) as string ?? "";
    Check("日期格式", System.Text.RegularExpressions.Regex.IsMatch(dv, @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$"), dv);
    var bCol = new ColumnCtx { Name = "is_enabled", RawType = "TINYINT(1)", Nullable = false };
    var bv = Faker.Gen(SemanticKind.Boolean, bCol, new Random(3), 1);
    Check("布尔 0/1", bv is int i && i is 0 or 1, bv?.ToString());
    var eCol = new ColumnCtx { Name = "gender", RawType = "ENUM('男','女')", Nullable = false, EnumValues = Faker.ParseEnumValues("ENUM('男','女')") };
    var ev = Faker.Gen(SemanticKind.Gender, eCol, new Random(5), 1) as string ?? "";
    Check("枚举取值合法", ev is "男" or "女", ev);
    var phoneCol = new ColumnCtx { Name = "phone", RawType = "VARCHAR(11)", Nullable = false, MaxLen = 11 };
    var pv = Faker.Gen(SemanticKind.Phone, phoneCol, new Random(11), 1) as string ?? "";
    Check("手机号 11 位", System.Text.RegularExpressions.Regex.IsMatch(pv, @"^1[3-9]\d{9}$"), pv);
    var nameCol = new ColumnCtx { Name = "real_name", RawType = "VARCHAR(20)", Nullable = false, MaxLen = 20 };
    var nv = Faker.Gen(SemanticKind.PersonName, nameCol, new Random(13), 1) as string ?? "";
    Check("中文姓名", nv.Length >= 2 && nv.Length <= 20, nv);
    var nullableCol = new ColumnCtx { Name = "remark", RawType = "VARCHAR(255)", Nullable = true };
    var rndN = new Random(21);
    bool sawNull = false;
    for (long row = 1; row <= 14; row++)
        if (Faker.Gen(SemanticKind.Remark, nullableCol, rndN, row) is null) { sawNull = true; break; }
    Check("可空列稀疏 NULL", sawNull);
    var notNullCol = new ColumnCtx { Name = "remark", RawType = "VARCHAR(255)", Nullable = false };
    rndN = new Random(21);
    bool anyNull = Enumerable.Range(1, 14).Any(row => Faker.Gen(SemanticKind.Remark, notNullCol, rndN, row) is null);
    Check("非空列不出 NULL", !anyNull);
    var poolCol = new ColumnCtx { Name = "user_id", RawType = "BIGINT", Nullable = false, IsPrimaryKey = false };
    var pv2 = Faker.Gen(SemanticKind.Id, poolCol, new Random(31), 1, poolValue: "88");
    Check("关联列取池值", pv2 as string == "88", pv2?.ToString());
}
{
    // 万行零异常 + 断言规模（暴力）
    var cols = new List<ColumnSpec>
    {
        new() { Name = "id", DataType = "BIGINT", IsKey = true },
        new() { Name = "real_name", DataType = "VARCHAR(20)" },
        new() { Name = "email", DataType = "VARCHAR(64)" },
        new() { Name = "mobile", DataType = "VARCHAR(11)" },
        new() { Name = "total_amount", DataType = "DECIMAL(10,2)" },
        new() { Name = "created_at", DataType = "DATETIME" },
        new() { Name = "is_enabled", DataType = "TINYINT(1)" },
        new() { Name = "remark", DataType = "VARCHAR(255)" },
        new() { Name = "city_name", DataType = "VARCHAR(30)" },
        new() { Name = "gender", DataType = "ENUM('男','女')" },
    };
    var table = new SeedTable { Name = "orders", Columns = cols, Rows = 10000 };
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var rows = SeedPlan.GenerateRows(DbEngine.MySQL, table, new List<SeedEdge>(), new(), 2026);
    sw.Stop();
    Check("万行生成零异常", rows.Count == 10000 && rows.All(r => r.Count == cols.Count), $"{rows.Count} 行 {sw.ElapsedMilliseconds} ms");
    var ids = rows.Select(r => r[0]).Cast<long>().ToList();
    Check("PK 唯一递增", ids.Count == 10000 && ids.Distinct().Count() == 10000 && ids[0] == 1 && ids[9999] == 10000);
    var emails = rows.Select(r => r[2] as string).Where(s => s is not null).ToList();
    Check("万行邮箱全合法", emails.Count > 0 && emails.All(e => e.Contains('@') && e.Length <= 64));
    Check("枚举列全合法", rows.All(r => r[9] == null || r[9]!.ToString() is "男" or "女"));
    Check("确定性重放", SeedPlan.GenerateRows(DbEngine.MySQL, table, new(), new(), 2026)
        .Zip(rows, (a, b) => a.Select((x, i) => (x?.ToString()) == (b[i]?.ToString())).All(y => y)).All(x => x));
    Check("不同 seed 不同值", SeedPlan.GenerateRows(DbEngine.MySQL, table, new(), new(), 2027)[1][2]!.ToString() != rows[1][2]!.ToString());
}

Console.WriteLine("== SeedPlan 关联与脚本 ==");
{
    var users = new SeedTable
    {
        Name = "users",
        Columns = new() { new ColumnSpec { Name = "id", DataType = "BIGINT", IsKey = true }, new ColumnSpec { Name = "real_name", DataType = "VARCHAR(20)" } },
    };
    var orders = new SeedTable
    {
        Name = "orders",
        Columns = new()
        {
            new ColumnSpec { Name = "id", DataType = "BIGINT", IsKey = true },
            new ColumnSpec { Name = "user_id", DataType = "BIGINT" },
            new ColumnSpec { Name = "total_amount", DataType = "DECIMAL(10,2)" },
        },
    };
    var items = new SeedTable
    {
        Name = "order_items",
        Columns = new()
        {
            new ColumnSpec { Name = "id", DataType = "BIGINT", IsKey = true },
            new ColumnSpec { Name = "order_id", DataType = "BIGINT" },
            new ColumnSpec { Name = "product_id", DataType = "BIGINT" },
        },
    };
    var tables = new List<SeedTable> { items, orders, users };
    var edges = SeedPlan.BuildRelations(tables, null);
    Check("字段名关联", edges.Count == 2 && edges.Any(e => e.FromTable == "orders" && e.FromCol == "user_id" && e.ToTable == "users") &&
        edges.Any(e => e.FromTable == "order_items" && e.FromCol == "order_id" && e.ToTable == "orders"),
        string.Join(";", edges.Select(e => $"{e.FromTable}.{e.FromCol}->{e.ToTable}")));
    Check("自引用跳过", !SeedPlan.BuildRelations(new List<SeedTable> { users }, null)
        .Any(e => e.FromTable == "users" && e.ToTable == "users"));
    var order = SeedPlan.TopoOrder(tables, edges);
    Check("拓扑：被引用表先", order[0].Name == "users" && order[1].Name == "orders" && order[2].Name == "order_items",
        string.Join("→", order.Select(t => t.Name)));
    Check("环不炸", SeedPlan.TopoOrder(tables, new List<SeedEdge> { new() { FromTable = "users", FromCol = "id", ToTable = "orders", ToCol = "id" }, new() { FromTable = "orders", FromCol = "id", ToTable = "users", ToCol = "id" } }).Count == 3);

    // == 多表生成计划（v1.4.2：每表独立行数，拓扑序执行） ==
    {
        var plan = SeedPlan.PlanFromCounts(new Dictionary<string, int> { ["users"] = 100, ["orders"] = 0, ["order_items"] = 20 },
            tables, edges);
        Check("多表计划：零行剔除、拓扑先行",
            plan.Count == 2 && plan[0].Table.Name == "users" && plan[1].Table.Name == "order_items",
            string.Join("→", plan.Select(p => $"{p.Table.Name}×{p.Count}")));
        Check("多表计划：行数逐表独立", plan[0].Count == 100 && plan[1].Count == 20);
        var all = SeedPlan.PlanFromCounts(new Dictionary<string, int> { ["users"] = 10, ["orders"] = 5, ["order_items"] = 3 },
            tables, edges);
        Check("多表计划：全表拓扑序", all.Select(p => p.Table.Name).SequenceEqual(new[] { "users", "orders", "order_items" }),
            string.Join(",", all.Select(p => p.Table.Name)));
        Check("多表计划：无输入为空", SeedPlan.PlanFromCounts(new Dictionary<string, int>(), tables, edges).Count == 0);
        // 多表生成确定性：同库同表同参必同值（多表计划复用单表生成器）
        var poolUsers = new Dictionary<string, Dictionary<string, List<object?>>> { ["users"] = new() { ["id"] = new List<object?> { 1, 2, 3 } } };
        var g1 = SeedPlan.GenerateRows(DbEngine.MySQL, all[1].Table, edges, poolUsers, 2026);
        var g2 = SeedPlan.GenerateRows(DbEngine.MySQL, all[1].Table, edges, poolUsers, 2026);
        Check("多表生成确定性", g1.Count == 5 && g1.Zip(g2, (a, b) => a.SequenceEqual(b)).All(x => x),
            g1.Count.ToString());
        // 引用列取值来自池（多表模式：被引用表先写入，子表引用列采样其值）
        Check("多表引用值来自池", g1.All(r => new object?[] { 1, 2, 3 }.Contains(r[1])), string.Join(",", g1.Select(r => r[1])));
    }

    // 生成：orders.user_id 从 users 值池采样
    var userRows = SeedPlan.GenerateRows(DbEngine.MySQL, users, edges, new(), 100);
    var pools = new Dictionary<string, Dictionary<string, List<object?>>> { ["users"] = SeedPlan.PoolFrom(DbEngine.MySQL, users, userRows) };
    var orderRows = SeedPlan.GenerateRows(DbEngine.MySQL, orders, edges, pools, 200);
    var userIds = orderRows.Select(r => r[1]).Cast<object>().Where(v => v is not null).Select(v => Convert.ToInt64(v)).ToList();
    Check("关联值来自池", userIds.Count > 0 && userIds.All(v => v >= 1 && v <= 100), string.Join(",", userIds.Take(5)));
}
{
    var cols = new List<ColumnSpec>
    {
        new() { Name = "id", DataType = "INT", IsKey = true },
        new() { Name = "name", DataType = "VARCHAR(20)" },
        new() { Name = "note", DataType = "VARCHAR(50)" },
    };
    var rows = new List<List<object?>>
    {
        new() { 1, "张三", "他说:'你好'" },
        new() { 2, null, null },
    };
    var my = SeedPlan.BuildInsertScripts(DbEngine.MySQL, "t_users", cols, rows);
    Check("MySQL 方言", my.Count == 1 && my[0].Contains("INSERT INTO `t_users` (`id`, `name`, `note`) VALUES") &&
        my[0].Contains("'张三'") && my[0].Contains("'他说:''你好'''") && my[0].Contains("NULL"), my[0]);
    var pg = SeedPlan.BuildInsertScripts(DbEngine.PostgreSQL, "t users", cols, rows);
    Check("PG 双引号", pg[0].Contains("INSERT INTO \"t users\" (\"id\", \"name\", \"note\")"), pg[0]);
    var ss = SeedPlan.BuildInsertScripts(DbEngine.SqlServer, "t", cols, rows);
    Check("SQL Server N 前缀", ss[0].Contains("N'张三'"), ss[0]);
    var lite = SeedPlan.BuildInsertScripts(DbEngine.SQLite, "t", cols, rows);
    Check("SQLite 方言", lite[0].Contains("INSERT INTO \"t\" (\"id\", \"name\", \"note\")"), lite[0]);
    Check("Oracle 裸值", SeedPlan.BuildInsertScripts(DbEngine.Oracle, "T", cols, rows)[0].Contains("'张三'"));
    // 批 500
    var big = new List<List<object?>>();
    for (int i = 0; i < 1201; i++) big.Add(new List<object?> { i, "n", null });
    var scripts = SeedPlan.BuildInsertScripts(DbEngine.MySQL, "t", cols, big);
    Check("分批 500", scripts.Count == 3, scripts.Count.ToString());
    Check("批首 500 行", System.Text.RegularExpressions.Regex.Matches(scripts[0], "\n\\(").Count == 500);
    Check("空列/空行不产出", SeedPlan.BuildInsertScripts(DbEngine.MySQL, "t", new(), rows).Count == 0 &&
        SeedPlan.BuildInsertScripts(DbEngine.MySQL, "t", cols, new()).Count == 0);
}
{
    Check("分页方言", SeedPlan.SelectTopSql(DbEngine.SqlServer, "t", "id", 100).Contains("TOP (100)") &&
        SeedPlan.SelectTopSql(DbEngine.Oracle, "t", "id", 100).Contains("ROWNUM <= 100") &&
        SeedPlan.SelectTopSql(DbEngine.MySQL, "t", "id", 100).EndsWith("LIMIT 100"));
    Check("外键发现 SQL", SeedPlan.ForeignKeySql(DbEngine.MySQL, "shop", "orders")!.Contains("KEY_COLUMN_USAGE") &&
        SeedPlan.ForeignKeySql(DbEngine.SqlServer, "shop", "orders")!.Contains("sys.foreign_keys") &&
        SeedPlan.ForeignKeySql(DbEngine.Oracle, "shop", "orders")!.Contains("all_constraints") &&
        SeedPlan.ForeignKeySql(DbEngine.MongoDB, "shop", "orders") is null);
    Check("SQLite PRAGMA", SeedPlan.SqliteForeignKeySql("orders").Contains("foreign_key_list"));
    Check("引号转义", SeedPlan.QuoteIdent(DbEngine.MySQL, "we`ird") == "`we``ird`" &&
        SeedPlan.QuoteIdent(DbEngine.PostgreSQL, "a\"b") == "\"a\"\"b\"" &&
        SeedPlan.QuoteIdent(DbEngine.SqlServer, "a]b") == "[a]]b]");
}


Layer("万链");
{
    // 万链归账（万级补则）：万链并行走核心纯函数域，链内不写输出，Interlocked 归账终局断言。
    // 四个领域：SQL 分词区间不变量 / SqlSplitter 分段非空 / CSV 导出解析往返 / ER 模型 JSON 往返。
    const int ChainCount = 10000, StepsPerChain = 30;
    long totalAsserts = 0, violations = 0;
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var valuePool = new[] { "1", "42", "abc", "张三", "x y", "3.5", "true", "2026-01-01", "", "O'Brien" };
    var tokPool = new[] { "SELECT", "id", "FROM", "`t`", "'a;b'", "--c\n", "/*x*/", ";", "1.5e-3", "中", "\"i\"", ",", "(", ")", "WHERE", "x=", "0x1F" };
    Parallel.For(0, ChainCount, chain =>
    {
        var rnd = new Random(20261101 + chain);   // 每链独立种子：确定性不因并行改变，失败可复现
        long a = 0, v = 0;
        for (int step = 0; step < StepsPerChain; step++)
        {
            switch (rnd.Next(4))
            {
                case 0:   // SQL 分词：区间界内、逐段不越原文
                {
                    var sb = new StringBuilder();
                    int n = rnd.Next(0, 30);
                    for (int i = 0; i < n; i++) sb.Append(tokPool[rnd.Next(tokPool.Length)]);
                    var sql = sb.ToString();
                    try
                    {
                        var toks = SqlHighlighter.Tokenize(sql);
                        int lastEnd = 0;
                        foreach (var t in toks)
                        {
                            if (t.Start < lastEnd || t.Start < 0 || t.Length <= 0 || t.Start + t.Length > sql.Length) v++;
                            a++;
                            lastEnd = Math.Max(lastEnd, t.Start + t.Length);
                        }
                    }
                    catch { v++; a++; }
                    break;
                }
                case 1:   // SqlSplitter：每段非空、永不抛
                {
                    var sb = new StringBuilder();
                    int n = rnd.Next(1, 8);
                    for (int i = 0; i < n; i++) { sb.Append("SELECT ").Append(rnd.Next(1000)); if (i < n - 1) sb.Append(';'); }
                    try
                    {
                        var parts = SqlSplitter.Split(sb.ToString());
                        if (parts.Any(p => string.IsNullOrWhiteSpace(p))) v++;
                        a++;
                    }
                    catch { v++; a++; }
                    break;
                }
                case 2:   // CSV 往返：导出→解析，行列一致、值还原（空串归 null 口径）
                {
                    var r = new QueryResult { Columns = { "c1", "c2", "c3" } };
                    var vals = new object?[3];
                    for (int i = 0; i < 3; i++)
                        vals[i] = rnd.Next(10) == 0 ? DBNull.Value : valuePool[rnd.Next(valuePool.Length)];
                    r.Rows.Add(vals);
                    try
                    {
                        var (heads, rows) = CsvImporter.Parse(Exporter.Csv(r));
                        bool ok = heads.SequenceEqual(r.Columns) && rows.Count == 1;
                        if (ok)
                            for (int i = 0; i < 3; i++)
                            {
                                // CSV 口径：DBNull/null/空串 输出为空单元格，还原一律为 null
                                bool eq = vals[i] is DBNull or null or ""
                                    ? rows[0][i] is null
                                    : rows[0][i] == (string?)vals[i];
                                if (!eq) { ok = false; break; }
                            }
                        if (!ok) v++;
                        a++;
                    }
                    catch { v++; a++; }
                    break;
                }
                case 3:   // ER 模型：JSON 往返表列全等
                {
                    var m = new ErdModel();
                    int tableN = rnd.Next(1, 5);
                    for (int ti = 0; ti < tableN; ti++)
                    {
                        var tab = new ErdTable { Name = $"t{ti}_{rnd.Next(100)}" };
                        int colN = rnd.Next(1, 6);
                        for (int ci = 0; ci < colN; ci++)
                            tab.Columns.Add(new ErdColumn { Name = $"c{ci}", Type = rnd.Next(2) == 0 ? "int" : "varchar(50)", IsKey = ci == 0 });
                        m.Tables.Add(tab);
                    }
                    try
                    {
                        var back = ErdModel.FromJson(m.ToJson());
                        if (back is null || back.Tables.Count != tableN
                            || !back.Tables.Zip(m.Tables, (b, o) => b.Name == o.Name && b.Columns.Count == o.Columns.Count).All(x => x)) v++;
                        a++;
                    }
                    catch { v++; a++; }
                    break;
                }
            }
        }
        Interlocked.Add(ref totalAsserts, a);
        Interlocked.Add(ref violations, v);
    });
    sw.Stop();
    Check($"万链归账：{ChainCount} 链 × {StepsPerChain} 步零违规",
        violations == 0 && totalAsserts >= ChainCount,
        $"断言 {totalAsserts} 条，违规 {violations}，耗时 {sw.Elapsed.TotalSeconds:0.0}s");
    Console.WriteLine($"  （万链归账：{ChainCount} 链 / {ChainCount * StepsPerChain} 步 / 断言 {totalAsserts} 条 / 违规 {violations} / 耗时 {sw.Elapsed.TotalSeconds:0.0}s）");
}

Console.WriteLine("\n== 分层结果 ==");
foreach (var name in new[] { "单元", "集成", "系统", "暴力", "万链" })
    Console.WriteLine($"{name}: {layerPass.GetValueOrDefault(name)} / {layerPass.GetValueOrDefault(name) + layerFail.GetValueOrDefault(name)}");
Console.WriteLine($"\n通过 {pass} / {pass + fail}");
return fail == 0 ? 0 : 1;

// 测试用最小 Provider：复用 RelationalProvider 的 Quote 逻辑
class TestProv : TaoDBM.Core.RelationalProvider
{
    private readonly char _q;
    public TestProv(char q = '`') : base(new TaoDBM.Core.ConnectionProfile()) => _q = q;
    protected override System.Data.Common.DbConnection Open(string? db) => throw new NotSupportedException();
    protected override char OpenQuote => _q;
    protected override char CloseQuote => _q;
    public override bool SupportsBrowse => true;
    protected override string ListDatabasesSql() => "";
    protected override string ListChildrenSql(string db) => "";
    protected override string ListColumnsSql(string db, string table) => "";
    protected override string PagingClause(int offset, int limit) => "";
}
