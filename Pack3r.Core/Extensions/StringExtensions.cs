using System.Text.RegularExpressions;

namespace Pack3r.Extensions;

public enum TextureExtension { Empty = 0, Other, Tga, Jpg }

public static class StringExtensions
{
    public static string NormalizePath(this string path) => path.Replace(Path.DirectorySeparatorChar, '/');

    public static TextureExtension GetTextureExtension(this string path) => GetTextureExtension(path.AsSpan());
    public static TextureExtension GetTextureExtension(this ReadOnlySpan<char> path)
    {
        ReadOnlySpan<char> extension = path.GetExtension();

        if (extension.IsEmpty)
            return TextureExtension.Empty;

        if (extension.EqualsF(".tga"))
            return TextureExtension.Tga;

        if (extension.EqualsF(".jpg"))
            return TextureExtension.Jpg;

        return TextureExtension.Other;
    }

    public static bool TryReadUpToWhitespace(in this ReadOnlyMemory<char> value, out ReadOnlyMemory<char> token)
    {
        int space = value.Span.IndexOfAny(Tokens.SpaceOrTab);

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[..space].Trim();
        return true;
    }

    public static bool TryReadPastWhitespace(in this ReadOnlyMemory<char> value, out ReadOnlyMemory<char> token)
    {
        int space = value.Span.IndexOfAny(Tokens.SpaceOrTab);

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[space..].Trim();
        return true;
    }

    public static ReadOnlyMemory<char> TrimQuotes(this ReadOnlyMemory<char> token)
    {
        _ = TryTrimQuotes(token, out token);
        return token;
    }

    public static bool TryTrimQuotes(this ReadOnlyMemory<char> token, out ReadOnlyMemory<char> trimmed)
    {
        if (token.Length >= 2)
        {
            var keySpan = token.Span;

            if (keySpan[0] == '"' &&
                keySpan[^1] == '"')
            {
                trimmed = token[1..^1];
                return true;
            }
        }


        trimmed = token;
        return false;
    }

    public static bool TryReadKeyValue(this ReadOnlyMemory<char> line, out (ReadOnlyMemory<char> key, ReadOnlyMemory<char> value) kvp)
    {
        var enumerator = Tokens.WhitespaceSeparatedTokens().EnumerateMatches(line.Span);

        if (enumerator.MoveNext())
        {
            var key = line.Slice(enumerator.Current);

            if (key.TryTrimQuotes(out var keyTrimmed) &&
                enumerator.MoveNext())
            {
                var value = line.Slice(enumerator.Current);

                if (value.TryTrimQuotes(out var valueTrimmed) &&
                    !enumerator.MoveNext())
                {
                    kvp = (keyTrimmed, valueTrimmed);
                    return true;
                }
            }
        }

        kvp = default;
        return false;
    }

    public static ReadOnlyMemory<char> Slice(this ReadOnlyMemory<char> token, ValueMatch match)
    {
        return token.Slice(match.Index, match.Length);
    }
}
