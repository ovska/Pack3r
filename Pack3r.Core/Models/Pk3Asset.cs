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

    public ZipArchiveEntry CreateEntry(ZipArchive archive)
    {
        ZipArchiveEntry destination = archive.CreateEntry(Name, CompressionLevel.Optimal);
        destination.LastWriteTime = entry.LastWriteTime;

        using Stream sourceStream = entry.Open();
        using Stream destinationStream = destination.Open();
        sourceStream.CopyTo(destinationStream);
        return destination;
    }

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
