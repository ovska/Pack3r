using System.Runtime.CompilerServices;

namespace Pack3r.Extensions;

public static class UtilityExtensions
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Text")]
    internal static extern ReadOnlySpan<char> GetInternalBuffer(this ref DefaultInterpolatedStringHandler @this);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "Clear")]
    internal static extern void Clear(this ref DefaultInterpolatedStringHandler @this);
}
