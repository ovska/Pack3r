using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pack3r.Extensions;

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

    public static bool TryPickOne<TState, TItem>(
        TState state,
        TItem a,
        TItem b,
        Func<TState, TItem, bool> predicate,
        [NotNullWhen(true)] out TItem item)
    {
        var fora = predicate(state, a);
        var forb = predicate(state, b);

        if (fora == forb)
        {
            item = default!;
            return false;
        }

        item = (fora ? a : b)!;
        return true;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Text")]
    internal static extern ReadOnlySpan<char> GetInternalBuffer(this ref DefaultInterpolatedStringHandler @this);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Clear")]
    internal static extern void Clear(this ref DefaultInterpolatedStringHandler @this);
}
