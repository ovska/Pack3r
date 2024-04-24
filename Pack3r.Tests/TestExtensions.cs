namespace Pack3r.Tests;

public static class TestExtensions
{
    public static async Task<List<T>> ToList<T>(this IAsyncEnumerable<T> enumerable, CancellationToken cancellationToken = default)
    {
        var list = new List<T>();

        await foreach (var item in enumerable.WithCancellation(cancellationToken))
            list.Add(item);

        return list;
    }

    public static IEnumerable<string> AsStrings(this List<ReadOnlyMemory<char>> items)
    {
        return items.Select(i => i.ToString());
    }
}
