using Pack3r.Models;

namespace Pack3r.IO;

public readonly record struct LineOptions(
    bool KeepEmpty = false,
    bool KeepRaw = false);

public interface ILineReader
{
    IAsyncEnumerable<Line> ReadLines(
        string path,
        LineOptions options,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Line> ReadLines(
        IAsset asset,
        LineOptions options,
        CancellationToken cancellationToken);
}
