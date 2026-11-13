using System.Buffers;
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
    /// Size of the data in bytes, uncompressed.
    /// </summary>
    long Length { get; }

    /// <summary>
    /// Opens a stream to read the data.
    /// </summary>
    /// <returns></returns>
    Stream OpenRead();

    /// <summary>
    /// Reads the contents as bytes.
    /// </summary>
    IMemoryOwner<byte> GetBytes(int? sizeHint, CancellationToken cancellationToken);

    /// <summary>
    /// Queues the asset to be compressed into the destination archive. Called on the packaging thread; the data
    /// is read on a compression thread, so anything not safe to read concurrently is read before this returns.
    /// </summary>
    /// <param name="writer">Destination archive</param>
    /// <param name="archivePath">Path in the archive, defaults to <see cref="Name"/></param>
    /// <param name="transform">Optional function to modify the data, see <see cref="Pk3Writer.Add"/></param>
    /// <exception cref="IOException">The asset could not be opened</exception>
    void AddTo(Pk3Writer writer, string? archivePath = null, Action<Stream, Stream>? transform = null);
}
