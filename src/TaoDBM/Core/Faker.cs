// 拟真数据引擎：字段语义推断（字段名 + 数据类型）→ 类型合法、业务合理的拟真值。
// 确定性：同 seed 同表同列必得同值（可重放）；全部中文语料，符合国内业务习惯。
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TaoDBM.Core;

/// <summary>字段语义槽位（由字段名与类型推断）。</summary>
public enum SemanticKind
{
    Unknown, Id, RowId, PersonName, UserName, NickName, CompanyName, Email, Phone, IdCard,
    Address, City, Province, Country, Age, Gender, Money, Price, Quantity, Percent, Ratio,
    Status, Category, Tag, Title, ContentText, Remark, Summary, Url, ImageUrl, Ip, Port,
    PasswordHash, Token, Code, OrderNo, ColorCode, Birthday, Date, DateTime, TimeOnly, Year,
    Month, Boolean, Json, Language, Job, Education, Industry, Version, Guid, Enumeration,
}

/// <summary>列上下文：名称 + 解析后的类型信息。</summary>
public class ColumnCtx
{
    public string Name { get; set; } = "";
    public string RawType { get; set; } = "";
    public bool Nullable { get; set; } = true;
    public bool IsPrimaryKey { get; set; }
    /// <summary>字符串长度上限（0 = 无界）。</summary>
    public int MaxLen { get; set; }
    /// <summary>数值精度（scale 位小数）。</summary>
    public int Precision { get; set; }
    public int Scale { get; set; }
    /// <summary>枚举可选取值（ENUM('a','b') 等解析结果）。</summary>
    public List<string> EnumValues { get; set; } = new();
}

/// <summary>拟真数据生成器（纯逻辑，可测试）。</summary>
public static class Faker
{
    // ---------- 词库（中文拟真） ----------

