using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Extensions;

public enum TextureExtension { Empty = 0, Other, Tga, Jpg }

public static class StringExtensions
{
    public static ReadOnlyMemory<char> ChangeExtension(this ReadOnlyMemory<char> file, ReadOnlySpan<char> extension)
    {
        int extensionLength = file.GetExtension().Length;

        var withoutExtension = file[..^extensionLength];

        if (extension.IsEmpty)
            return withoutExtension;

        return $"{withoutExtension.Span}{extension}".AsMemory();
    }

    public static TextureExtension GetTextureExtension(this ReadOnlyMemory<char> path) => GetTextureExtension(path.Span);
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

    public static ArraySegment<Range> Split(
        this ReadOnlyMemory<char> value,
        char separator,
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    {
        var ranges = new Range[32];

        int count = value.Span.Split(ranges.AsSpan(), separator, options);

        if (count == 0)
        {
            ranges[0] = Range.All;
            count = 1;
        }

        return new ArraySegment<Range>(ranges, 0, count);
    }

    public static bool TryReadUpToWhitespace(in this ReadOnlyMemory<char> value, out ReadOnlyMemory<char> token)
    {
        var span = value.Span;

        int space = span.IndexOfAny(Tokens.SpaceOrTab);

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
        var span = value.Span;

        int space = span.IndexOfAny(Tokens.SpaceOrTab);

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[space..].Trim();
        return true;
    }

    public static bool MatchPrefix(in this Line line, string prefix, out ReadOnlyMemory<char> remainder)
    {
        if (line.Value.StartsWithF(prefix.AsSpan()))
        {
            remainder = line.Value[prefix.Length..].Trim();
            return true;
        }

        remainder = default;
        return false;
    }

    public static (ReadOnlyMemory<char> key, ReadOnlyMemory<char> value) ReadKeyValue(in this Line line)
    {
        if (line.Value.Span.IndexOf("\" \"") is int index and >= 0)
        {
            return (
                line.Value[1..index],
                line.Value[(index + 3)..^1]);
        }

        ThrowForInvalidKVP(in line);
        return default;
    }

    private static void ThrowForInvalidKVP(in Line line)
    {
        throw new InvalidDataException($"Invalid key/value pair in {line.Path} on line {line.Index}: {line.Raw}");
    }
}
