using System.IO;
using System.Text.RegularExpressions;

namespace MusicDownloader.Services;

/// <summary>
/// 文件名清洗服务（纯静态工具类）
/// 职责：从 DownloadTask.CleanFileNamePart / AudioFileService.ParseFileName / SanitizeFileName 抽离
/// </summary>
public static partial class FileNameCleanerService
{
    // ════════════════════ 预编译正则池 ════════════════════

    // 4. 元数据标注后缀（TVB/颁奖 → DJ → 演唱会 → 运营商/个人标签 → 年份）
    //    静态预编译：一次编译，多次匹配，避免每次 Clean() 调用都重新编译
    //    注意：verbatim string(@"") 中 \s 等正则元字符无需双重转义
    private static readonly Regex[] _metaPatterns =
    [
        // 年份 + 场景组合（先处理组合，再处理单独年份）
        MetaRx(@"\s+\d{4}\s+TVB[^\s\)】)]*\s*$"),
        MetaRx(@"\s+\d{4}\s+现场版\s*$"),
        MetaRx(@"\s+\d{4}\s+颁奖典礼[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s+\d{4}\s+演唱会[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s+\d{4}\s+\([^)]+\)\s*$"),
        // TVB / 现场 / 颁奖（来源标注，外层）
        MetaRx(@"\s*\(?\s*TVB[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*颁奖典礼[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*现场版\)?\s*$"),
        // 版本类型
        MetaRx(@"\s*\(?\s*Live[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*Remix[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*KTV[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*MV[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*卡拉OK\)?\s*$"),
        MetaRx(@"\s*\(?\s*器乐版\)?\s*$"),
        MetaRx(@"\s*\(?\s*独奏版\)?\s*$"),
        MetaRx(@"\s*\(?\s*合辑\)?\s*$"),
        MetaRx(@"\s*\(?\s*伴奏\)?\s*$"),
        MetaRx(@"\s*\(?\s*纯音乐\)?\s*$"),
        // DJ 类
        MetaRx(@"\s*\(?\s*DJ[\s\.·\-]*[^\s\)】)]*\)?\s*$"),
        // 演唱会 / 综艺
        MetaRx(@"\s*\(?\s*演唱会[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*综艺[^\s\)】)]*\)?\s*$"),
        // 运营商 / 平台后缀
        MetaRx(@"\s*\(?\s*手机加油站\)?\s*$"),
        MetaRx(@"\s*\(?\s*手机铃声\)?\s*$"),
        MetaRx(@"\s*\(?\s*中国移动[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*中国联通[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*中国电信[^\s\)】)]*\)?\s*$"),
        MetaRx(@"\s*\(?\s*彩铃[^\s\)】)]*\)?\s*$"),
        // 个人化标签（括号内含关键词）
        MetaRx(@"\s*\([^)]*(?:专属|定制|私人订制)[^)]*\)\s*$"),
        // 个人化标签（无括号，末尾）
        MetaRx(@"\s*[^\s\)】)]*(?:专属|定制|私人订制)\)?\s*$"),
    ];

    // Unicode escape 序列解码（\uXXXX）
    [GeneratedRegex(@"\\u([0-9a-fA-F]{4})")]
    private static partial Regex UnicodeEscapeRx();

    // 空格归一化：多个空白字符 → 单空格（热路径，每次 Clean 调用都用到）
    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRx();

