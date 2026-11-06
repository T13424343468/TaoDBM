// 真连数据库层：环境变量启用（DBM_REALDB_MYSQL / _PG / _MSSQL / _MONGO / _REDIS），逐库执行
// 连接→库列表→建表→插入→查询逐字段核对（含中文与 Unicode）→改→删→列结构→表浏览→清理。
// Oracle 无轻量容器，列为补测范围，不在本层阻塞。
using MongoDB.Bson;
using MongoDB.Driver;
using StackExchange.Redis;
using System.Globalization;
using TaoDBM.Core;

public static class RealDbCheck
{
    public static void Run(Action<string, bool, string> check)
    {
        var items = new (string env, Action<Action<string, bool, string>> run)[]
        {
            ("DBM_REALDB_MYSQL", c => Relational(c, DbEngine.MySQL, "VARCHAR(50)", false, "MYSQL")),
            ("DBM_REALDB_PG", c => Relational(c, DbEngine.PostgreSQL, "VARCHAR(50)", false, "PG")),
            ("DBM_REALDB_MSSQL", c => Relational(c, DbEngine.SqlServer, "NVARCHAR(50)", true, "MSSQL")),
            ("DBM_REALDB_MONGO", c => Mongo(c)),
            ("DBM_REALDB_REDIS", c => Redis(c)),
        };
        foreach (var (env, run) in items)
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(env))) run(check);
    }

    static string Root(Exception ex)
    {
        var cur = ex; while (cur.InnerException != null) cur = cur.InnerException;
        return cur.Message;
    }

    static ConnectionProfile ProfileOf(DbEngine engine, string spec)
    {
        var p = spec.Split('|');
        return new ConnectionProfile
        {
            Engine = engine,
            Host = p[0],
            Port = int.Parse(p[1]),
            User = p.Length > 2 ? p[2] : "",
            Password = p.Length > 3 ? p[3] : "",
            Database = p.Length > 4 ? p[4] : "",
        };
    }

    static void WithRetry(Action act, int times = 6)
    {
        for (int i = 1; ; i++)
        {
            try { act(); return; }
            catch (Exception) when (i < times) { Thread.Sleep(8000); }
        }
    }

    static void Relational(Action<string, bool, string> check, DbEngine engine, string textType, bool nPrefix, string tag)
    {
        try
        {
            var p = ProfileOf(engine, Environment.GetEnvironmentVariable("DBM_REALDB_" + tag)!);
            using var prov = ProviderFactory.Create(p);
            string db = p.Database;
            WithRetry(() => prov.TestConnection().Wait());
            check($"[{tag}] 真连：连接测试", true, "");
            var dbs = prov.GetDatabases().Result;
            check($"[{tag}] 真连：库列表可读", dbs.Count > 0, $"{dbs.Count} 项");
            check($"[{tag}] 真连：目标库在列表", dbs.Contains(db), string.Join(",", dbs.Take(5)));

            string q(string s) => prov.Quote(s);
            string lit(string s) => (nPrefix ? "N'" : "'") + s + "'";
            prov.Execute(db, $"DROP TABLE IF EXISTS {q("t_zy_real")}").Wait();
            prov.Execute(db, $"CREATE TABLE {q("t_zy_real")} (id INT PRIMARY KEY, name {textType}, val DECIMAL(10,2))").Wait();
            check($"[{tag}] 真连：建表成功", true, "");

            prov.Execute(db, $"INSERT INTO {q("t_zy_real")} (id, name, val) VALUES (1, {lit("道")}, 3.14), (2, {lit("α")}, 2.71), (3, {lit("tai")}, 1.61)").Wait();
            var r0 = prov.Execute(db, $"SELECT id, name, val FROM {q("t_zy_real")} ORDER BY id").Result.Last();
            static decimal Dec(object? v) => decimal.Parse(Convert.ToString(v)!.Trim(), CultureInfo.InvariantCulture);
            bool okIns = r0.Rows.Count == 3
                && Convert.ToInt32(r0.Rows[0][0]) == 1 && Convert.ToInt32(r0.Rows[1][0]) == 2 && Convert.ToInt32(r0.Rows[2][0]) == 3
                && Convert.ToString(r0.Rows[0][1]) == "道" && Convert.ToString(r0.Rows[1][1]) == "α" && Convert.ToString(r0.Rows[2][1]) == "tai"
                && Dec(r0.Rows[0][2]) == 3.14m && Dec(r0.Rows[1][2]) == 2.71m && Dec(r0.Rows[2][2]) == 1.61m;
            check($"[{tag}] 数据对比：写入 3 行逐字段读回核对（含中文与 Unicode）", okIns, $"{r0.Rows.Count} 行");

            prov.Execute(db, $"UPDATE {q("t_zy_real")} SET val = 6.28 WHERE id = 2").Wait();
            var r1 = prov.Execute(db, $"SELECT val FROM {q("t_zy_real")} WHERE id = 2").Result.Last();
            check($"[{tag}] 数据对比：改后读回", Dec(r1.Rows[0][0]) == 6.28m, "");

            prov.Execute(db, $"DELETE FROM {q("t_zy_real")} WHERE id = 3").Wait();
            var r2 = prov.Execute(db, $"SELECT id FROM {q("t_zy_real")}").Result.Last();
            check($"[{tag}] 数据对比：删后行数", r2.Rows.Count == 2, $"{r2.Rows.Count} 行");

            var cols = prov.GetColumns(db, "t_zy_real").Result;
            check($"[{tag}] 真连：列结构读取", cols.Any(c => c.Name == "name") && cols.Count >= 3, string.Join(",", cols.Select(c => c.Name)));

            var br = prov.Browse(db, "t_zy_real", 0, 10).Result;
            check($"[{tag}] 真连：表浏览", (br.TotalRows ?? br.Rows.Count) == 2, $"total={br.TotalRows} rows={br.Rows.Count}");

            prov.Execute(db, $"DROP TABLE {q("t_zy_real")}").Wait();
            check($"[{tag}] 真连：全流程清理完成", true, "");
        }
        catch (Exception ex) { check($"[{tag}] 真连全流程", false, Root(ex)); }
    }

    static void Mongo(Action<string, bool, string> check)
    {
        try
        {
            var p = ProfileOf(DbEngine.MongoDB, Environment.GetEnvironmentVariable("DBM_REALDB_MONGO")!);
            var prov = ProviderFactory.Create(p);
            using var _ = prov;
            var mongo = (MongoProvider)prov;
            WithRetry(() => mongo.TestConnection().Wait());
            check("[MONGO] 真连：连接测试", true, "");
            check("[MONGO] 真连：库列表可读", mongo.GetDatabases().Result.Count >= 0, "");
            var docs = new List<BsonDocument>
            {
                new BsonDocument { { "_id", "k1" }, { "val", 3.14 }, { "tag", "道" } },
                new BsonDocument { { "_id", "k2" }, { "val", 2.71 }, { "tag", "α" } },
            };
            long n = mongo.InsertManyAsync("taodbm_ci", "t_zy_real", docs).Result;
            check("[MONGO] 真连：写入 2 文档", n == 2, $"{n}");
            check("[MONGO] 真连：集合出现在子树", mongo.GetChildren("taodbm_ci").Result.Contains("t_zy_real"), "");
            var br = mongo.Browse("taodbm_ci", "t_zy_real", 0, 10).Result;
            int idCol = br.Columns.IndexOf("_id"), docCol = br.Columns.IndexOf("(document)");
            var byId = br.Rows.ToDictionary(r => Convert.ToString(r[idCol])!, r => Convert.ToString(r[docCol])!);
            var doc1 = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(byId["k1"]);
            var doc2 = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<BsonDocument>(byId["k2"]);
            bool ok = br.TotalRows == 2 && br.Rows.Count == 2
                && doc1["val"].ToDouble() == 3.14 && doc1["tag"].AsString == "道"
                && doc2["val"].ToDouble() == 2.71 && doc2["tag"].AsString == "α";
            check("[MONGO] 数据对比：读回逐字段核对", ok, $"total={br.TotalRows} rows={br.Rows.Count}");
            new MongoClient($"mongodb://{p.Host}:{p.Port}").GetDatabase("taodbm_ci").GetCollection<BsonDocument>("t_zy_real").DeleteMany(new BsonDocument());
            check("[MONGO] 真连：清理完成", true, "");
        }
        catch (Exception ex) { check("[MONGO] 真连全流程", false, Root(ex)); }
    }

    static void Redis(Action<string, bool, string> check)
    {
        try
        {
            var p = ProfileOf(DbEngine.Redis, Environment.GetEnvironmentVariable("DBM_REALDB_REDIS")!);
            var prov = new RedisProvider(p);
            using var _ = prov;
            WithRetry(() => prov.TestConnection().Wait());
            check("[REDIS] 真连：连接测试", true, "");
            check("[REDIS] 真连：库列表可读", prov.GetDatabases().Result.Count > 0, "");
            string key = "t_zy:real:道";
            var direct = ConnectionMultiplexer.Connect($"{p.Host}:{p.Port}").GetDatabase();
            direct.StringSet(key, "3.14");
            direct.StringSet(key + ":n", "道α");
            var got = direct.StringGet(key);
            check("[REDIS] 数据对比：真实写入读回", got == "3.14", $"{got}");
            var g = prov.Execute("0", $"GET {key}").Result.Last();
            string all = string.Join("|", g.Rows.Select(r => string.Join(",", r.Select(v => Convert.ToString(v))))) + g.Message + g.Title;
            check("[REDIS] 数据对比：产品链路 GET 读回", all.Contains("3.14"), $"[{all}]");
            var g2 = prov.Execute("0", $"GET {key}:n").Result.Last();
            string all2 = string.Join("|", g2.Rows.Select(r => string.Join(",", r.Select(v => Convert.ToString(v))))) + g2.Message + g2.Title;
            check("[REDIS] 数据对比：Unicode 键值读回", all2.Contains("道α"), $"[{all2}]");
            direct.KeyDelete(key); direct.KeyDelete(key + ":n");
            check("[REDIS] 真连：清理完成", true, "");
        }
        catch (Exception ex) { check("[REDIS] 真连全流程", false, Root(ex)); }
    }
}
