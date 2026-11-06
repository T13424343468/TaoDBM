# TaoDBM

WinUI 3 + .NET 8 开发的 Windows 桌面数据库管理工具，Ant Design 视觉风格，
支持主流关系型与非关系型数据库的连接管理、对象浏览、SQL 查询执行与数据导出。

![图标](src/TaoDBM/Assets/TaoDBM.png)

> 道生一，一生二，二生三，三生万物。数据即万物。

## 支持的数据库

| 类型 | 引擎 | 说明 |
| --- | --- | --- |
| 关系型 | MySQL / MariaDB | 库、表、列浏览，SQL 执行，分页浏览 |
| 关系型 | PostgreSQL | 库、表、列浏览，SQL 执行，分页浏览 |
| 关系型 | SQL Server | 库、表、列浏览，SQL 执行，分页浏览 |
| 关系型 | SQLite | 单文件直连，表浏览，SQL 执行 |
| 关系型 | Oracle | 12c+ 分页语法，用户/表浏览 |
| 非关系型 | MongoDB | 库、集合浏览，JSON 过滤器查询，文档浏览 |
| 非关系型 | Redis | db0-15 切换，键扫描，GET/TYPE/TTL/DEL 等命令 |

通用：SSH 跳板机隧道（全部引擎可用，密码本机加密保存）。

> 数据存储（Tao 系列规范）：连接、收藏、历史、会话、主题等本机数据默认存
> `D:\TaoDBMData\TaoDBM`，不占 C 盘；D 盘不可用时自动回退
> `%LOCALAPPDATA%\TaoDBM`；首次运行自动迁移旧 C 盘数据（不覆盖已有文件）。

## 核心功能

- 连接管理：新建 / 编辑 / 删除 / 测试连接，密码 DPAPI 本机加密保存，SSH 隧道；
  **左侧树右键菜单**：连接节点可展开 / 编辑 / 关闭 / 删除（关闭 = 断开并关隧道，配置保留）
- **分库展示**（v1.4.2）：按每个库选最合理的展示方式——MySQL 等关系库保持表格网格（可编辑）；
  Redis 用键值视图（库级键列表：键名 / 类型 / TTL / 大小，单击键进详情页看值并可直接删除）；
  MongoDB 用 JSON 文档列表
- **连接不再卡界面**（v1.4.2）：连接 / 扫表 / 查询全部在后台线程流转，SSH 隧道建立、
  Redis 首连等耗时操作不再阻塞界面；各引擎连接串统一 10 秒超时，连不上快速失败
- **拟真数据多表模式**（v1.4.2）：一键给库内多个表生成拟真数据，每个表独立设定行数
  （留空不生成），按外键 / 字段名关联拓扑顺序执行（被引用表先写入），引用列自动取父表值
- 对象浏览器：连接 → 库 → 表 / 集合 / 键，懒加载展开，随时刷新
- 查询编辑器：**SQL 语法着色**（关键字/类型/字符串/注释/数字分色，Monaco 风格）、多语句执行
  （自动按分号拆分）、F5 / Ctrl+Enter 执行、耗时与行数统计、Ctrl+Space 关键字与列名补全、
  查询历史（本机保存最近 300 条）
- **表结构设计器**：可视化建表 / 改表（加列、改列、删列、主键、自增、默认值），DDL 实时预览，
  按引擎方言生成（MySQL AUTO_INCREMENT / PG 与 Oracle IDENTITY / SQL Server IDENTITY(1,1) /
  SQLite AUTOINCREMENT），SQLite 改列自动跳过并提示
- 结果网格：多结果集顺序展示、行序斑马纹、超 5000 行自动截断提示
- 数据浏览：表 / 集合分页浏览（每页 200 行）+ 总行数
- **数据编辑**：单击结果行弹出编辑窗，修改 / 删除（主键保护，无主键表拒绝编辑）、新增行
- 数据导入：CSV（RFC 4180 解析，数字列自动识别，100 行/批）与 JSON（对象数组 / 行式 JSONL，
  嵌套结构序列化为文本，复用类型识别与转义）导入当前表
- 表结构查看：一键查看列名 / 类型 / 可空 / 主键
- 数据导出：CSV（UTF-8 BOM，Excel 直接打开不乱码）、INSERT 语句导出
- PostgreSQL：列出所有非系统 schema 的表（schema.table 形式）；SQL Server 同样按
  schema.table 多 schema 展示
