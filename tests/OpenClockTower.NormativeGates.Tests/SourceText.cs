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
