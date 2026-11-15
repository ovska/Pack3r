using System.Text;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

internal sealed class StringLineReader(string data) : ILineReader
{
    public LineEnumerable ReadLines(string path, CancellationToken cancellationToken)
        => Read(path, keepRaw: false, cancellationToken);

    public LineEnumerable ReadLines(IAsset asset, CancellationToken cancellationToken)
        => Read(asset.FullPath, keepRaw: false, cancellationToken);

    public LineEnumerable ReadRawLines(string path, CancellationToken cancellationToken)
        => Read(path, keepRaw: true, cancellationToken);

    private LineEnumerable Read(string path, bool keepRaw, CancellationToken cancellationToken)
        => LineEnumerable.FromStream(new MemoryStream(Encoding.ASCII.GetBytes(data)), path, keepRaw, cancellationToken);
}
