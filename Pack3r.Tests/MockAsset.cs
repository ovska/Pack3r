using System.Buffers;
using System.IO.Compression;
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
        return new NoopMemoryOwner(await File.ReadAllBytesAsync(path, cancellationToken));
    }

    public Stream OpenRead(bool isAsync = false) => File.OpenRead(FullPath);

    private sealed class NoopMemoryOwner(Memory<byte> data) : IMemoryOwner<byte>
    {
        public Memory<byte> Memory => data;
        public void Dispose()
        {
        }
    }
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
