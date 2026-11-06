# 03 · 数据库接入与扩展指南

## 1. 新增一种数据库

以「新增 XXX 数据库」为例，共 4 步：

### 步骤 1：引擎枚举

`Core/DbEngine.cs`：

```csharp
public enum DbEngine { ..., XXX }
```

在 `EngineInfo` 中补 `Name`、`DefaultPort`、`IsFileBased`、`IsRelational` 分支。

### 步骤 2：实现 Provider

关系型继承 `RelationalProvider`（约 60 行）：

```csharp
public class XxxProvider : RelationalProvider
{
    public XxxProvider(ConnectionProfile p) : base(p) { }
    protected override char OpenQuote => '"';   // 标识符引号
    protected override char CloseQuote => '"';
    public override bool SupportsBrowse => true;
    protected override DbConnection Open(string? db) { /* 组连接串 */ }
    protected override string ListDatabasesSql() => "...";
    protected override string ListChildrenSql(string db) => "...";
    protected override string ListColumnsSql(string db, string table) => "...";
    protected override string PagingClause(int offset, int limit) => "...";
}
```

非关系型直接实现 `IDbProvider`（参照 `MongoProvider` / `RedisProvider`）。

### 步骤 3：工厂注册

`RelationalProvider.cs` 的 `ProviderFactory.Create` 加一行：

```csharp
DbEngine.XXX => new XxxProvider(p),
```

### 步骤 4：NuGet 引用

`src/TaoDBM/TaoDBM.csproj`、`typecheck/tc.csproj`、`logictest/logictest.csproj`
三处加同一个 `PackageReference`。

UI 完全不用改——连接表单自动多一项引擎下拉、树与工作区自动生效。

## 2. 现有引擎的连接串组装

各 Provider 的 `Build` 方法都遵循同一模式：
固定参数 → Profile 覆盖（Host/Port/User/Password/Database）→
`Profile.Parameters` 里的 `k=v;k2=v2` 逐项透传给驱动连接串 builder。
因此用户可以在连接参数里写任何驱动支持的选项（如 MySQL 的
`SslMode=Required`、PG 的 `Trust Server Certificate=true`）。

## 3. 添加数据库的验证清单

1. `dotnet build typecheck/tc.csproj` —— 0 错 0 警告
2. `dotnet run --project logictest` —— 18/18（若改了拆分/导出逻辑，补断言）
3. 真机：测试连接 → 展开树 → 新建查询 → 查看数据 → 导出 CSV
4. 连接失败场景：错误密码、错误端口，确认错误信息可读

## 4. 代码约定

- 命名：简短英文语义化（`WorkTab`、`GridBuilder`、`SqlSplitter`），不加冗长前缀
- 面向用户的文案一律中文；注释中文
- 脚本文件（.bat/.cmd）：GBK + CRLF，`build/` 下留 UTF-8 母本
- 新 UI 控件优先代码构建；XAML 仅用于窗口骨架与资源
