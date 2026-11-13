using System.Buffers;
using System.Text;
using Pack3r.Models;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public const long DefaultWholeFileThreshold = 10 * 1024 * 1024;

    /// <summary>
    /// Files up to this size are read into memory at once, and their lines are slices of one string instead of a
    /// string each. Larger files are streamed line by line.
    /// </summary>
    public long WholeFileThreshold { get; init; } = DefaultWholeFileThreshold;

    // read in larger chunks than the default, the files are read start to end
    private const int BufferSize = 16 * 1024;

    public IEnumerable<Line> ReadRawLines(string path)
    {
        return Read(OpenFile(path), GetLength(path), path, keepRaw: true, CancellationToken.None);
    }

    public IEnumerable<Line> ReadLines(string path, CancellationToken cancellationToken)
    {
        return Read(OpenFile(path), GetLength(path), path, keepRaw: false, cancellationToken);
    }

    public IEnumerable<Line> ReadLines(IAsset asset, CancellationToken cancellationToken)
    {
        return Read(asset.OpenRead, asset.Length, asset.FullPath, keepRaw: false, cancellationToken);
    }

    private static long GetLength(string path) => new FileInfo(path).Length;

    private static Func<Stream> OpenFile(string path) => () => new FileStream(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 0, // the reader does the buffering
        FileOptions.SequentialScan);

    private IEnumerable<Line> Read(
        Func<Stream> open,
        long length,
        string path,
        bool keepRaw,
        CancellationToken cancellationToken)
    {
        return length <= WholeFileThreshold
            ? ReadWhole(open, length, path, keepRaw, cancellationToken)
            : ReadStreaming(open, path, keepRaw, cancellationToken);
    }

    private static IEnumerable<Line> ReadWhole(
        Func<Stream> open,
        long length,
        string path,
        bool keepRaw,
        CancellationToken cancellationToken)
    {
        string text = ReadAllText(open, length);

        int position = 0;
        int index = 0;

        // same line breaks as StreamReader.ReadLine: \n, \r\n and \r
        while (position < text.Length)
        {
            int lineLength = text.AsSpan(position).IndexOfAny('\r', '\n');
            int next;

            if (lineLength < 0)
            {
                lineLength = text.Length - position;
                next = text.Length;
            }
            else
            {
                int lineEnd = position + lineLength;
                next = lineEnd + (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n' ? 2 : 1);
            }

            index++;

            if (lineLength > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Line line = new(path, index, text.AsMemory(position, lineLength), keepRaw);

                if (line.HasValue)
                    yield return line;
            }

            position = next;
        }
    }

    /// <summary>
    /// Reads the whole stream as ASCII. Bytes outside ASCII become '?', same as with a <see cref="StreamReader"/>.
    /// </summary>
    private static string ReadAllText(Func<Stream> open, long lengthHint)
    {
        // +1 so a file that is exactly the hinted length is read to the end without growing the buffer
        byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Clamp(lengthHint + 1, 1, Array.MaxLength));
        int total = 0;

        try
        {
            using (var stream = open())
            {
                // read to the end instead of trusting the length, the file may have changed since it was listed
                int read;

                while ((read = stream.Read(buffer, total, buffer.Length - total)) > 0)
                {
                    total += read;

                    if (total == buffer.Length)
                    {
                        byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
                        buffer.AsSpan(0, total).CopyTo(larger);
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = larger;
                    }
                }
            }

            return Encoding.ASCII.GetString(buffer, 0, total);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IEnumerable<Line> ReadStreaming(
        Func<Stream> open,
        string path,
        bool keepRaw,
        CancellationToken cancellationToken)
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
