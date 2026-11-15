using Pack3r.Models;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public LineEnumerable ReadRawLines(string path, CancellationToken cancellationToken)
        => LineEnumerable.FromFile(path, keepRaw: true, cancellationToken);

    public LineEnumerable ReadLines(string path, CancellationToken cancellationToken)
        => LineEnumerable.FromFile(path, keepRaw: false, cancellationToken);

    public LineEnumerable ReadLines(IAsset asset, CancellationToken cancellationToken)
        => LineEnumerable.FromAsset(asset, keepRaw: false, cancellationToken);
}
