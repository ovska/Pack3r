using System.Runtime.CompilerServices;
using Pack3r.IO;

namespace Pack3r.Extensions;

/// <summary>
/// Helpers for parsing lines as spans, without creating strings.
/// </summary>
/// <remarks>
/// Compare with string literals and <see cref="StringComparison.OrdinalIgnoreCase"/> directly at the call site
/// (<c>span.StartsWith("map", StringComparison.OrdinalIgnoreCase)</c>), so the JIT sees the constant and can unroll
/// the comparison.
/// </remarks>
public static class LineExtensions
{
    /// <summary>Whether the value contains the word followed by whitespace, anywhere. Case-sensitive.</summary>
    public static bool ContainsWordFollowedByWhitespace(this ReadOnlySpan<char> value, ReadOnlySpan<char> word)
    {
        int offset = 0;

        while (true)
        {
            int index = value[offset..].IndexOf(word, StringComparison.Ordinal);

            if (index < 0)
                return false;

            int end = offset + index + word.Length;

            if (end < value.Length && char.IsWhiteSpace(value[end]))
                return true;

            offset += index + 1;
        }
    }

    /// <summary>The value up to the first space or tab, trimmed. False if there is none.</summary>
    public static bool TryReadUpToWhitespace(this ReadOnlySpan<char> value, out ReadOnlySpan<char> token)
    {
        int space = value.IndexOfAny(' ', '\t');

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[..space].Trim();
        return true;
    }

    /// <summary>The value after the first space or tab, trimmed. False if there is none.</summary>
    public static bool TryReadPastWhitespace(this ReadOnlySpan<char> value, out ReadOnlySpan<char> token)
    {
        int space = value.IndexOfAny(' ', '\t');

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[space..].Trim();
        return true;
    }

    /// <summary>
    /// Whether the line's value starts with the keyword, ignoring case, followed by a space or a tab. The remainder
    /// is the rest of the line, trimmed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MatchKeyword(this in LineRef line, string keyword, out ReadOnlySpan<char> remainder)
    {
        ReadOnlySpan<char> value = line.Value;

        if (value.Length > keyword.Length &&
            value[keyword.Length] is ' ' or '\t' &&
            value.StartsWith(keyword, StringComparison.OrdinalIgnoreCase))
        {
            remainder = value[(keyword.Length + 1)..].Trim();
            return true;
        }

        remainder = default;
        return false;
    }

    public static ReadOnlySpan<char> TrimQuotes(this ReadOnlySpan<char> token)
    {
        _ = TryTrimQuotes(token, out token);
        return token;
    }

    public static bool TryTrimQuotes(this ReadOnlySpan<char> token, out ReadOnlySpan<char> trimmed)
    {
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
        {
            trimmed = token[1..^1];
            return true;
        }

        trimmed = token;
        return false;
    }

    /// <summary>
    /// Splits into tokens separated by whitespace, where a quoted token can contain whitespace. Same as matching
    /// <see cref="Tokens.WhitespaceSeparatedTokens"/>: quoted tokens include their quotes, and a quote without a
    /// closing one is skipped.
    /// </summary>
    public static TokenEnumerator EnumerateTokens(this ReadOnlySpan<char> value) => new(value);

    /// <summary>Reads a <c>"key" "value"</c> pair, both quoted.</summary>
    public static bool TryReadKeyValue(this ReadOnlySpan<char> line, out ReadOnlySpan<char> key, out ReadOnlySpan<char> value)
    {
        var tokens = line.EnumerateTokens();

        if (tokens.MoveNext() &&
            tokens.Current.TryTrimQuotes(out key) &&
            tokens.MoveNext() &&
            tokens.Current.TryTrimQuotes(out value) &&
            !tokens.MoveNext())
        {
            return true;
        }

        key = default;
        value = default;
        return false;
    }

    /// <inheritdoc cref="TryReadKeyValue(ReadOnlySpan{char}, out ReadOnlySpan{char}, out ReadOnlySpan{char})"/>
    /// <exception cref="InvalidDataException">The line isn't a key/value pair</exception>
    public static void ReadKeyValue(this in LineRef line, out ReadOnlySpan<char> key, out ReadOnlySpan<char> value)
    {
        if (!line.Value.TryReadKeyValue(out key, out value))
            throw new InvalidDataException($"Invalid key/value pair in {line.Path} on line {line.Index}: {line.Raw}");
    }

    /// <summary>Creates a string to keep.</summary>
    public static QString ToQString(this ReadOnlySpan<char> value) => new(value.ToString());

    /// <summary>Creates a string to keep.</summary>
    public static QPath ToQPath(this ReadOnlySpan<char> value) => new(value.ToString());
}

/// <summary>See <see cref="LineExtensions.EnumerateTokens"/>.</summary>
public ref struct TokenEnumerator(ReadOnlySpan<char> value)
{
    private readonly ReadOnlySpan<char> _value = value;
    private int _position;

    public ReadOnlySpan<char> Current { get; private set; }

    public readonly TokenEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_position < _value.Length)
        {
            char c = _value[_position];

            if (char.IsWhiteSpace(c))
            {
                _position++;
                continue;
            }

            if (c == '"')
            {
                int close = _value[(_position + 1)..].IndexOf('"');

                if (close < 0)
                {
                    // no closing quote: skipped, like the regex does
                    _position++;
                    continue;
                }

                Current = _value.Slice(_position, close + 2);
                _position += close + 2;
                return true;
            }

            int end = _position + 1;

            while (end < _value.Length && !char.IsWhiteSpace(_value[end]) && _value[end] != '"')
                end++;

            Current = _value[_position..end];
            _position = end;
            return true;
        }

        Current = default;
        return false;
    }
}
