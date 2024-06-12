using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Pk3Asset(
    Pk3AssetSource source,
    ZipArchiveEntry entry) : IAsset
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

    internal string DebuggerDisplay => $"{{ Pk3Asset: '{Name}' from {Source.Name} }}";
}
