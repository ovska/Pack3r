﻿namespace Pack3r.Extensions;

public static class UtilityExtensions
{
    private const StringComparison cmp = StringComparison.OrdinalIgnoreCase;

    public static bool HasExtension(this string file, ReadOnlySpan<char> extension) => HasExtension(file.AsSpan(), extension);
    public static bool HasExtension(this ReadOnlyMemory<char> file, ReadOnlySpan<char> extension) => HasExtension(file.Span, extension);

    public static bool HasExtension(this ReadOnlySpan<char> file, ReadOnlySpan<char> extension)
    {
        return Path.GetExtension(file).Equals(extension, cmp);
    }

    public static ReadOnlySpan<char> GetExtension(this string file) => GetExtension(file.AsSpan());
    public static ReadOnlySpan<char> GetExtension(this ReadOnlyMemory<char> file) => GetExtension(file.Span);

    public static ReadOnlySpan<char> GetExtension(this ReadOnlySpan<char> file)
    {
        return Path.GetExtension(file);
    }

    public static bool EqualsF(this string value, ReadOnlySpan<char> other) => EqualsF(value.AsSpan(), other);
    public static bool EqualsF(this ReadOnlyMemory<char> value, ReadOnlySpan<char> other) => EqualsF(value.Span, other);

    public static bool EqualsF(this ReadOnlySpan<char> value, ReadOnlySpan<char> other)
    {
        return value.Equals(other, cmp);
    }

    public static bool StartsWithF(this string value, ReadOnlySpan<char> other) => StartsWithF(value.AsSpan(), other);
    public static bool StartsWithF(this ReadOnlyMemory<char> value, ReadOnlySpan<char> other) => StartsWithF(value.Span, other);

    public static bool StartsWithF(this ReadOnlySpan<char> value, ReadOnlySpan<char> other)
    {
        return value.StartsWith(other, cmp);
    }

    public static bool EndsWithF(this string value, ReadOnlySpan<char> other) => EndsWithF(value.AsSpan(), other);
    public static bool EndsWithF(this ReadOnlyMemory<char> value, ReadOnlySpan<char> other) => EndsWithF(value.Span, other);

    public static bool EndsWithF(this ReadOnlySpan<char> value, ReadOnlySpan<char> other)
    {
        return value.EndsWith(other, cmp);
    }
}
