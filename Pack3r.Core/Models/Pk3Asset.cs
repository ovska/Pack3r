using System.Buffers;
using System.Diagnostics;
using System.IO.Compression;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Pk3Asset(Pk3AssetSource source, ZipArchiveEntry entry) : IAsset
{
    public string Name { get; } = entry.FullName.NormalizePath();
    public string FullPath { get; } = Path.Combine(source.ArchivePath, entry.FullName).NormalizePath();
    public AssetSource Source => source;

    public Stream OpenRead(bool isAsync = false) => entry.Open();

    public void AddTo(Pk3Writer writer, string? archivePath = null, Action<Stream, Stream>? transform = null)
    {
        string name = archivePath ?? Name;
        DateTime lastWriteTime = entry.LastWriteTime.DateTime;

        if (source.TryGetEntryData(entry, out ZipDirectory.Entry data, out long dataOffset))
        {
            Stream compressed = source.OpenData(dataOffset, data.CompressedSize);

            if (data.Method == DeflateMethod && transform is null)
            {
                // already deflated, so the writer copies the bytes as is: no decompression, compression or buffering
                writer.AddRaw(
                    name,
                    lastWriteTime,
                    data.Method,
                    data.Crc32,
                    data.CompressedSize,
                    data.UncompressedSize,
                    compressed);
                return;
            }

            if (data.Method is DeflateMethod or StoredMethod)
            {
                // decompressed and compressed again on a compression thread.
                // stored entries are compressed so they don't make the pk3 larger than necessary
                Stream content = data.Method == DeflateMethod
                    ? new DeflateStream(compressed, CompressionMode.Decompress, leaveOpen: false)
                    : compressed;

                writer.Add(name, lastWriteTime, content, transform);
                return;
            }

            compressed.Dispose();
        }

        // unsupported archive or compression method (e.g. deflate64): ZipArchive shares one file stream between
        // its entries, so decompress it on this thread and let the writer compress the copy in parallel
        var buffer = new MemoryStream(capacity: (int)Math.Min(entry.Length, Array.MaxLength));

        using (Stream sourceStream = entry.Open())
        {
            sourceStream.CopyTo(buffer);
        }

        buffer.Position = 0;
        writer.Add(name, lastWriteTime, buffer, transform);
    }

    private const ushort StoredMethod = 0;
    private const ushort DeflateMethod = 8;

    public ValueTask<IMemoryOwner<byte>> GetBytes(int? sizeHint, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled<IMemoryOwner<byte>>(cancellationToken);
        }

        var arrayPoolBufferWriter = new ArrayPoolBufferWriter<byte>(sizeHint ?? 1024);

        using (var stream = entry.Open())
        {
            stream.CopyTo(arrayPoolBufferWriter.AsStream());
        }

        return new ValueTask<IMemoryOwner<byte>>(arrayPoolBufferWriter);
    }

    internal string DebuggerDisplay => $"{{ Pk3Asset: '{Name}' from {Source.Name} }}";
}
