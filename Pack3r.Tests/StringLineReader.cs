using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

internal sealed class StringLineReader(string data) : ILineReader
{
    public IEnumerable<Line> ReadLines(string path, CancellationToken cancellationToken)
    {
        return Read(path, keepRaw: false);
    }

    public IEnumerable<Line> ReadLines(IAsset asset, CancellationToken cancellationToken)
    {
        return Read(asset.FullPath, keepRaw: false);
    }

    public IEnumerable<Line> ReadRawLines(string path)
    {
        return Read(path, keepRaw: true);
    }

    private IEnumerable<Line> Read(string path, bool keepRaw)
    {
        using var reader = new StringReader(data);

        string? line;
        int index = 0;

        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
                continue;

            var obj = new Line(path, ++index, line, keepRaw);

            if (obj.HasValue)
                yield return obj;
        }
    }
}
