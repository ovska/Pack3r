using System.Runtime.CompilerServices;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

internal sealed class StringLineReader(string data) : ILineReader
{
    public IAsyncEnumerable<Line> ReadLines(string path, LineOptions options, CancellationToken cancellationToken)
    {
        return ReadLinesCore(path, options, cancellationToken);
    }

    public IAsyncEnumerable<Line> ReadLines(IAsset asset, LineOptions options, CancellationToken cancellationToken)
    {
        return ReadLinesCore(asset.FullPath, options, cancellationToken);
    }

    private async IAsyncEnumerable<Line> ReadLinesCore(
        string path,
        LineOptions options,
         [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StringReader(data);

        string? line;
        int index = 0;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0 && !options.KeepEmpty)
                continue;

            var obj = new Line(path, ++index, line, options.KeepRaw);

            if (obj.HasValue || options.KeepEmpty)
                yield return obj;
        }
    }
}
