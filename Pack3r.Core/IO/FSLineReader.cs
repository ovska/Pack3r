using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Text;
using Pack3r.Extensions;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public IAsyncEnumerable<Line> ReadLines(
        string archivePath,
        ZipArchiveEntry entry,
        LineOptions options,
        CancellationToken cancellationToken)
    {
        return ReadLinesCore(archivePath, entry.Open(), options, cancellationToken);
    }

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

        return ReadLinesCore(path, stream, options, cancellationToken);
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
