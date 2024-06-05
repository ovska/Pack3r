using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

public interface IAsset
{
    /// <summary>
    /// Relative path of the asset.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Absolute path of the asset.
    /// </summary>
    string FullPath { get; }

    /// <summary>
    /// Source (directory or pk3)
    /// </summary>
    AssetSource Source { get; }

    /// <summary>
    /// Opens a stream to read the data.
    /// </summary>
    /// <returns></returns>
    Stream OpenRead();

    /// <summary>
    /// Creates a new entry to destination archive.
    /// </summary>
    ZipArchiveEntry CreateEntry(ZipArchive archive);
}

public sealed class FileAsset(
    DirectoryAssetSource source,
    FileInfo file) : IAsset
{
    public string Name { get; } = Path.GetRelativePath(source.RootPath, file.FullName).NormalizePath();
    public string FullPath { get; } = file.FullName.NormalizePath();
    public AssetSource Source => source;

    public Stream OpenRead() => file.OpenRead();

    public ZipArchiveEntry CreateEntry(ZipArchive archive) => archive.CreateEntryFromFile(FullPath, Name, CompressionLevel.Optimal);
}

public sealed class Pk3Asset(
    Pk3AssetSource source,
    string archivePath,
    ZipArchiveEntry entry) : IAsset
{
    public string Name { get; } = entry.FullName.NormalizePath();
    public string FullPath { get; } = Path.Combine(archivePath, entry.FullName).NormalizePath();
    public AssetSource Source => source;

    public Stream OpenRead() => entry.Open();

    public ZipArchiveEntry CreateEntry(ZipArchive archive)
    {
        ZipArchiveEntry destination = archive.CreateEntry(Name, CompressionLevel.Optimal);
        destination.LastWriteTime = entry.LastWriteTime;

        using Stream sourceStream = entry.Open();
        using Stream destinationStream = destination.Open();
        sourceStream.CopyTo(destinationStream);
        return destination;
    }
}
