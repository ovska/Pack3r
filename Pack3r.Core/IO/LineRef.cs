using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Pack3r.Extensions;
using Pack3r.Models;

namespace Pack3r.IO;

/// <summary>
/// A line as ASCII bytes, pointing into the reader's buffer: valid only until the next line is read.
/// Parsers inspect it, and call <see cref="ToLine"/> only when something from the line is kept.
/// </summary>
public readonly ref struct LineRef
{
    private readonly int _valueStart;

    public LineRef(string path, int index, ReadOnlySpan<byte> raw, bool keepRaw)
    {
        Path = path;
        Index = index;
        RawSpan = raw;

        ReadOnlySpan<byte> value = raw;

        if (!keepRaw)
        {
            int comment = raw.IndexOf("//"u8);
            value = (comment >= 0 ? raw[..comment] : raw).TrimWhitespace();
        }

        Value = value;
        _valueStart = OffsetIn(raw, value);
    }

    private LineRef(string path, int index, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> value)
    {
        Path = path;
        Index = index;
        RawSpan = raw;
        Value = value;
        _valueStart = OffsetIn(raw, value);
    }

    /// <summary>Path to the file the line was read from.</summary>
    public string Path { get; }

    /// <summary>1-based line number.</summary>
    public int Index { get; }

    /// <summary>The line as it was in the file.</summary>
    public ReadOnlySpan<byte> RawSpan { get; }

    /// <summary>The line with comments and whitespace removed, unless read as raw.</summary>
    public ReadOnlySpan<byte> Value { get; }

    public bool HasValue => !Value.IsEmpty;

    /// <summary>First byte of <see cref="Value"/>, or 0 if it's empty.</summary>
    public byte FirstChar => Value.IsEmpty ? (byte)0 : Value[0];

    public bool IsOpeningBrace => FirstChar == '{';
    public bool IsClosingBrace => FirstChar == '}';

    /// <summary>The raw line as a string, for log and error messages. Allocates.</summary>
    public string Raw => RawSpan.ToAsciiString();

    /// <summary>Same line with a different value, which must be a slice of <see cref="RawSpan"/>.</summary>
    public LineRef WithValue(ReadOnlySpan<byte> value) => new(Path, Index, RawSpan, value);

    /// <summary>Copies the line into an array, to keep it after the next line has been read.</summary>
    public Line ToLine() => new(Path, Index, RawSpan.ToArray(), _valueStart, Value.Length);

    private static int OffsetIn(ReadOnlySpan<byte> raw, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
            return 0;

        nint offset = Unsafe.ByteOffset(ref MemoryMarshal.GetReference(raw), ref MemoryMarshal.GetReference(value));
        Debug.Assert(offset >= 0 && offset + value.Length <= raw.Length, "Value must be a slice of the raw line");
        return (int)offset;
    }
}

/// <summary>
/// Lines of a file or stream, see <see cref="LineEnumerator"/>. The file is opened, and the buffer rented, by
/// <see cref="GetEnumerator"/>, which disposes them when the enumeration ends.
/// </summary>
public readonly ref struct LineEnumerable
{
    private readonly string? _file;
    private readonly IAsset? _asset;
    private readonly Stream? _stream;
    private readonly string _path;
    private readonly bool _keepRaw;
    private readonly CancellationToken _cancellationToken;
    private readonly int _bufferSize;

    private LineEnumerable(string? file, IAsset? asset, Stream? stream, string path, bool keepRaw, CancellationToken cancellationToken, int bufferSize)
    {
        _file = file;
        _asset = asset;
        _stream = stream;
        _path = path;
        _keepRaw = keepRaw;
        _cancellationToken = cancellationToken;
        _bufferSize = bufferSize;
    }

    public static LineEnumerable FromFile(string path, bool keepRaw, CancellationToken cancellationToken)
        => new(path, null, null, path, keepRaw, cancellationToken, LineEnumerator.DefaultBufferSize);

    public static LineEnumerable FromAsset(IAsset asset, bool keepRaw, CancellationToken cancellationToken)
        => new(null, asset, null, asset.FullPath, keepRaw, cancellationToken, LineEnumerator.DefaultBufferSize);

    /// <summary>Lines of a stream, which is disposed when the enumeration ends. Can be enumerated once.</summary>
    public static LineEnumerable FromStream(
        Stream stream,
        string path,
        bool keepRaw,
        CancellationToken cancellationToken,
        int bufferSize = LineEnumerator.DefaultBufferSize)
        => new(null, null, stream, path, keepRaw, cancellationToken, bufferSize);

    public LineEnumerator GetEnumerator()
    {
        Stream stream = _stream ?? _asset?.OpenRead() ?? new FileStream(
            _file!,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 0, // the enumerator does the buffering
            FileOptions.SequentialScan);

        return new LineEnumerator(stream, _path, _keepRaw, _cancellationToken, _bufferSize);
    }
}

