using System.Buffers;
using System.IO.Compression;
using CommunityToolkit.HighPerformance.Buffers;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

#nullable disable
internal class FileAsset(string path) : IAsset
{
    public string Name => path;
    public string FullPath => Path.GetFullPath(path);
    public AssetSource Source { get; }

    public ZipArchiveEntry CreateEntry(ZipArchive archive)
    {
        throw new NotImplementedException();
    }

    public async ValueTask<IMemoryOwner<byte>> GetBytes(int? sizeHint, CancellationToken cancellationToken)
    {
        var data = await File.ReadAllBytesAsync(path, cancellationToken);
        var owner = MemoryOwner<byte>.Allocate(data.Length);
        return owner;
    }

    public Stream OpenRead(bool isAsync = false) => File.OpenRead(FullPath);
}

internal class MockAsset : IAsset
{
    public string Name { get; }
    public string FullPath { get; }
    public AssetSource Source { get; }

    public ZipArchiveEntry CreateEntry(ZipArchive archive)
    {
        throw new NotImplementedException();
    }

    public ValueTask<IMemoryOwner<byte>> GetBytes(int? sizeHint, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Stream OpenRead(bool isAsync = false)
    {
        throw new NotImplementedException();
    }
}
