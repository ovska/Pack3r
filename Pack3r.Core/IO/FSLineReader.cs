using System.Runtime.CompilerServices;
using System.Text;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public async IAsyncEnumerable<Line> ReadLines(
        ResourcePath path,
        LineOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int index = 0;

        int bufferSize = Path.GetExtension(path.Path.AsSpan()).Equals(".map", StringComparison.OrdinalIgnoreCase)
            ? 4096 * 16
            : 4096;

        using var reader = new StreamReader(
                 path.Entry?.Open() ?? new FileStream(
                    path.Path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: bufferSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
        {
            if (line.Length == 0 && !options.KeepEmpty)
                continue;

            var obj = new Line(path.Path, ++index, line, options.KeepRaw);

            if (obj.HasValue || options.KeepEmpty)
                yield return obj;
        }
    }
}
