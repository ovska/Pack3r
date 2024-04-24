using System.Runtime.CompilerServices;
using Pack3r.IO;

namespace Pack3r.Tests;

internal sealed class StringLineReader(string data) : ILineReader
{
    public async IAsyncEnumerable<Line> ReadLines(
        string path,
        LineOptions options,
         [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.Yield();

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
