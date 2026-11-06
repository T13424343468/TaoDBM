using System.Collections.Generic;

namespace TaoDBM.Core;

/// <summary>
/// Tao 系列主题三象（亮度阶梯定律，2026-11-01 用户统一指示；天青 2026-11-06 用户定稿对齐 TaoAgent 色板）：
///   水墨 ink（默认，浅色为主）· 天青 gray（釉底浅调）· 墨夜 night（以黑为主）。
/// 天青与水墨同为浅色象，亮度带内次序保持水墨>天青>墨夜单调递减；WCAG 体检入测试层（正文 4.5/次要 3.5/点缀 3.0/
/// 描边 2.0/按钮字 4.0）。旧 "ant"（清爽蓝）退役，读取时映射为天青。
/// 只存色值不依赖 WinUI，便于四层测试；画刷实例由 UI 侧维护，运行时改 Color 即全窗口生效。
/// </summary>
public static class ThemePalette
{
    /// <summary>取色键全集（XAML 资源键与代码画刷共用同名；沿用 Ant* 历史键名）。</summary>
    public static readonly string[] Keys =
    {
        "AntPrimary", "AntPrimaryDark", "AntBg", "AntCard", "AntBorder",
        "AntText", "AntTextSub", "AntSuccess", "AntWarn", "AntError",
        "AntHeadBg", "AntAltRow",
    };

    /// <summary>主题名全集：0=水墨 1=天青 2=墨夜（系列编号语义统一；内部名沿用历史 "gray"，落盘兼容）。</summary>
    public static readonly string[] Names = { "ink", "gray", "night" };

    /// <summary>水墨（默认，浅色为主）：暖宣底 #F7F5EE + 青灰点缀 #52708A，文字中性墨避免暖残。</summary>
    public static IReadOnlyDictionary<string, string> Ink { get; } = new Dictionary<string, string>
    {
        ["AntPrimary"] = "#52708A",
        ["AntPrimaryDark"] = "#3D5568",
        ["AntBg"] = "#F7F5EE",
        ["AntCard"] = "#FCFAF5",
        ["AntBorder"] = "#B0ACA0",
        ["AntText"] = "#292927",
        ["AntTextSub"] = "#75736E",
        ["AntSuccess"] = "#5A7D5A",
        ["AntWarn"] = "#C2943B",
        ["AntError"] = "#B5484D",
        ["AntHeadBg"] = "#EFECE3",
        ["AntAltRow"] = "#F8F6F0",
    };

    /// <summary>天青（釉底浅调，TaoAgent 天青色板同源 2026-11-06）：天青釉底 #EDF2EF（G&gt;B&gt;R）、
    /// 卡片面 #FAFCFA 亮于底、墨青文字 #2B3A36/#64746E、青瓷点缀 #34655C、
    /// 描边 #8FA39A（较 TaoAgent 的浅釉描边加深一档，过 WCAG 描边 2.0）。</summary>
    public static IReadOnlyDictionary<string, string> Gray { get; } = new Dictionary<string, string>
    {
        ["AntPrimary"] = "#34655C",
        ["AntPrimaryDark"] = "#2C534C",
        ["AntBg"] = "#EDF2EF",
        ["AntCard"] = "#FAFCFA",
        ["AntBorder"] = "#8FA39A",
        ["AntText"] = "#2B3A36",
        ["AntTextSub"] = "#64746E",
        ["AntSuccess"] = "#1F6E4A",
        ["AntWarn"] = "#A06500",
        ["AntError"] = "#A83030",
        ["AntHeadBg"] = "#E1E9E4",
        ["AntAltRow"] = "#F3F8F4",
    };

    /// <summary>墨夜（以黑为主，TaoZip v59 母本）：近黑底 #131211（R&gt;G&gt;B 暖序）、暖炭控件面 #211E1B、
    /// 宣纸白 #E8E3D5/#ABA595、黛青 #6FA3BC 点缀、描边 #5C574C。</summary>
    public static IReadOnlyDictionary<string, string> Night { get; } = new Dictionary<string, string>
    {
        ["AntPrimary"] = "#6FA3BC",
        ["AntPrimaryDark"] = "#54809A",
        ["AntBg"] = "#131211",
        ["AntCard"] = "#211E1B",
        ["AntBorder"] = "#5C574C",
        ["AntText"] = "#E8E3D5",
        ["AntTextSub"] = "#ABA595",
        ["AntSuccess"] = "#7FA97C",
        ["AntWarn"] = "#C9A35B",
        ["AntError"] = "#C4716B",
        ["AntHeadBg"] = "#181614",
        ["AntAltRow"] = "#1B1917",
    };

    /// <summary>按主题名取配色；未知名字一律回落水墨默认。</summary>
    public static IReadOnlyDictionary<string, string> Get(string? name) =>
        name == null ? Ink : Normalize(name) switch
        {
            "gray" => Gray,
            "night" => Night,
            _ => Ink,
        };

    /// <summary>主题名规范化：未知值回落 "ink"；旧 "ant"（清爽蓝）退役，映射为 "gray" 天青。</summary>
    public static string Normalize(string? name)
    {
        var v = (name ?? "").Trim().ToLowerInvariant();
        foreach (var n in Names) if (v == n) return n;
        if (v == "ant") return "gray";
        return "ink";
    }
}
