namespace Pack3r;

public static class Extensions
{
    public static IEnumerable<ReadOnlyMemory<char>> SplitWords(
        this ReadOnlyMemory<char> value,
        StringSplitOptions options = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    {
        var ranges = new Range[32];

        int count = value.Span.Split(ranges.AsSpan(), ' ', options);

        if (count == 0)
        {
            yield return value;
        }
        else
        {
            foreach (var range in new ArraySegment<Range>(ranges, 0, count))
            {
                yield return value[range];
            }
        }
    }

    public static bool TryReadUpToWhitespace(in this ReadOnlyMemory<char> value, out ReadOnlyMemory<char> token)
    {
        var span = value.Span;

        int space = span.IndexOf(' ');

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

        int space = span.IndexOf(' ');

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
}