    internal static readonly string[] Surnames =
    {
        "张","王","李","赵","刘","陈","杨","黄","周","吴","徐","孙","马","朱","胡","郭","何","林","罗","高",
        "郑","梁","谢","宋","唐","许","韩","冯","邓","曹","彭","曾","肖","田","董","潘","袁","蒋","蔡","余",
        "杜","叶","程","苏","魏","吕","丁","任","卢","姚","沈","钟","姜","崔","谭","陆","范","汪","廖","石",
    };
    internal static readonly string[] GivenNames =
    {
        "伟","芳","娜","敏","静","丽","强","磊","军","洋","勇","艳","杰","娟","涛","明","超","霞","平","刚",
        "桂英","玉兰","婷婷","慧","颖","浩","宇","欣","怡","子涵","梓萱","浩然","思远","雨泽","明轩","诗涵",
        "梦琪","语嫣","晓东","志强","建国","建军","淑芬","桂芳","文静","海燕","春花","国庆","卫东","红梅",
        "俊杰","晨曦","雨桐","天佑","若彤","佳琪","紫萱","皓轩","致远","立诚","嘉懿","瑾瑜","子墨","思聪",
    };
    internal static readonly string[] Cities =
    {
        "北京","上海","广州","深圳","杭州","成都","武汉","西安","南京","重庆","苏州","天津","长沙","郑州",
        "青岛","合肥","福州","厦门","昆明","济南","大连","无锡","佛山","东莞","宁波","沈阳","石家庄","南昌",
        "贵阳","南宁","太原","珠海","惠州","温州","绍兴","长春","泉州","烟台","嘉兴","海口","兰州","乌鲁木齐",
    };
    internal static readonly string[] Provinces =
    {
        "北京市","上海市","广东省","江苏省","浙江省","山东省","四川省","河南省","湖北省","湖南省","河北省",
        "福建省","辽宁省","吉林省","黑龙江省","安徽省","江西省","广西壮族自治区","海南省","贵州省","云南省",
        "陕西省","甘肃省","青海省","内蒙古自治区","新疆维吾尔自治区","西藏自治区","宁夏回族自治区",
    };
    internal static readonly string[] Streets =
    {
        "中山路","人民路","解放大道","和平街","建设路","文化路","朝阳街","长江路","黄河路","新华街",
        "光明路","振兴街","繁荣路","幸福路","康乐街","花园街","望江路","临江大道","科技大道","创业路",
    };
    internal static readonly string[] CompanyPrefixes =
    {
        "华星","天启","云图","青松","极光","星河","紫微","玄武","聚变","元界","灵犀","开拓","宏图","远景",
        "蓝海","破晓","光年","鹏程","磐石","锐进","和光","同尘","九章","扶摇","鲲鹏","拾贝","澄明","知行",
    };
    internal static readonly string[] CompanySuffixes = { "科技", "信息技术", "网络科技", "软件", "数据科技", "智能科技", "传媒", "电子商务", "咨询", "生物科技" };
    internal static readonly string[] Industries =
    {
        "互联网","金融","教育","医疗","制造","零售","物流","能源","建筑","农业","旅游","传媒","通信","政务","文化",
    };
    internal static readonly string[] JobTitles =
    {
        "软件工程师","产品经理","运营专员","设计师","数据分析师","测试工程师","项目经理","销售顾问","市场专员",
        "人事专员","财务主管","客服代表","架构师","算法工程师","运维工程师","前端开发","后端开发","行政助理",
    };
    internal static readonly string[] Educations = { "高中及以下", "大专", "本科", "硕士", "博士" };
    internal static readonly string[] StatusWords =
    {
        "启用","停用","冻结","正常","待审核","已审核","已完成","进行中","已取消","已发货","待支付","已支付",
        "退款中","已关闭","草稿","已发布","已归档","处理中","已拒绝","生效中",
    };
    internal static readonly string[] CategoryWords =
    {
        "数码","家电","服饰","食品","美妆","母婴","图书","运动","家居","汽车","办公","宠物","玩具","医药","百货",
    };
    internal static readonly string[] TagWords =
    {
        "热销","新品","限量","优选","爆款","包邮","秒杀","特价","直降","人气","推荐","置顶","精品","免邮","回馈",
    };
    internal static readonly string[] TitleWords =
    {
        "智能","云","极光","星辰","青柠","墨","晨曦","星轨","万象","灵犀","天枢","北斗","苍穹","流萤","拾光",
    };
    internal static readonly string[] TitleSuffixes =
    {
        "平台","系统","引擎","管家","助手","网关","盒子","工作台","实验室","计划","行动","日志","手记","笔记",
    };
    internal static readonly string[] RemarkSentences =
    {
        "客户要求加急处理，请尽快跟进。",
        "已电话沟通确认，细节见附件。",
        "等待供应商发货，预计三天内到。",
        "系统升级期间暂停服务，请知悉。",
        "该客户为年度大客户，优先安排。",
        "上次反馈的问题已修复，请回归验证。",
        "合同续签事宜需法务确认后再推进。",
        "数据核对无误，可以进入下一阶段。",
        "成本超预算 5%，需重新评估方案。",
        "测试环境已就绪，随时可以联调。",
    };
    internal static readonly string[] ContentParagraphs =
    {
        "本方案覆盖从需求梳理、原型设计到上线验收的全流程，里程碑与责任人一一对应。",
        "随着业务规模扩大，原有的单体架构逐渐暴露出扩展性瓶颈，服务化改造势在必行。",
        "数据显示，用户留存率在上线第二个月达到峰值，随后进入平稳期。",
        "我们建议优先解决支付链路的稳定性问题，再逐步推进个性化推荐。",
        "本次评审通过后，相关接口文档将同步更新至共享空间。",
        "培训计划分为三个阶段：基础认知、专项技能与综合演练，每阶段均配有考核。",
        "注意：该配置只对新增数据生效，历史数据需手动迁移。",
        "经财务复核，本季度预算执行率 92%，符合预期目标。",
    };
    internal static readonly string[] EmailDomains = { "qq.com", "163.com", "126.com", "gmail.com", "outlook.com", "foxmail.com", "sina.com" };
    internal static readonly string[] Genders = { "男", "女" };
    internal static readonly string[] Versions = { "1.0.0", "1.2.3", "2.0.1", "2.3.0", "3.1.4", "10.2.0", "0.9.9" };

    /// <summary>固定基准日：日期区间只由种子决定，重放必一致（避免依赖运行时钟）。</summary>
    internal static readonly DateTime BaseDate = new(2026, 11, 1);

    // ---------- 语义推断（字段名 + 类型） ----------

