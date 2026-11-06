# 02 · 架构设计

## 1. 分层总览

```
┌─────────────────────────────────────────────┐
│  UI 层（WinUI 3，代码优先构建）                  │
│  MainWindow（主壳） WorkTab（工作区标签）        │
│  ConnectionDialog（连接表单） GridBuilder（网格） │
├─────────────────────────────────────────────┤
│  接口层                                        │
│  IDbProvider —— 7 种引擎统一抽象                │
├─────────────────────────────────────────────┤
│  驱动层                                        │
│  RelationalProvider（ADO 基类）                 │
│   ├ MySqlProvider   ├ PostgreSqlProvider       │
│   ├ SqlServerProvider ├ SqliteProvider         │
│   └ OracleProvider                             │
│  MongoProvider（官方 Driver）                   │
│  RedisProvider（StackExchange.Redis）          │
├─────────────────────────────────────────────┤
│  基础层                                        │
│  ConnectionProfile / ConnectionStore（DPAPI）  │
│  SshTunnel（SSH.NET 本地转发）                 │
│  CsvImporter（RFC 4180 解析 + 批量 INSERT）    │
│  JsonImporter（对象数组/JSONL 解析，复用 INSERT）│
│  TnsNames（tnsnames.ora 条目解析）             │
│  QueryHistory（%APPDATA% 持久化）              │
│  QueryResult / ColumnSpec / EngineInfo         │
└─────────────────────────────────────────────┘
```

设计原则：**UI 层不感知具体数据库**。UI 只面向 `IDbProvider` 的 8 个成员编程，
新增一种数据库 = 新增一个 Provider 类 + 工厂注册一行，界面零改动。

## 2. 关键决策

### 2.1 统一驱动接口

```csharp
public interface IDbProvider : IDisposable
{
    Task TestConnection();
    Task<List<string>> GetDatabases();              // 库 / db0-15
    Task<List<string>> GetChildren(string db);      // 表 / 集合 / 键
    Task<List<ColumnSpec>> GetColumns(string db, string child);
    Task<List<QueryResult>> Execute(string db, string script);
    Task<QueryResult> Browse(string db, string child, int offset, int limit);
    bool SupportsBrowse { get; }
    bool IsNoSql { get; }
}
```

- 关系型：`Browse` 走 `SELECT *` + 分页方言；非关系型按各自语义实现
- `Quote` 支持逐段引用（PostgreSQL schema.table → "schema"."table"）
- 数据编辑：以 `ColumnSpec.IsKey` 生成受保护的 UPDATE / DELETE（无主键拒绝）
- MongoDB：`GetDatabases`=listDatabases、`GetChildren`=listCollections、
  `Execute`=JSON 过滤器 find
- Redis：`GetDatabases`=db0..db15、`Execute`=命令解析（KEYS/GET/TYPE/TTL/DEL…）

### 2.2 多语句执行

`SqlSplitter.Split` 自研字符级解析，处理：单/双引号、反引号、`''` 转义、
`--` 行注释、`/* */` 块注释；避免正则方案的灾难性回溯。
每条语句独立执行并独立产出 `QueryResult`（结果集 + 受影响行数 + 耗时），
单条失败不中断后续语句。

### 2.3 分页方言

| 引擎 | 分页子句 |
| --- | --- |
| MySQL / SQLite / PostgreSQL | `LIMIT n OFFSET m` |
| SQL Server | `ORDER BY (SELECT NULL) OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| Oracle（12c+） | `OFFSET m ROWS FETCH NEXT n ROWS ONLY` |
| MongoDB | `Find(filter).Skip(m).Limit(n)` |

浏览前先执行 `SELECT COUNT(*)`（Mongo 为 CountDocuments）填 `TotalRows`。

### 2.4 懒加载树（占位子节点模式）

WinUI 的 `TreeViewNode.HasChildren` 为只读，因此用**占位子节点**驱动展开：
根节点与库节点创建时挂一个 `"…"` 占位子节点，`Expanding` 事件里检测到占位即
清空并异步加载真实子节点；失败时节点显示错误文案，点「刷新」重置占位重试。

### 2.5 连接存储

`%APPDATA%\TaoDBM\connections.json`，密码用 DPAPI（LocalMachine 作用域）
加密后存储，前缀 `dpapi:` 标记；反序列化时自动解密。

### 2.6 UI 构建策略

XAML 只保留窗口骨架（顶栏/树/标签容器/状态栏），动态 UI（连接表单、结果网格、
工作区标签内容）全部代码构建。理由：动态列网格在 XAML 绑定下实现复杂度高，
代码构建可测、可控，且规避 XAML 编译器的跨平台限制（见打包指南）。

## 3. 线程模型

- 数据库操作全部 `Task` 异步，不阻塞 UI 线程
- 连接为短生命周期：每次操作 `Open → 执行 → Dispose`，无长连接管理复杂度
  （Redis 例外：`ConnectionMultiplexer` 复用，Provider 持有）
- 查询防重入：`WorkTab._busy` 标志避免并发执行

## 4. 渲染限制（防 OOM / 防假死）

| 限制点 | 阈值 | 行为 |
| --- | --- | --- |
| Execute 读行 | 5000 行/结果集 | 截断 + 消息提示 |
| 网格渲染 | 1000 行 | 尾部提示「导出不受影响」 |
| Redis 键扫描 | 500 键 | 消息提示 |
| Mongo find | 200 文档 | 硬限制 |
| 列数 | 64 列 | 超出截断 |

## 5. 类型检查与逻辑测试

- `typecheck/`：`EnableDefaultCompileItems=false` 收录全部源码 +
  桩（仅 InitializeComponent 与 x:Name 字段），引用真实
  `Microsoft.WinUI.dll` 投影程序集——等效于 Windows 上的 C# 编译校验
- `logictest/`：110 项断言覆盖 SqlSplitter（引号/注释/转义/空脚本）、SqlHighlighter 词法、
  TableDdl 方言生成、
  CSV（BOM/逗号/引号/换行）、INSERT（NULL/二进制/转义）、EngineInfo