    // 年份后缀（独立年份）
    [GeneratedRegex(@"\s+\d{4}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YearSuffix1Rx();

    [GeneratedRegex(@"[-]+\d{4}\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YearSuffix2Rx();

    [GeneratedRegex(@"\(\d{4}\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex YearSuffix3Rx();

    // 辅助：构造预编译正则（忽略大小写 + 编译）
    private static Regex MetaRx(string pattern)
        => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ════════════════════ 清洗规则 ════════════════════

    /// <summary>
    /// 清洗文件名组件（歌手/歌名）
    /// 1. 解码 HTML entity 和 Unicode escape（\u0026）
    /// 2. 去掉转义反斜杠（\&amp; → &amp;，\\ → \）
    /// 3. 按从外到内顺序剥离元数据标签（TVB/颁奖 → DJ → 演唱会 → 运营商/个人标签 → 年份）
    /// 4. 去除歌名括号内与歌手/歌名重复的内容（如"放逐(放逐 张学友)"→"放逐"）
    /// 5. 修复截断的括号（末尾孤立左括号无右括号则删除）
    /// 6. 去除多余空格和首尾分隔符
    /// </summary>
    public static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";

        // 1. Unicode escape 序列解码（\uXXXX）
        s = UnicodeEscapeRx().Replace(s,
            m => ((char)int.Parse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber)).ToString());

        // 2. HTML entity 解码
        s = System.Net.WebUtility.HtmlDecode(s);
        s = s.Replace("&nbsp;", " ");

        // 3. 去掉转义反斜杠（一次遍历完成全部替换）
        s = s.Replace("\\&", "&")
             .Replace("\\\\", "\\")
             .Replace("\\>", ">")
             .Replace("\\<", "<")
             .Replace("\\\"", "\"")
             .Replace("\\'", "'");

        // 4. 元数据标注后缀（预编译正则，一次编译，多次匹配）
        foreach (var rx in _metaPatterns)
            s = rx.Replace(s, "").Trim();

        // 5. 年份后缀（独立年份）
        s = YearSuffix1Rx().Replace(s, "").Trim();
        s = YearSuffix2Rx().Replace(s, "").Trim();
        s = YearSuffix3Rx().Replace(s, "").Trim();

        // 6. 修复截断的括号：末尾孤立左括号（无配对右括号）
        s = FixTruncatedParen(s);

        // 7. 去除多余空格和首尾分隔符
        s = WhitespaceRx().Replace(s, " ").Trim(' ', '-', '_');
        return s;
    }

    /// <summary>
    /// 修复截断的括号：若字符串末尾存在孤立左括号（有内容无右括号），则删除该括号及内容。
    /// </summary>
    public static string FixTruncatedParen(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;

        var escaped = s.Replace("\\(", "").Replace("\\)", "");
        int left = 0, right = 0;
        foreach (char c in escaped)
        {
            if (c == '(') left++;
            else if (c == ')') right++;
        }
        if (left <= right) return s;

        int diff = left - right;
        int unmatchedLeftCount = 0;
        int cutPos = s.Length;

        for (int i = s.Length - 1; i >= 0; i--)
        {
            char c = s[i];
            if (c == '(' && (i == 0 || s[i - 1] != '\\'))
            {
                unmatchedLeftCount++;
                if (unmatchedLeftCount >= diff)
                {
                    cutPos = i;
                    break;
                }
            }
        }
        return s.Substring(0, cutPos).Trim();
    }

    /// <summary>
    /// 去除歌名中括号内与歌手/歌名重复的内容
    /// 例如: "放逐(放逐 张学友)" → "放逐"
    /// </summary>
    public static string RemoveTitleParenRepeat(string title, string artist)
    {
        var artists = artist.Split(new[] { '&', ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(a => a.Trim())
                            .Where(a => !string.IsNullOrEmpty(a))
                            .ToList();

        // 第一步：去掉包含歌手名的括号
        foreach (var ar in artists)
        {
            var pattern = @"\([^\)]*" + Regex.Escape(ar) + @"[^\)]*\)\s*$";
            var prev = "";
            while (prev != title && !string.IsNullOrEmpty(title))
            {
                prev = title;
                title = Regex.Replace(title, pattern, "", RegexOptions.IgnoreCase).Trim();
            }
        }

        // 第二步：去掉括号内容等于歌名去掉括号后内容的括号
        if (!string.IsNullOrEmpty(title))
        {
            var baseTitle = Regex.Replace(title, @"\([^)]*\)", "").Trim();
            if (!string.IsNullOrEmpty(baseTitle))
            {
                var pattern = @"\(" + Regex.Escape(baseTitle) + @"\)\s*$";
                title = Regex.Replace(title, pattern, "").Trim();
            }
        }
        return title;
    }

    // ════════════════════ 文件名解析 ════════════════════

    /// <summary>解析文件名为歌手和歌名</summary>
    public static void ParseFileName(string nameNoExt, out string artist, out string title)
    {
        var idx = nameNoExt.IndexOf(" - ", StringComparison.Ordinal);
        if (idx > 0)
        {
            artist = nameNoExt.Substring(0, idx).Trim();
            title = nameNoExt.Substring(idx + 3).Trim();
            return;
        }
        var dashIdx = Regex.Match(nameNoExt, @"(?<=[^\d])-(?=[^\d])");
        if (dashIdx.Success)
        {
            artist = nameNoExt.Substring(0, dashIdx.Index).Trim();
            title = nameNoExt.Substring(dashIdx.Index + 1).Trim();
            return;
        }
        var uIdx = nameNoExt.IndexOf('_');
        if (uIdx > 0)
        {
            artist = nameNoExt.Substring(0, uIdx).Trim();
            title = nameNoExt.Substring(uIdx + 1).Trim();
            return;
        }
        artist = "";
        title = nameNoExt;
    }

    /// <summary>修复文件名中的非法字符（Windows）</summary>
    public static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        if (name.Length > 180) name = name.Substring(0, 180);
        return name.Trim();
    }

    /// <summary>
    /// 生成清洗后的文件名（不含扩展名）
    /// 支持用户传入自定义目标文件名，若为空则自动从歌手/歌名生成
    /// </summary>
    public static string GenerateCleanFileName(string? customFileName, string artist, string title)
    {
        if (!string.IsNullOrWhiteSpace(customFileName))
            return customFileName.Trim();

        var cleanArtist = Clean(artist);
        var cleanTitle = Clean(title);
        cleanTitle = RemoveTitleParenRepeat(cleanTitle, cleanArtist);

        if (!string.IsNullOrEmpty(cleanArtist) && !string.IsNullOrEmpty(cleanTitle))
            return $"{cleanArtist} - {cleanTitle}";
        if (!string.IsNullOrEmpty(cleanArtist))
            return cleanArtist;
        return cleanTitle;
    }
}
