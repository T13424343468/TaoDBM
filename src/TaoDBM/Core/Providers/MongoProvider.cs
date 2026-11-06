using MongoDB.Bson;
using MongoDB.Driver;

namespace TaoDBM.Core;

/// <summary>MongoDB：库 = database，子节点 = collection。</summary>
public class MongoProvider : IDbProvider
{
    public ConnectionProfile Profile { get; }
    public string EngineName => EngineInfo.Name(Profile.Engine);
    public bool IsNoSql => true;
    public bool SupportsBrowse => true;
    public string BrowseLabel => "集合";

    public MongoProvider(ConnectionProfile p) => Profile = p;

    public string Quote(string ident) => ident; // Mongo 无 SQL 标识符引用

    /// <summary>拟真数据专用：向集合批量插入文档（find 命令不覆盖写路径）。</summary>
    public Task<long> InsertManyAsync(string db, string collection, List<BsonDocument> docs) => Task.Run(async () =>
    {
        if (docs.Count == 0) return 0L;
        await DB(db).GetCollection<BsonDocument>(collection).InsertManyAsync(docs);
        return docs.Count;
    });


    private IMongoDatabase DB(string? db)
    {
        var client = BuildClient();
        return client.GetDatabase(string.IsNullOrEmpty(db) ? "test" : db);
    }

    private MongoClient BuildClient()
    {
        var (host, port) = SshTunnel.Resolve(Profile, Profile.Host, Profile.Port > 0 ? Profile.Port : 27017);
        var url = $"mongodb://{Uri.EscapeDataString(Profile.User)}:{Uri.EscapeDataString(Profile.Password)}@{host}:{port}";
        if (string.IsNullOrEmpty(Profile.User)) url = $"mongodb://{host}:{port}";
        return new MongoClient(MongoUrl.Create(url + "/?serverSelectionTimeoutMS=8000&connectTimeoutMS=8000"));
    }

    public Task TestConnection() => Task.Run(() => BuildClient().ListDatabaseNames());

    public Task<List<string>> GetDatabases() => Task.Run(async () =>
    {
        var client = BuildClient();
        var names = await (await client.ListDatabaseNamesAsync()).ToListAsync();
        return names;
    });


    public Task<List<string>> GetChildren(string db) => Task.Run(async () =>
    {
        var list = await (await DB(db).ListCollectionNamesAsync()).ToListAsync();
        return list;
    });


    public Task<List<ColumnSpec>> GetColumns(string db, string child)
    {
        List<ColumnSpec> cols = new()
        {
            new ColumnSpec { Name = "_id", DataType = "ObjectId", IsKey = true },
            new ColumnSpec { Name = "(document)", DataType = "JSON" },
        };
        return Task.FromResult(cols);
    }

    public Task<List<QueryResult>> Execute(string db, string script) => Task.Run(async () =>
    {
        // 约定：输入为 JSON 过滤器（可为空 {}），对选中集合执行 find
        var col = CurrentCollection ?? throw new InvalidOperationException("请先在左侧树中选择集合，或新建查询时选中集合节点。");
        var filter = BsonDocument.Parse(string.IsNullOrWhiteSpace(script) ? "{}" : script.Trim());
        var qr = new QueryResult { Title = $"{col} · find({script.Trim()})" };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var docs = await DB(db).GetCollection<BsonDocument>(col).Find(filter).Limit(200).ToListAsync();
        qr.Columns.Add("_id");
        qr.Columns.Add("(document)");
        foreach (var d in docs)
        {
            string json = d.ToJson(new() { Indent = false });
            string id = d.TryGetValue("_id", out var idv) ? idv.ToString() ?? "" : "";
            qr.Rows.Add(new object?[] { id, json });
        }
        sw.Stop();
        qr.Elapsed = sw.Elapsed;
        return new List<QueryResult> { qr };
    });


    public Task<QueryResult> Browse(string db, string collection, int offset, int limit) => Task.Run(async () =>
    {
        var col = DB(db).GetCollection<BsonDocument>(collection);
        var qr = new QueryResult { Title = collection };
        qr.TotalRows = await col.CountDocumentsAsync(new BsonDocument());
        var docs = await col.Find(new BsonDocument()).Skip(offset).Limit(limit).ToListAsync();
        qr.Columns.Add("_id");
        qr.Columns.Add("(document)");
        foreach (var d in docs)
        {
            string id = d.TryGetValue("_id", out var idv) ? idv.ToString() ?? "" : "";
            qr.Rows.Add(new object?[] { id, d.ToJson() });
        }
        return qr;
    });


    /// <summary>当前操作的集合（由界面在展开集合/新建查询时设置）。</summary>
    public string? CurrentCollection { get; set; }

    public void Dispose() { }
}
