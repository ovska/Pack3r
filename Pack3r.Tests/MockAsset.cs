using System.IO.Compression;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

#nullable disable
internal class FileAsset(string path) : IAsset
{
    public string Name { get; } = path;
    public string FullPath { get; } = Path.GetFullPath(path);
    public AssetSource Source { get; }

    public ZipArchiveEntry CreateEntry(ZipArchive archive)
    {
        throw new NotImplementedException();
    }

    public Stream OpenRead() => File.OpenRead(FullPath);
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

    public Stream OpenRead()
    {
        throw new NotImplementedException();
    }
}
