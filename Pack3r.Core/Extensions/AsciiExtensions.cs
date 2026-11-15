using System.Text;
using Pack3r.IO;

namespace Pack3r.Extensions;

/// <summary>
/// Helpers for parsing ASCII bytes without creating strings.
/// </summary>
/// <remarks>
/// Bytes outside ASCII are kept as is while parsing, and become '?' when turned into text, same as reading the
/// file with an ASCII <see cref="StreamReader"/>.
/// </remarks>
public static class AsciiExtensions
{
    /// <summary>Removes leading and trailing ASCII whitespace (tab, line breaks, vertical tab, form feed, space).</summary>
    public static ReadOnlySpan<byte> TrimWhitespace(this ReadOnlySpan<byte> value) => value[Ascii.Trim(value)];

    public static bool EqualsIgnoreCase(this ReadOnlySpan<byte> value, ReadOnlySpan<byte> other)
        => Ascii.EqualsIgnoreCase(value, other);

    public static bool StartsWithIgnoreCase(this ReadOnlySpan<byte> value, ReadOnlySpan<byte> prefix)
        => value.Length >= prefix.Length && Ascii.EqualsIgnoreCase(value[..prefix.Length], prefix);

    public static bool EndsWithIgnoreCase(this ReadOnlySpan<byte> value, ReadOnlySpan<byte> suffix)
        => value.Length >= suffix.Length && Ascii.EqualsIgnoreCase(value[^suffix.Length..], suffix);

    /// <summary>Whether the value contains the word followed by whitespace, anywhere. Case-sensitive.</summary>
    public static bool ContainsWordFollowedByWhitespace(this ReadOnlySpan<byte> value, ReadOnlySpan<byte> word)
    {
        int offset = 0;

        while (true)
        {
            int index = value[offset..].IndexOf(word);

            if (index < 0)
                return false;

            int end = offset + index + word.Length;

            if (end < value.Length && IsWhitespace(value[end]))
                return true;

            offset += index + 1;
        }
    }

    public static bool IsWhitespace(byte value) => value is (byte)' ' or (>= (byte)'\t' and <= (byte)'\r');

    /// <summary>The value up to the first space or tab, trimmed. False if there is none.</summary>
    public static bool TryReadUpToWhitespace(this ReadOnlySpan<byte> value, out ReadOnlySpan<byte> token)
    {
        int space = value.IndexOfAny((byte)' ', (byte)'\t');

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[..space].TrimWhitespace();
        return true;
    }

    /// <summary>The value after the first space or tab, trimmed. False if there is none.</summary>
    public static bool TryReadPastWhitespace(this ReadOnlySpan<byte> value, out ReadOnlySpan<byte> token)
    {
        int space = value.IndexOfAny((byte)' ', (byte)'\t');

        if (space == -1)
        {
            token = default;
            return false;
        }

        token = value[space..].TrimWhitespace();
        return true;
    }

    /// <summary>
    /// Whether the line's value starts with the keyword, ignoring case, followed by a space or a tab. The remainder
    /// is the rest of the line, trimmed.
    /// </summary>
    public static bool MatchKeyword(this in LineRef line, ReadOnlySpan<byte> keyword, out ReadOnlySpan<byte> remainder)
    {
        ReadOnlySpan<byte> value = line.Value;

        if (value.Length > keyword.Length &&
            value[keyword.Length] is (byte)' ' or (byte)'\t' &&
            Ascii.EqualsIgnoreCase(value[..keyword.Length], keyword))
        {
            remainder = value[(keyword.Length + 1)..].TrimWhitespace();
            return true;
        }

        remainder = default;
        return false;
    }

    public static ReadOnlySpan<byte> TrimQuotes(this ReadOnlySpan<byte> token)
    {
        _ = TryTrimQuotes(token, out token);
        return token;
    }

    public static bool TryTrimQuotes(this ReadOnlySpan<byte> token, out ReadOnlySpan<byte> trimmed)
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
    public static TokenEnumerator EnumerateTokens(this ReadOnlySpan<byte> value) => new(value);

    /// <summary>Reads a <c>"key" "value"</c> pair, both quoted.</summary>
    public static bool TryReadKeyValue(this ReadOnlySpan<byte> line, out ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value)
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

    /// <inheritdoc cref="TryReadKeyValue(ReadOnlySpan{byte}, out ReadOnlySpan{byte}, out ReadOnlySpan{byte})"/>
    /// <exception cref="InvalidDataException">The line isn't a key/value pair</exception>
    public static void ReadKeyValue(this in LineRef line, out ReadOnlySpan<byte> key, out ReadOnlySpan<byte> value)
    {
        if (!line.Value.TryReadKeyValue(out key, out value))
            throw new InvalidDataException($"Invalid key/value pair in {line.Path} on line {line.Index}: {line.Raw}");
    }

    /// <summary>Decodes as ASCII, bytes outside it become '?'. Allocates.</summary>
    public static string ToAsciiString(this ReadOnlySpan<byte> value) => value.IsEmpty ? "" : Encoding.ASCII.GetString(value);

    /// <summary>Creates a string to keep, see <see cref="ToAsciiString"/>.</summary>
    public static QString ToQString(this ReadOnlySpan<byte> value) => new(value.ToAsciiString());

    /// <summary>Creates a string to keep, see <see cref="ToAsciiString"/>.</summary>
    public static QPath ToQPath(this ReadOnlySpan<byte> value) => new(value.ToAsciiString());

    /// <summary>
    /// Decodes into the given buffer if it fits, e.g. to look up a value in a set of strings without creating one.
    /// </summary>
    public static ReadOnlySpan<char> ToChars(this ReadOnlySpan<byte> value, Span<char> buffer)
    {
        if (value.Length > buffer.Length)
            return value.ToAsciiString();

        int count = Encoding.ASCII.GetChars(value, buffer);
        return buffer[..count];
    }
}

/// <summary>See <see cref="AsciiExtensions.EnumerateTokens"/>.</summary>
public ref struct TokenEnumerator(ReadOnlySpan<byte> value)
{
    private readonly ReadOnlySpan<byte> _value = value;
    private int _position;

    public ReadOnlySpan<byte> Current { get; private set; }

    public readonly TokenEnumerator GetEnumerator() => this;

    public bool MoveNext()
    {
        while (_position < _value.Length)
        {
            byte c = _value[_position];

            if (AsciiExtensions.IsWhitespace(c))
            {
                _position++;
                continue;
            }

            if (c == '"')
            {
                int close = _value[(_position + 1)..].IndexOf((byte)'"');

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

            while (end < _value.Length && !AsciiExtensions.IsWhitespace(_value[end]) && _value[end] != '"')
                end++;

            Current = _value[_position..end];
            _position = end;
            return true;
        }

        Current = default;
        return false;
    }
}