- **报表**：查询结果一键统计（每列空值 / 去重 / 数值列极值均值 / 非数值列 Top 值），
  导出 Ant 风格独立 HTML 报表，浏览器直接打开
- **模型设计（ER 图）**：库内表结构一键成图，主外键关系自动推断（user_id → user），
  框可拖拽，导出 PNG，布局可保存 / 打开（.erjson）
- **数据传输与结构同步**：源连接 → 目标连接整表搬运，自动补目标缺失列（可选对齐列类型），
  目标缺表自动按源结构建表，全量数据分批（200 行/批）写入，逐表报告行数
- **备份 / 还原**：MySQL（mysqldump/mysql）、PostgreSQL（pg_dump/psql）、SQL Server（T-SQL
  BACKUP/RESTORE）、SQLite（文件复制）；密码经环境变量传递，不进命令行
- **Oracle 服务名发现**：连接对话框一键解析 tnsnames.ora（注释 / 多别名 / 嵌套描述符），
  选中条目自动回填主机、端口与服务名

## 目录结构

```
TaoDBM/
├── TaoDBM.sln                 # 解决方案
├── build-installer.bat        # 一键打包批处理（GBK+CRLF）
├── assets/make_icons.py       # 图标生成脚本（道·太极 + 数据库圆柱）
├── docs/                      # 全套开发文档
│   ├── 01-REQUIREMENTS.md
│   ├── 02-ARCHITECTURE.md
│   ├── 03-DB_CONNECTORS.md
│   ├── 04-USER_MANUAL.md
│   └── 05-PACKAGING_GUIDE.md
├── logictest/                 # 核心逻辑测试（121 项断言）
├── typecheck/                 # 类型检查工程（引用真实 WinUI 投影程序集）
└── src/
    ├── TaoDBM/                # WinUI 3 主程序
    │   ├── Core/              # 引擎定义、连接存储、查询结果模型
    │   │   └── Providers/     # 统一驱动层（5 关系型 + 2 非关系型）
    │   ├── UI/                # 连接对话框、结果网格、工作区标签页、导出
    │   ├── App.xaml           # Ant Design 色板资源
    │   └── MainWindow.xaml    # 主窗口（连接树 + 工作区标签）
    └── Installer/             # WiX v4 安装包（MSI，微信式单页界面）
```

## 快速开始

### 环境

- Windows 10 1809+ / Windows 11
- .NET 8 SDK（Windows x64）
- 打包需要 WiX（首次运行批处理会自动安装）

### 编译运行

```bat
dotnet build src\TaoDBM\TaoDBM.csproj -c Release -p:Platform=x64
```

### 一键打包

双击 `build-installer.bat`，产物在 `out\`：

- `TaoDBM-1.0.0-x64.msi` —— 微信式单页安装包（唯一交付形态，装完即用）

## 开发验证（本项目内置的可重复验证方式）

- `typecheck/`：在非 Windows 环境引用真实 WinUI 投影程序集做全量类型检查（0 错 0 警告）
- `logictest/`：SQL 拆分器、着色器词法、DDL 生成（建表/改表/方言）、CSV / JSON 导入导出、INSERT 生成、引擎元信息、查询历史、查询收藏、标签会话、备份命令构造、数据传输与结构同步、ER 关系推断、报表统计、tnsnames.ora 解析，138 项四层测试（单元/集成/系统/暴力）通过
- 安装器：WiX 清单经真实 wix 工具编译验证（MSI 最终封装需 Windows，见打包指南）

## Roadmap（收尾）

1. ~~Oracle 服务名自动发现、SQL Server 多 schema 树、JSON / Excel 导入~~ 已完成（Excel
   导入先转 CSV 即可，原生 .xlsx 解析列入后续迭代）
2. Navicat 对标方向后续可继续扩展：结构同步可视化 diff、模型设计（多表关联编辑）、
   定时任务、数据字典导出等——按用户反馈排期

## 设计说明

- 命名沿用「Tao」家族（TaoPad / TaoZip / TaoDBM），图标以道与太极为思路：
  玄蓝渐变底、太极双鱼（阴阳金点）、右下角金色数据库圆柱，明确表达「DB 管理工具」。
- 界面遵循 Ant Design 规范：主色 #1677FF，#F5F6F8 浅灰底，白卡片 + 8px 圆角，
  表头 #F0F5FF，分隔线 #E5E7EB。
- 安装器为微信式单页：一页完成协议勾选、目录选择与组件勾选，点「立即安装」直达。

## 许可

个人与商业用途自由使用，按「现状」提供，不含任何担保。