    /// <summary>按字段名与类型推断语义（纯函数，可测试）。</summary>
    public static SemanticKind InferSemantic(string fieldName, string rawType)
    {
        var name = Regex.Replace(fieldName ?? "", @"[\s\-_\.]+", "_").ToLowerInvariant();
        var t = NormType(rawType);

        // 布尔/标志优先：is_xxx / has_xxx / xxx_flag / enabled / deleted(位) / tinyint(1)
        if (name.StartsWith("is_") || name.StartsWith("has_") || name.StartsWith("can_") ||
            name.EndsWith("_flag") || name is "enabled" or "disabled" or "visible" or "active" ||
            (name is "deleted" or "removed" or "locked") && IsTinyInt(t))
            return SemanticKind.Boolean;

        // 强类型信号：JSON 列就是 JSON
        if (t == "JSON" || t == "JSONB") return SemanticKind.Json;

        // 特殊语义先于通用编号：身份证号、版本号
        if (name.Contains("idcard") || name.Contains("id_card") || name == "card_no") return SemanticKind.IdCard;
        if (name.Contains("version")) return SemanticKind.Version;

        if (name is "id" or "pid" or "uid" or "guid" || name.EndsWith("_id") || name.EndsWith("_no") ||
            name.EndsWith("_code") || name == "code" || name.EndsWith("_sn") || name.EndsWith("_key") ||
            name.Contains("uuid") || name.Contains("guid"))
        {
            if (name.Contains("uuid") || name.Contains("guid") || IsUuidLike(name, t)) return SemanticKind.Guid;
            if (name == "id" || name.EndsWith("_id")) return SemanticKind.Id;
            if (name.Contains("order") || name.Contains("bill")) return SemanticKind.OrderNo;
            return SemanticKind.Code;
        }
        if (name.Contains("email") || name.Contains("mail")) return SemanticKind.Email;
        if (name.Contains("phone") || name.Contains("mobile") || name.Contains("tel")) return SemanticKind.Phone;
        if (name.Contains("passwd") || name.Contains("password") || name == "pwd") return SemanticKind.PasswordHash;
        if (name.Contains("token") || name.Contains("secret") || name.Contains("apikey") || name.Contains("api_key")) return SemanticKind.Token;
        if (name.Contains("avatar") || name.Contains("image") || name.Contains("photo") || name.Contains("cover") ||
            name.Contains("thumb") || name.Contains("icon") || name.Contains("img")) return SemanticKind.ImageUrl;
        if (name.Contains("url") || name.Contains("link") || name.Contains("website") ||
            name.Contains("homepage") || name.EndsWith("_page")) return SemanticKind.Url;
        if (name == "ip" || name.EndsWith("_ip") || name == "host") return SemanticKind.Ip;
        if (name == "port" || name.EndsWith("_port")) return SemanticKind.Port;
        if (name.Contains("birthday") || name.Contains("birth")) return SemanticKind.Birthday;
        if (name.Contains("created") || name.Contains("updated") || name.Contains("modified") ||
            name.Contains("expire") || name.Contains("deleted_at") || name.Contains("publish") ||
            name.Contains("paid") || name.Contains("shipped") || name.Contains("login") ||
            name.EndsWith("_at") || name.EndsWith("_time") || name.Contains("datetime"))
        {
            return t.Contains("date", StringComparison.OrdinalIgnoreCase) || t.Contains("time", StringComparison.OrdinalIgnoreCase)
                ? (t.Contains("date") && !t.Contains("time") ? SemanticKind.Date : SemanticKind.DateTime)
                : SemanticKind.DateTime;
        }
        if (name.Contains("year")) return SemanticKind.Year;
        if (name.Contains("month")) return SemanticKind.Month;
        if (name == "age" || name.EndsWith("_age")) return SemanticKind.Age;
        if (name.Contains("gender") || name.Contains("sex")) return SemanticKind.Gender;
        if (name.Contains("price")) return SemanticKind.Price;
        if (name.Contains("money") || name.Contains("amount") ||
            name.Contains("fee") || name.Contains("salary") || name.Contains("cost") || name.Contains("balance") ||
            name.Contains("payment") || name.Contains("total"))
            return SemanticKind.Money;
        if (name.Contains("qty") || name.Contains("quantity") || name.Contains("stock") ||
            name.Contains("count") || name.EndsWith("_num") || name == "num" || name.Contains("number"))
            return SemanticKind.Quantity;
        if (name.Contains("percent") || name.Contains("rate") || name.Contains("ratio") || name.Contains("discount"))
            return SemanticKind.Percent;
        if (name.Contains("status") || name.Contains("state")) return SemanticKind.Status;
        if (name.Contains("type") || name.Contains("kind") || name.Contains("category") || name.EndsWith("_group"))
            return SemanticKind.Category;
        if (name.Contains("tag")) return SemanticKind.Tag;
        if (name.Contains("job") || name.Contains("position") || name.Contains("occupation"))
            return SemanticKind.Job;
        if (name.Contains("company") || name.Contains("corp") || name.Contains("org")) return SemanticKind.CompanyName;
        if (name.Contains("address") || name.Contains("addr")) return SemanticKind.Address;
        if (name.Contains("province") || name.Contains("state_name")) return SemanticKind.Province;
        if (name.Contains("city")) return SemanticKind.City;
        if (name.Contains("country") || name.Contains("nation")) return SemanticKind.Country;
        if (name.Contains("title") || name.Contains("subject") || name.Contains("heading") || name == "name" && IsTextType(t))
            return SemanticKind.Title;
        if (name.Contains("desc") || name.Contains("summary") || name.Contains("intro") || name.Contains("remark") ||
            name.Contains("comment") || name.Contains("note") || name.Contains("memo") || name.Contains("detail"))
            return SemanticKind.Remark;
        if (name.Contains("content") || name.Contains("text") || name.Contains("body") || name.Contains("article"))
            return SemanticKind.ContentText;
        if (name.Contains("education") || name.Contains("degree")) return SemanticKind.Education;
        if (name.Contains("industry") || name.Contains("trade")) return SemanticKind.Industry;
        if (name.Contains("lang") || name.Contains("locale")) return SemanticKind.Language;
        if (name.Contains("color") || name.Contains("colour")) return SemanticKind.ColorCode;
        if (name is "username" or "user_name" or "account" || name.Contains("login_name")) return SemanticKind.UserName;
        if (name.Contains("nickname") || name.Contains("nick_name")) return SemanticKind.NickName;
        if (IsTextType(t) && (name.Contains("name") || name.EndsWith("name"))) return SemanticKind.PersonName;

        // 类型兜底
        return t switch
        {
            var x when IsUuidType(x) => SemanticKind.Guid,
            var x when IsEnumType(x) => SemanticKind.Enumeration,
            var x when IsTinyInt(x) => SemanticKind.Boolean,
            var x when IsDateType(x) => x.Contains("time") ? SemanticKind.DateTime : SemanticKind.Date,
            var x when IsNumericType(x) => SemanticKind.Quantity,
            var x when IsTextType(x) => SemanticKind.ContentText,
            _ => SemanticKind.Unknown,
        };
    }

