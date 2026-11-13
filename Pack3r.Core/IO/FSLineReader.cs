using System.Text;
using Pack3r.Models;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    // read in larger chunks than the default, the files are read start to end
    private const int BufferSize = 16 * 1024;

    public IEnumerable<Line> ReadRawLines(string path)
    {
        return Read(OpenFile(path), path, keepRaw: true, CancellationToken.None);
    }

    public IEnumerable<Line> ReadLines(string path, CancellationToken cancellationToken)
    {
        return Read(OpenFile(path), path, keepRaw: false, cancellationToken);
    }

    public IEnumerable<Line> ReadLines(IAsset asset, CancellationToken cancellationToken)
    {
        return Read(asset.OpenRead, asset.FullPath, keepRaw: false, cancellationToken);
    }

    private static Func<Stream> OpenFile(string path) => () => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 0, // the reader does the buffering
        FileOptions.SequentialScan);

    private static IEnumerable<Line> Read(Func<Stream> open, string path, bool keepRaw, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            open(),
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: BufferSize,
            leaveOpen: false);

        string? value;
        int index = 0;

        while ((value = reader.ReadLine()) is not null)
        {
            index++;

            if (value.Length == 0)
                continue;

            cancellationToken.ThrowIfCancellationRequested();

            Line line = new(path, index, value, keepRaw);

            if (line.HasValue)
                yield return line;
        }
    }
}
