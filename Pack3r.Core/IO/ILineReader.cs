using Pack3r.Models;

namespace Pack3r.IO;

public interface ILineReader
{
    IEnumerable<Line> ReadRawLines(string path);

    IAsyncEnumerable<Line> ReadLines(
        string path,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Line> ReadLines(
        IAsset asset,
        CancellationToken cancellationToken);
}
