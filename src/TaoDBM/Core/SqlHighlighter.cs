namespace TaoDBM.Core;

/// <summary>SQL 词法单元种类。</summary>
public enum TokenKind
{
    /// <summary>普通标识符 / 空白 / 标点，不着色。</summary>
    Plain,
    /// <summary>SQL 关键字。</summary>
    Keyword,
    /// <summary>数据类型。</summary>
    TypeName,
    /// <summary>字符串字面量。</summary>
    String,
    /// <summary>注释（行 / 块）。</summary>
    Comment,
    /// <summary>数字（含十六进制、科学计数）。</summary>
    Number,
    /// <summary>双引号 / 反引号引用的标识符。</summary>
    QuotedIdent,
    /// <summary>运算符。</summary>
    Operator,
}

public readonly record struct SqlToken(int Start, int Length, TokenKind Kind);

/// <summary>SQL 语法着色内核：纯词法分析，不依赖 UI，与 AntPad 同思路的轻量实现。</summary>
public static class SqlHighlighter
{
    private static readonly HashSet<string> KwSet = new(
        @"SELECT FROM WHERE INSERT INTO VALUES UPDATE SET DELETE CREATE TABLE ALTER DROP ADD COLUMN INDEX VIEW
TRIGGER DATABASE SCHEMA JOIN INNER LEFT RIGHT FULL OUTER CROSS ON AS AND OR NOT NULL IS IN BETWEEN LIKE RLIKE REGEXP
EXISTS UNION ALL DISTINCT GROUP BY ORDER HAVING LIMIT OFFSET FETCH TOP CASE WHEN THEN ELSE END WHILE IF BEGIN END
COMMIT ROLLBACK TRANSACTION WORK PRIMARY FOREIGN KEY REFERENCES CONSTRAINT UNIQUE DEFAULT CHECK CASCADE RESTRICT
TRUNCATE RENAME USE GRANT REVOKE EXPLAIN DESCRIBE DESC ASC RETURNING CONFLICT REPLACE LOCK UNLOCK ANALYZE VACUUM
INTERVAL CURRENT_DATE CURRENT_TIME CURRENT_TIMESTAMP CURRENT_USER LOCALTIME LOCALTIMESTAMP NOW SESSION USER
AUTO_INCREMENT AUTOINCREMENT IDENTITY GENERATED ALWAYS ROW BY ROWS SEQUENCE NEXTVAL START VALUE
GRANT ADMIN MINUS PARTITION OVER WINDOW RANK DENSE_RANK ROW_NUMBER LAG LEAD PARTITION CLUSTER COPY COMMENT STORED
VIRTUAL GENERATED IMMUTABLE VOLATILE STRICT SECURITY DEFINER TEMPORARY TEMP GLOBAL LOCAL".Split(
            new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> TypeSet = new((
        "INT INTEGER SMALLINT BIGINT TINYINT MEDIUMINT FLOAT DOUBLE REAL DECIMAL NUMERIC BIT CHAR VARCHAR NCHAR NVARCHAR " +
        "TEXT NTEXT MEDIUMTEXT LONGTEXT TINYTEXT DATE TIME DATETIME DATETIME2 TIMESTAMP SMALLDATETIME BOOLEAN BOOL BLOB " +
        "CLOB JSON JSONB UUID BYTEA NUMBER VARCHAR2 NVARCHAR2 RAW BINARY VARBINARY MONEY SERIAL BIGSERIAL ENUM SET " +
        "GEOMETRY POINT LINESTRING POLYGON INTERVAL BYTE ARRAY").Split(' '),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>把 SQL 文本切分为着色区间；索引基于原文偏移，可逐 token 截取验证。</summary>
    public static List<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        if (string.IsNullOrEmpty(sql)) return tokens;
        int n = sql.Length, i = 0;
        while (i < n)
        {
            char c = sql[i];
            // -- 行注释
            if (c == '-' && i + 1 < n && sql[i + 1] == '-')
            {
                int j = sql.IndexOf('\n', i);
                if (j < 0) j = n;
                Push(tokens, i, j - i, TokenKind.Comment);
                i = j;
                continue;
            }
            // /* 块注释 */
            if (c == '/' && i + 1 < n && sql[i + 1] == '*')
            {
                int end = sql.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? n : end + 2;
                Push(tokens, i, end - i, TokenKind.Comment);
                i = end;
                continue;
            }
            // 字符串（'' 转义）
            if (c == '\'')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (sql[j] == '\'')
                    {
                        if (j + 1 < n && sql[j + 1] == '\'') { j += 2; continue; }
                        j++; break;
                    }
                    j++;
                }
                Push(tokens, i, Math.Min(j, n) - i, TokenKind.String);
                i = Math.Min(j, n);
                continue;
            }
            // 引号标识符（"x" / `x`，"" / `` 转义）
            if (c == '"' || c == '`')
            {
                int j = i + 1;
                while (j < n)
                {
                    if (sql[j] == c)
                    {
                        if (j + 1 < n && sql[j + 1] == c) { j += 2; continue; }
                        j++; break;
                    }
                    j++;
                }
                Push(tokens, i, Math.Min(j, n) - i, TokenKind.QuotedIdent);
                i = Math.Min(j, n);
                continue;
            }
            // 数字（含 0x 十六进制、小数、科学计数）
            if (char.IsDigit(c) || (c == '.' && i + 1 < n && char.IsDigit(sql[i + 1])))
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(sql[j]) || sql[j] == '.' || sql[j] == '_')) j++;
                if (j - 1 > i && (sql[j - 1] is 'e' or 'E') && j < n && (sql[j] == '+' || sql[j] == '-'))
                {
                    j++;
                    while (j < n && char.IsDigit(sql[j])) j++;
                }
                Push(tokens, i, j - i, TokenKind.Number);
                i = j;
                continue;
            }
            // 单词（标识符 / 关键字 / 类型）
            if (char.IsLetter(c) || c == '_' || c == '$' || c == '#')
            {
                int j = i + 1;
                while (j < n && (char.IsLetterOrDigit(sql[j]) || sql[j] == '_' || sql[j] == '$' || sql[j] == '#')) j++;
                string word = sql[i..j];
                Push(tokens, i, j - i,
                    KwSet.Contains(word) ? TokenKind.Keyword
                    : TypeSet.Contains(word) ? TokenKind.TypeName
                    : TokenKind.Plain);
                i = j;
                continue;
            }
            // 运算符
            if (c is '+' or '-' or '*' or '/' or '=' or '<' or '>' or '!' or '&' or '|' or '^' or '%' or '~' or '@' or '?')
            {
                int j = i + 1;
                while (j < n && sql[j] is '+' or '-' or '*' or '/' or '=' or '<' or '>' or '!' or '&' or '|' or '^' or '%') j++;
                Push(tokens, i, j - i, TokenKind.Operator);
                i = j;
                continue;
            }
            // 其余（空白、括号、逗号等）按普通处理
            int k = i + 1;
            while (k < n && !(sql[k] is '\'' or '"' or '`' or '-' or '/')
                   && !char.IsLetterOrDigit(sql[k]) && sql[k] is not ('_' or '$' or '#')
                   && !"+=*<>!&|^%~?".Contains(sql[k]))
                k++;
            Push(tokens, i, k - i, TokenKind.Plain);
            i = k;
        }
        return tokens;
    }

    /// <summary>提取 token 原文（测试与调试用）。</summary>
    public static string Text(string sql, SqlToken t) => sql.Substring(t.Start, t.Length);

    private static void Push(List<SqlToken> tokens, int start, int len, TokenKind kind)
    {
        if (len > 0) tokens.Add(new SqlToken(start, len, kind));
    }
}
