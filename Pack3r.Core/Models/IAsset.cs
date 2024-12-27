using System.Buffers;
using System.IO.Compression;
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
    Stream OpenRead(bool isAsync = false);

    /// <summary>
    /// Reads the contents as bytes.
    /// </summary>
    ValueTask<IMemoryOwner<byte>> GetBytes(int? sizeHint, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new entry to destination archive.
    /// </summary>
    ZipArchiveEntry CreateEntry(ZipArchive archive);
}
