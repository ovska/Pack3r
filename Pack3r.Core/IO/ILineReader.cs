namespace Pack3r.IO;

public readonly record struct LineOptions(
    bool KeepEmpty = false,
    bool KeepRaw = false);

public interface ILineReader
{
    IAsyncEnumerable<Line> ReadLines(
        ResourcePath path,
        LineOptions options,
        CancellationToken cancellationToken);
}
