using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

internal class FileAsset : IAsset
{
    public string Name { get; }
    public string FullPath { get; }
    public AssetSource Source { get; }

    public FileAsset(string path)
    {
        FullPath = Path.GetFullPath(path);
        Name = path;
        Source = null!;
    }

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
