using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Pack3r.IO;

/// <summary>
/// Minimal reader for the central directory of a zip file, to find where each entry's compressed data is.
/// </summary>
/// <remarks>
/// <see cref="System.IO.Compression.ZipArchive"/> reads all entries through one shared stream and doesn't expose
/// where an entry's data starts, so it can't be read from several threads or copied without decompressing it.
/// With the offsets, the data can be read with <see cref="RandomAccess"/>, which is safe to use concurrently.
/// Archives this type doesn't understand (zip64, split archives) are left to <c>ZipArchive</c>.
/// </remarks>
public sealed class ZipDirectory
{
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const int LocalFileHeaderSize = 30;
    private const int CentralDirectoryHeaderSize = 46;
    private const int EndOfCentralDirectorySize = 22;
    private const ushort FlagEncrypted = 0x0001;
    private const ushort FlagStrongEncryption = 0x0040;

    public readonly record struct Entry(
        ushort Method,
        ushort Flags,
        uint Crc32,
        long CompressedSize,
        long UncompressedSize,
        long LocalHeaderOffset)
    {
        public bool IsEncrypted => (Flags & (FlagEncrypted | FlagStrongEncryption)) != 0;
    }

    private readonly Dictionary<string, Entry> _entries;
    private readonly long _fileLength;

    private ZipDirectory(Dictionary<string, Entry> entries, long fileLength)
    {
        _entries = entries;
        _fileLength = fileLength;
    }

    public int Count => _entries.Count;

    /// <summary>
    /// Gets an entry by its full name. Entries whose name appears more than once in the archive are not found.
    /// </summary>
    public bool TryGetEntry(string fullName, out Entry entry) => _entries.TryGetValue(fullName, out entry);

    /// <summary>
    /// Reads the central directory, or returns <see langword="null"/> if the archive is not supported.
    /// </summary>
    /// <exception cref="IOException">Reading the file failed</exception>
    public static ZipDirectory? TryRead(SafeFileHandle handle)
    {
        long length = RandomAccess.GetLength(handle);

        if (length < EndOfCentralDirectorySize)
            return null;

        // end of central directory record is at the end of the file, followed by a comment of up to 64 KiB
        byte[] tail = new byte[(int)Math.Min(length, EndOfCentralDirectorySize + ushort.MaxValue)];
        ReadExactly(handle, tail, length - tail.Length);

        int end = -1;

        for (int i = tail.Length - EndOfCentralDirectorySize; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(i)) == EndOfCentralDirectorySignature &&
                i + EndOfCentralDirectorySize + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 20)) <= tail.Length)
            {
                end = i;
                break;
            }
        }

        if (end == -1)
            return null;

        ReadOnlySpan<byte> record = tail.AsSpan(end, EndOfCentralDirectorySize);
        ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
        ushort directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
        ushort entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
        ushort entryCount = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
        uint directorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
        uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

        // split archives, or zip64 where the real values are elsewhere
        if (disk != 0 ||
            directoryDisk != 0 ||
            entriesOnDisk != entryCount ||
            entryCount == ushort.MaxValue ||
            directorySize == uint.MaxValue ||
            directoryOffset == uint.MaxValue ||
            directoryOffset + (long)directorySize > length)
        {
            return null;
        }

        byte[] directory = new byte[directorySize];
        ReadExactly(handle, directory, directoryOffset);

        var entries = new Dictionary<string, Entry>(entryCount, StringComparer.Ordinal);
        HashSet<string>? duplicates = null;
        int position = 0;

        for (int i = 0; i < entryCount; i++)
        {
            if (position + CentralDirectoryHeaderSize > directory.Length)
                return null;

            ReadOnlySpan<byte> header = directory.AsSpan(position);

            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != CentralDirectorySignature)
                return null;

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[20..]);
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(header[24..]);
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[30..]);
            ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(header[32..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(header[42..]);

            int next = position + CentralDirectoryHeaderSize + nameLength + extraLength + commentLength;

            if (next > directory.Length)
                return null;

            // ZipArchive decodes names as UTF-8 by default, so the names match its FullName
            string name = Encoding.UTF8.GetString(header.Slice(CentralDirectoryHeaderSize, nameLength));
            position = next;

            // zip64 entry, the real values are in the extra field
            if (compressedSize == uint.MaxValue || uncompressedSize == uint.MaxValue || offset == uint.MaxValue)
                continue;

            if (!entries.TryAdd(name, new Entry(method, flags, crc32, compressedSize, uncompressedSize, offset)))
                (duplicates ??= []).Add(name);
        }

        if (duplicates is not null)
        {
            foreach (var name in duplicates)
                entries.Remove(name);
        }

        return new ZipDirectory(entries, length);
    }

    /// <summary>
    /// Returns the position of the entry's compressed data in the file.
    /// </summary>
    /// <exception cref="InvalidDataException">The local header is invalid</exception>
    /// <exception cref="IOException">Reading the file failed</exception>
    public long GetDataOffset(SafeFileHandle handle, in Entry entry)
    {
        Span<byte> header = stackalloc byte[LocalFileHeaderSize];
        ReadExactly(handle, header, entry.LocalHeaderOffset);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != LocalFileHeaderSignature)
            throw new InvalidDataException($"Invalid local file header at offset {entry.LocalHeaderOffset}");

        // the name and extra field may differ from the central directory, so the lengths are read from here
        long dataOffset = entry.LocalHeaderOffset
            + LocalFileHeaderSize
            + BinaryPrimitives.ReadUInt16LittleEndian(header[26..])
            + BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);

        if (dataOffset + entry.CompressedSize > _fileLength)
            throw new InvalidDataException($"Entry data at offset {dataOffset} extends past the end of the file");

        return dataOffset;
    }

    private static void ReadExactly(SafeFileHandle handle, Span<byte> buffer, long fileOffset)
    {
        while (!buffer.IsEmpty)
        {
            int read = RandomAccess.Read(handle, buffer, fileOffset);

            if (read == 0)
                throw new EndOfStreamException();

            buffer = buffer[read..];
            fileOffset += read;
        }
    }
}

/// <summary>
/// Read-only stream over a range of a file. Uses positional reads, so streams over the same handle can be read
/// from different threads at the same time. Does not own the handle.
/// </summary>
public sealed class FileRangeStream(SafeFileHandle handle, long offset, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;

    public override long Position
    {
        get => _position;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            _position = value;
        }
    }

    public override int Read(Span<byte> buffer)
    {
        long remaining = length - _position;

        if (remaining <= 0 || buffer.IsEmpty)
            return 0;

        if (buffer.Length > remaining)
            buffer = buffer[..(int)remaining];

        int read = RandomAccess.Read(handle, buffer, offset + _position);
        _position += read;
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
