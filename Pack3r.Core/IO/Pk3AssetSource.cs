using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public sealed class Pk3AssetSource(string path, bool isPak0) : AssetSource<ZipArchiveEntry>
{
    public string ArchivePath { get; } = path;
    public ZipArchive Archive
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _archive;
        }
    }

    public override bool IsPak0 { get; } = isPak0;
    public override string RootPath => ArchivePath;

    private readonly ZipArchive _archive = ZipFile.OpenRead(path);
    private bool _disposed;

    public override string ToString() => $"{{ Pk3: {ArchivePath} }}";

    public override bool Contains(ReadOnlyMemory<char> relativePath)
    {
        return Assets.ContainsKey(relativePath);
    }

    public override bool TryHandleAsset(
        ZipArchive destination,
        ReadOnlyMemory<char> relativePath,
        out ZipArchiveEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Assets.TryGetValue(relativePath, out var pk3entry))
        {
            if (IsPak0)
            {
                entry = null;
            }
            else
            {
                entry = destination.CreateEntry(pk3entry.FullName, GetCompressionLevel(pk3entry.FullName));
                entry.LastWriteTime = pk3entry.LastWriteTime;

                using Stream destinationStream = entry.Open();
                using Stream sourceStream = pk3entry.Open();
                sourceStream.CopyTo(destinationStream);
            }

            return true;
        }

        entry = null;
        return false;
    }

    public override async IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var entry in _archive.Entries)
        {
            if (skipPredicate(entry.FullName))
                continue;

            if (entry.FullName.HasExtension(".shader"))
            {
                await foreach (var shader in parser.Parse(this, entry, cancellationToken))
                {
                    yield return shader;
                }
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _archive.Dispose();
        }
    }

    protected override IEnumerable<ZipArchiveEntry> EnumerateAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive.Entries.Where(entry => Tokens.PackableFile().IsMatch(entry.FullName.GetExtension()));
    }

    protected override ReadOnlyMemory<char> GetKey(ZipArchiveEntry asset) => asset.FullName.Replace('\\', '/').AsMemory();

    public override FileInfo? GetShaderlist()
    {
        return null;
    }
}