    // ---------- 类型解析与判断（纯函数） ----------

    public static string NormType(string raw) => Regex.Replace(raw ?? "", @"\s+", " ").Trim().ToUpperInvariant();

    public static string BaseType(string raw) => Regex.Replace(NormType(raw), @"\(.*\)", "").Trim();

    /// <summary>VARCHAR(50) → 50；无括号 0。</summary>
    public static int ParseLen(string raw)
    {
        var m = Regex.Match(NormType(raw), @"\((\d+)\s*(?:,\s*\d+\s*)?\)");
        return m.Success ? int.Parse(m.Groups[1].Value) : 0;
    }

    /// <summary>DECIMAL(10,2) → (10,2)。</summary>
    public static (int p, int s) ParsePrecision(string raw)
    {
        var m = Regex.Match(NormType(raw), @"\((\d+)\s*,\s*(\d+)\s*\)");
        return m.Success ? (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : (0, 0);
    }

    /// <summary>ENUM('a','b','c') → 取值清单。</summary>
    public static List<string> ParseEnumValues(string raw)
    {
        var m = Regex.Match(NormType(raw), @"(ENUM|SET)\s*\((.*)\)", RegexOptions.IgnoreCase);
        if (!m.Success) return new();
        var inner = m.Groups[2].Value;
        return Regex.Matches(inner, @"'((?:[^']|'')*)'")
            .Select(x => x.Groups[1].Value.Replace("''", "'")).ToList();
    }

    public static bool IsNumericType(string raw) => BaseType(raw) is
        "TINYINT" or "SMALLINT" or "MEDIUMINT" or "INT" or "INTEGER" or "BIGINT" or "DECIMAL" or "NUMERIC" or
        "FLOAT" or "DOUBLE" or "REAL" or "MONEY" or "NUMBER" or "SMALLMONEY" or "SERIAL";
    public static bool IsDateType(string raw) => BaseType(raw) is "DATE" or "DATETIME" or "TIMESTAMP" or "TIME" or "SMALLDATETIME" or "DATETIME2";
    public static bool IsTextType(string raw) => BaseType(raw) is "CHAR" or "VARCHAR" or "NVARCHAR" or "NCHAR" or "TEXT" or "NTEXT" or "CLOB" or "VARCHAR2" or "NVARCHAR2" or "LONGTEXT" or "MEDIUMTEXT" or "TINYTEXT" or "STRING";
    public static bool IsUuidType(string raw) => BaseType(raw) is "UUID" or "UNIQUEIDENTIFIER";
    public static bool IsEnumType(string raw) => NormType(raw).StartsWith("ENUM") || NormType(raw).StartsWith("SET");
    public static bool IsTinyInt(string raw) => BaseType(raw) is "TINYINT" or "BIT" or "BOOLEAN" or "BOOL";
    private static bool IsUuidLike(string name, string raw) => IsUuidType(raw) || name.Contains("uuid") || name.Contains("guid");

    // ---------- 值生成（确定性：Random 已由调用方按 seed 建立） ----------

    /// <summary>生成一个拟真值；返回对象（null 表示放 NULL）。</summary>
    public static object? Gen(SemanticKind kind, ColumnCtx col, Random r, long rowNo, string? poolValue = null)
    {
        if (col.Nullable && rowNo % 7 == 3 && kind != SemanticKind.Id && !col.IsPrimaryKey) return null;   // 稀疏 NULL

        if (poolValue is not null && (kind == SemanticKind.Id || col.IsPrimaryKey)) return poolValue;   // 关联列取池值

        var t = NormType(col.RawType);
        return kind switch
        {
            SemanticKind.Id => NumericId(col, r, rowNo),
            SemanticKind.Guid => GuidLike(col, r),
            SemanticKind.Code or SemanticKind.OrderNo => CodeLike(kind, col, r, rowNo),
            SemanticKind.PersonName => Truncate(PersonName(r), col),
            SemanticKind.UserName => Truncate(UserName(r, rowNo), col),
            SemanticKind.NickName => Truncate(NickName(r), col),
            SemanticKind.CompanyName => Truncate(CompanyName(r), col),
            SemanticKind.Email => Truncate(Email(r, rowNo), col),
            SemanticKind.Phone => Phone(r),
            SemanticKind.IdCard => IdCard(r),
            SemanticKind.Address => Truncate(Address(r), col),
            SemanticKind.City => Pick(Cities, r, col),
            SemanticKind.Province => Pick(Provinces, r, col),
            SemanticKind.Country => r.Next(10) < 8 ? "中国" : "海外",
            SemanticKind.Age => ClampInt(r.Next(18, 66), col),
            SemanticKind.Gender => Pick(Genders, r, col),
            SemanticKind.Money => Money(col, r),
            SemanticKind.Price => Money(col, r),
            SemanticKind.Quantity => ClampInt(r.Next(0, 1000), col),
            SemanticKind.Percent => Percent(col, r),
            SemanticKind.Ratio => Percent(col, r),
            SemanticKind.Status => col.EnumValues.Count > 0 ? Pick(col.EnumValues, r, col) : Pick(StatusWords, r, col),
            SemanticKind.Category => col.EnumValues.Count > 0 ? Pick(col.EnumValues, r, col) : Pick(CategoryWords, r, col),
            SemanticKind.Tag => Truncate(Pick(TagWords, r, col), col),
            SemanticKind.Enumeration => col.EnumValues.Count > 0 ? Pick(col.EnumValues, r, col) : Pick(StatusWords, r, col),
            SemanticKind.Title => Truncate(Title(r), col),
            SemanticKind.ContentText => Truncate(Content(r, col), col),
            SemanticKind.Remark => Truncate(Pick(RemarkSentences, r, col), col),
            SemanticKind.Summary => Truncate(Content(r, col), col),
            SemanticKind.Url => Truncate(Url(r, rowNo), col),
            SemanticKind.ImageUrl => Truncate(ImgUrl(r, rowNo), col),
            SemanticKind.Ip => $"{r.Next(1, 224)}.{r.Next(0, 256)}.{r.Next(0, 256)}.{r.Next(1, 255)}",
            SemanticKind.Port => ClampInt(r.Next(1024, 65536), col),
            SemanticKind.PasswordHash => Truncate(HashLike(r), col),
            SemanticKind.Token => Truncate(HashLike(r) + HashLike(r)[..8], col),
            SemanticKind.ColorCode => $"#{r.Next(0x1000000):X6}",
            SemanticKind.Birthday => DateStr(Birthday(r)),
            SemanticKind.Date => DateStr(RandomDate(r, BaseDate.AddYears(-3), BaseDate)),
            SemanticKind.DateTime => DateTimeStr(RandomDate(r, BaseDate.AddYears(-3), BaseDate)),
            SemanticKind.TimeOnly => $"{r.Next(0, 24):00}:{r.Next(0, 60):00}:{r.Next(0, 60):00}",
            SemanticKind.Year => ClampInt(r.Next(1990, BaseDate.Year + 1), col),
            SemanticKind.Month => ClampInt(r.Next(1, 13), col),
            SemanticKind.Boolean => BoolValue(t, r),
            SemanticKind.Json => Truncate("{\"ok\":true,\"n\":" + r.Next(100) + "}", col),
            SemanticKind.Language => r.Next(10) < 8 ? "zh-CN" : "en-US",
            SemanticKind.Job => Truncate(Pick(JobTitles, r, col), col),
            SemanticKind.Education => Pick(Educations, r, col),
            SemanticKind.Industry => Pick(Industries, r, col),
            SemanticKind.Version => Pick(Versions, r, col),
            _ => UnknownValue(col, r, rowNo),
        };
    }

    internal static string Pick(System.Collections.Generic.IReadOnlyList<string> pool, Random r, ColumnCtx col)
    {
        var v = pool[r.Next(pool.Count)];
        return col.MaxLen > 0 && v.Length > col.MaxLen ? v[..col.MaxLen] : v;
    }

    internal static string PersonName(Random r) => Surnames[r.Next(Surnames.Length)] + GivenNames[r.Next(GivenNames.Length)];

    internal static string UserName(Random r, long rowNo) =>
        "user" + (100000 + r.Next(900000)).ToString(CultureInfo.InvariantCulture);

    internal static string NickName(Random r) => Pick(new[]
    {
        "清风","明月","星辰","大海","青柠","南山","听雨","流云","望舒","知秋","逐光","静水","若尘","照夜","行歌",
    }, r, new ColumnCtx());

    internal static string CompanyName(Random r) =>
        CompanyPrefixes[r.Next(CompanyPrefixes.Length)] + CompanySuffixes[r.Next(CompanySuffixes.Length)] + "有限公司";

    internal static string Email(Random r, long rowNo) =>
        Pick(new[] { "tom", "jerry", "alice", "bob", "lily", "han", "wei", "ming", "xiao", "lin" }, r, new ColumnCtx())
        + (1000 + r.Next(9000)).ToString(CultureInfo.InvariantCulture)
        + "@" + EmailDomains[r.Next(EmailDomains.Length)];

    internal static string Phone(Random r) => "1" + Pick(new[] { "3", "5", "7", "8", "9" }, r, new ColumnCtx()) +
        r.Next(100000000, 999999999).ToString(CultureInfo.InvariantCulture)[..9].PadLeft(9, '0');

    internal static string IdCard(Random r)
    {
        var area = new[] { "110101", "310104", "440305", "330102", "510107", "420102" };
        return area[r.Next(area.Length)] + r.Next(1955, 2006) + $"{r.Next(1, 13):00}{r.Next(1, 29):00}"
            + r.Next(100, 999).ToString(CultureInfo.InvariantCulture);
    }

    internal static string Address(Random r) =>
        Pick(Cities, r, new ColumnCtx()) + "市" + Pick(Streets, r, new ColumnCtx())
        + r.Next(1, 999) + "号" + (r.Next(2) == 0 ? "" : $"{r.Next(1, 30)}栋{r.Next(101, 3202)}室");

    internal static string Title(Random r) =>
        Pick(TitleWords, r, new ColumnCtx()) + Pick(TitleSuffixes, r, new ColumnCtx())
        + (r.Next(3) == 0 ? Pick(IndustryTail(), r, new ColumnCtx()) : "");

    internal static string[] IndustryTail() => new[] { "（华东）", "（华南）", "·2026", "Pro", "" };

    internal static string Content(Random r, ColumnCtx col)
    {
        int n = col.MaxLen == 0 || col.MaxLen > 120 ? 2 : 1;
        var sb = new StringBuilder();
        for (int i = 0; i < n; i++) sb.Append(ContentParagraphs[r.Next(ContentParagraphs.Length)]);
        return sb.ToString();
    }

    internal static string Url(Random r, long rowNo) =>
        "https://" + CompanyPrefixes[r.Next(CompanyPrefixes.Length)].ToLowerInvariant() +
        r.Next(1, 99) + ".example.com/" + (100000 + r.Next(900000));

    internal static string ImgUrl(Random r, long rowNo) => Url(r, rowNo) + "/img.png";

    internal static string HashLike(Random r) =>
        Convert.ToHexString(MD5.HashData(BitConverter.GetBytes(r.NextInt64()))).ToLowerInvariant();

    internal static string DateStr(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static string DateTimeStr(DateTime d) => d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    internal static DateTime Birthday(Random r) => RandomDate(r, new DateTime(1956, 1, 1), new DateTime(2008, 12, 31));
    internal static DateTime RandomDate(Random r, DateTime from, DateTime to) =>
        from.AddDays(r.Next((int)Math.Max(1, (to - from).TotalDays)));

    internal static object BoolValue(string t, Random r) =>
        BaseType(t) is "BIT" or "BOOLEAN" or "BOOL" ? (object)(r.Next(10) < 8) : r.Next(10) < 8 ? 1 : 0;

    internal static object NumericId(ColumnCtx col, Random r, long rowNo) => BaseType(col.RawType) switch
    {
        "BIGINT" or "SERIAL" => (object)rowNo,
        _ => (object)(int)rowNo,
    };

    internal static object GuidLike(ColumnCtx col, Random r) =>
        BaseType(col.RawType) is "UUID" or "UNIQUEIDENTIFIER"
            ? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString("N")[..Math.Max(8, col.MaxLen > 0 ? Math.Min(col.MaxLen, 32) : 32)];

    internal static object CodeLike(SemanticKind kind, ColumnCtx col, Random r, long rowNo)
    {
        var head = kind == SemanticKind.OrderNo
            ? BaseDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
            : "";
        var body = head + rowNo.ToString(CultureInfo.InvariantCulture).PadLeft(6, '0') + r.Next(10, 99);
        if (col.MaxLen > 0 && body.Length > col.MaxLen) body = body[..col.MaxLen];
        return body;
    }

    internal static object Money(ColumnCtx col, Random r)
    {
        var (_, scale) = col.Scale > 0 ? (col.Precision, col.Scale) : (0, 2);
        var v = (decimal)(r.NextDouble() * 9999 + 0.01);
        var rounded = Math.Round(v, Math.Clamp(scale, 0, 6));
        return rounded;
    }

    internal static object Percent(ColumnCtx col, Random r)
    {
        var (_, scale) = col.Scale > 0 ? (col.Precision, col.Scale) : (0, 2);
        return Math.Round((decimal)(r.NextDouble() * 100), Math.Clamp(scale, 0, 4));
    }

    internal static object ClampInt(int v, ColumnCtx col) => v;

    internal static object UnknownValue(ColumnCtx col, Random r, long rowNo)
    {
        var t = NormType(col.RawType);
        if (IsNumericType(t)) return ClampInt(r.Next(0, 10000), col);
        if (IsDateType(t)) return BaseType(t) == "DATE" ? DateStr(RandomDate(r, BaseDate.AddYears(-2), BaseDate)) : DateTimeStr(RandomDate(r, BaseDate.AddYears(-2), BaseDate));
        if (IsUuidType(t)) return GuidLike(col, r);
        var s = "TaoDBM-" + rowNo.ToString(CultureInfo.InvariantCulture) + "-" + HashLike(r)[..6];
        return Truncate(s, col);
    }

    internal static string Truncate(string s, ColumnCtx col)
    {
        if (col.MaxLen > 0 && s.Length > col.MaxLen) return s[..col.MaxLen];
        return s;
    }
}
