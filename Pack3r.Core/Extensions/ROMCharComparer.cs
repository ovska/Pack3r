using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Pack3r.Extensions;

public sealed class ROMCharComparer : IEqualityComparer<ReadOnlyMemory<char>>
{
    public static readonly ROMCharComparer Instance = new();

    private ROMCharComparer() { }

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return x.Length == y.Length && x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode([DisallowNull] ReadOnlyMemory<char> obj)
    {
        if (obj.IsEmpty)
            return 0;

        return CultureInfo.InvariantCulture.CompareInfo.GetHashCode(obj.Span, CompareOptions.IgnoreCase);
    }
}
