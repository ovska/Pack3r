using System.Runtime.CompilerServices;
using System.Text;

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
}

public class FSLineReader() : ILineReader
{
    public async IAsyncEnumerable<Line> ReadLines(
        string path,
        LineOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int index = 0;

        using var reader = new StreamReader(
                new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 4096,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

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
