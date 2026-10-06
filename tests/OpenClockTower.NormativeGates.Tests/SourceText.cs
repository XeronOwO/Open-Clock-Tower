using System.Text;

namespace OpenClockTower.NormativeGates.Tests;

/// <summary>
/// 源码文本清洗：去掉注释与字符串/字符字面量，只留下"真正会被编译的代码"。
/// </summary>
/// <remarks>
/// 门禁基于文本扫描，如果不先清洗，就会把注释里举例的 <c>DateTime.Now</c>
/// 当成真实调用报出来——假红会让门禁迅速失去信任，最后被关掉。
/// </remarks>
internal static class SourceText
{
    /// <summary>返回只保留代码骨架的文本（注释与字面量被替换为空占位）。</summary>
    internal static string StripCommentsAndLiterals(string source)
    {
        var builder = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                index = SkipToLineEnd(source, index);
                continue;
            }

            if (current == '/' && next == '*')
            {
                index = SkipBlockComment(source, index);
                continue;
            }

            if (current == '"' && next == '"' && index + 2 < source.Length && source[index + 2] == '"')
            {
                index = SkipRawString(source, index);
                builder.Append("\"\"");
                continue;
            }

            if (current == '@' && next == '"')
            {
                index = SkipVerbatimString(source, index);
                builder.Append("\"\"");
                continue;
            }

            if (current == '"')
            {
                index = SkipQuoted(source, index, '"');
                builder.Append("\"\"");
                continue;
            }

            if (current == '\'')
            {
                index = SkipQuoted(source, index, '\'');
                builder.Append("''");
                continue;
            }

            builder.Append(current);
            index++;
        }

        return builder.ToString();
    }

    /// <summary>
    /// 只去掉注释，**保留字符串 / 字符字面量**的文本。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="StripCommentsAndLiterals"/> 的分工：那条判"代码里有没有出现某个调用"
    /// （注释与字面量都不可信，两边都要清）；这条判"某段 **SQL / 配置文本**有没有出现在源码里"——
    /// 那里的内容恰恰写在字面量里（原生字符串里的 DDL 就是），清洗掉字面量等于把门禁变成瞎子。
    /// 保留字面量带来的噪音（注释里举例的同一段文本）由这条先清掉，两边就只剩"真的写了"这一种命中。
    /// </remarks>
    internal static string StripComments(string source)
    {
        var builder = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (current == '/' && next == '/')
            {
                index = SkipToLineEnd(source, index);
                continue;
            }

            if (current == '/' && next == '*')
            {
                index = SkipBlockComment(source, index);
                continue;
            }

            // 字面量整段抄过去（含原生字符串与逐字字符串），它内部出现的 `//` 不算注释。
            var start = index;
            if (current == '"' && next == '"' && index + 2 < source.Length && source[index + 2] == '"')
            {
                index = SkipRawString(source, index);
            }
            else if (current == '@' && next == '"')
            {
                index = SkipVerbatimString(source, index);
            }
            else if (current == '"')
            {
                index = SkipQuoted(source, index, '"');
            }
            else if (current == '\'')
            {
                index = SkipQuoted(source, index, '\'');
            }
            else
            {
                builder.Append(current);
                index++;
                continue;
            }

            builder.Append(source, start, index - start);
        }

        return builder.ToString();
    }

    private static int SkipToLineEnd(string source, int index)
    {
        while (index < source.Length && source[index] != '\n')
        {
            index++;
        }

        return index;
    }

    private static int SkipBlockComment(string source, int index)
    {
        index += 2;
        while (index + 1 < source.Length && !(source[index] == '*' && source[index + 1] == '/'))
        {
            index++;
        }

        return Math.Min(index + 2, source.Length);
    }

    private static int SkipRawString(string source, int index)
    {
        index += 3;
        while (index + 2 < source.Length
               && !(source[index] == '"' && source[index + 1] == '"' && source[index + 2] == '"'))
        {
            index++;
        }

        return Math.Min(index + 3, source.Length);
    }

    private static int SkipVerbatimString(string source, int index)
    {
        index += 2;
        while (index < source.Length)
        {
            if (source[index] != '"')
            {
                index++;
                continue;
            }

            if (index + 1 < source.Length && source[index + 1] == '"')
            {
                index += 2;
                continue;
            }

            return index + 1;
        }

        return index;
    }

    private static int SkipQuoted(string source, int index, char delimiter)
    {
        index++;
        while (index < source.Length && source[index] != delimiter)
        {
            if (source[index] == '\\')
            {
                index++;
            }

            index++;
        }

        return Math.Min(index + 1, source.Length);
    }
}
