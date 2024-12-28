using System.Runtime.CompilerServices;
using System.Threading;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

internal sealed class StringLineReader(string data) : ILineReader
{
    public IAsyncEnumerable<Line> ReadLines(string path, CancellationToken cancellationToken)
    {
        return ReadLinesCore(path, cancellationToken);
    }

    public IAsyncEnumerable<Line> ReadLines(IAsset asset, CancellationToken cancellationToken)
    {
        return ReadLinesCore(asset.FullPath, cancellationToken);
    }

    public IEnumerable<Line> ReadRawLines(string path)
    {
        using var reader = new StringReader(data);

        string? line;
        int index = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            var obj = new Line(path, ++index, line, true);

            if (obj.HasValue)
                yield return obj;
        }
    }

    private async IAsyncEnumerable<Line> ReadLinesCore(
        string path,
         [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StringReader(data);

        string? line;
        int index = 0;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0)
                continue;

            var obj = new Line(path, ++index, line, false);

            if (obj.HasValue)
                yield return obj;
        }
    }
}
