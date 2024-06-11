using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Pack3r.Extensions;

[Obsolete("Use QString or QPath", true)]
public sealed class ROMCharComparer : IEqualityComparer<ReadOnlyMemory<char>>, IComparer<ReadOnlyMemory<char>>
{
    public static readonly ROMCharComparer Instance = new();

    private ROMCharComparer() { }

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode([DisallowNull] ReadOnlyMemory<char> obj)
    {
        return CultureInfo.InvariantCulture.CompareInfo.GetHashCode(obj.Span, CompareOptions.OrdinalIgnoreCase);
    }

    public int Compare(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        return CultureInfo.InvariantCulture.CompareInfo.Compare(x.Span, y.Span, CompareOptions.OrdinalIgnoreCase);
    }
}