/// <summary>
/// Reads lines from a stream into a buffer rented from the shared pool. Line breaks are the same as
/// <see cref="StreamReader.ReadLine"/>: \n, \r\n and \r. Empty lines, and lines left empty without comments,
/// are skipped, but counted in the line numbers.
/// </summary>
public ref struct LineEnumerator
{
    public const int DefaultBufferSize = 64 * 1024;

    private Stream? _stream;
    private byte[]? _buffer;
    private int _start;
    private int _end;
    private bool _endOfStream;
    private bool _skipLineFeed;
    private int _index;
    private LineRef _current;

    private readonly string _path;
    private readonly bool _keepRaw;
    private readonly CancellationToken _cancellationToken;

    internal LineEnumerator(Stream stream, string path, bool keepRaw, CancellationToken cancellationToken, int bufferSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(bufferSize, 1);

        _stream = stream;
        _buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
        _path = path;
        _keepRaw = keepRaw;
        _cancellationToken = cancellationToken;
    }

    public readonly LineRef Current => _current;

    public bool MoveNext()
    {
        ObjectDisposedException.ThrowIf(_buffer is null, typeof(LineEnumerator));

        while (true)
        {
            // a \r was the last byte read, a \n right after it belongs to the same line break
            if (_skipLineFeed)
            {
                if (_start == _end && !_endOfStream)
                {
                    Fill();
                    continue;
                }

                if (_start < _end && _buffer[_start] == '\n')
                    _start++;

                _skipLineFeed = false;
            }

            ReadOnlySpan<byte> available = _buffer.AsSpan(_start, _end - _start);
            int lineEnd = available.IndexOfAny((byte)'\r', (byte)'\n');
            ReadOnlySpan<byte> raw;

            if (lineEnd >= 0)
            {
                raw = available[..lineEnd];
                _skipLineFeed = available[lineEnd] == '\r';
                _start += lineEnd + 1;
            }
            else if (_endOfStream)
            {
                if (available.IsEmpty)
                {
                    _current = default;
                    return false;
                }

                raw = available;
                _start = _end;
            }
            else
            {
                Fill();
                continue;
            }

            _index++;

            if (raw.IsEmpty)
                continue;

            _cancellationToken.ThrowIfCancellationRequested();

            var line = new LineRef(_path, _index, raw, _keepRaw);

            if (line.HasValue)
            {
                _current = line;
                return true;
            }
        }
    }

    // moves the unread bytes to the start of the buffer, grows it if a line doesn't fit, and reads more
    private void Fill()
    {
        byte[] buffer = _buffer!;
        int unread = _end - _start;

        if (_start > 0)
        {
            buffer.AsSpan(_start, unread).CopyTo(buffer);
            _start = 0;
            _end = unread;
        }

        if (_end == buffer.Length)
        {
            byte[] larger = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
            buffer.AsSpan(0, _end).CopyTo(larger);
            ArrayPool<byte>.Shared.Return(buffer);
            _buffer = buffer = larger;
        }

        int read = _stream!.Read(buffer, _end, buffer.Length - _end);

        if (read == 0)
            _endOfStream = true;
        else
            _end += read;
    }

    public void Dispose()
    {
        _current = default;

        if (_buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = null;
        }

        _stream?.Dispose();
        _stream = null;
    }
}
