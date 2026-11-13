namespace Pack3r.Tests;

public static class TestExtensions
{
    public static IEnumerable<string> AsStrings(this List<ReadOnlyMemory<char>> items)
    {
        return items.Select(i => i.ToString());
    }

    public static IEnumerable<string> AsStrings(this List<QPath> items)
    {
        return items.Select(i => i.ToString());
    }
}
