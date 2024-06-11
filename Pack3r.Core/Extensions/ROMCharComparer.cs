using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Pack3r.Extensions;

public sealed class ROMCharComparer : IEqualityComparer<ReadOnlyMemory<char>>, IComparer<ReadOnlyMemory<char>>
{
    public static readonly ROMCharComparer Instance = new();

    private ROMCharComparer() { }

    public bool Equals(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        Assert(x);
        Assert(y);
        return x.Span.Equals(y.Span, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode([DisallowNull] ReadOnlyMemory<char> obj)
    {
        Assert(obj);
        return CultureInfo.InvariantCulture.CompareInfo.GetHashCode(obj.Span, CompareOptions.OrdinalIgnoreCase);
    }

    public int Compare(ReadOnlyMemory<char> x, ReadOnlyMemory<char> y)
    {
        Assert(x);
        Assert(y);
        return CultureInfo.InvariantCulture.CompareInfo.Compare(x.Span, y.Span, CompareOptions.OrdinalIgnoreCase);
    }

    [Conditional("DEBUG")]
    private static void Assert(ReadOnlyMemory<char> mem)
    {
        if (mem.Span.Contains('\\'))
        {
            Debug.Fail($"ROMCharComparer called with non-normalized value: '{mem}'");
        }
    }
}
