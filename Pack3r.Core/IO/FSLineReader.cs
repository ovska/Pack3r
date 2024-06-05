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
            bufferSize: path.HasExtension(".map") ? 4096 * 16 : 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return ReadLinesCore(path.NormalizePath(), stream, options, cancellationToken);
    }

    public IAsyncEnumerable<Line> ReadLines(IAsset asset, LineOptions options, CancellationToken cancellationToken)
    {
        return ReadLinesCore(asset.FullPath, asset.OpenRead(), options, cancellationToken);
    }

    private static async IAsyncEnumerable<Line> ReadLinesCore(
        string path,
        Stream source,
        LineOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int index = 0;

        await using (source)
        {
            using var reader = new StreamReader(
                source,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                bufferSize: 4096);

            string? line;

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
}
