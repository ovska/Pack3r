using System.Runtime.CompilerServices;
using System.Text;
using Pack3r.Extensions;
using Pack3r.Models;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public IAsyncEnumerable<Line> ReadLines(
        string path,
        LineOptions options,
        CancellationToken cancellationToken)
    {
        FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return ReadLinesCore(path.NormalizePath(), stream, options, cancellationToken);
    }

    public IAsyncEnumerable<Line> ReadLines(IAsset asset, LineOptions options, CancellationToken cancellationToken)
    {
        return ReadLinesCore(asset.FullPath, asset.OpenRead(isAsync: true), options, cancellationToken);
    }

    private static async IAsyncEnumerable<Line> ReadLinesCore(
        string path,
        Stream source,
        LineOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int index = 0;

        using var reader = new StreamReader(
            source,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: false);

        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            index++;

            if (line.Length == 0 && !options.KeepEmpty)
                continue;

            var obj = new Line(path, index, line, options.KeepRaw);

            if (obj.HasValue || options.KeepEmpty)
                yield return obj;
        }
    }
}
