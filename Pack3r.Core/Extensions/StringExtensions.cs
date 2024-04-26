using Pack3r.Extensions;

namespace Pack3r.Extensions;

public static class StringExtensions
{
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
        var span = line.Value.Span;

        if (span.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
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
