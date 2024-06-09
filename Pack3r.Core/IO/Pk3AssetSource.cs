using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Services;

namespace Pack3r.IO;

public sealed class Pk3AssetSource(string path, bool isExcluded, IIntegrityChecker checker) : AssetSource(checker)
{
    public string ArchivePath => path;
    public override bool IsExcluded => isExcluded;
    public override string RootPath => ArchivePath;

    private readonly ZipArchive _archive = ZipFile.OpenRead(path);

    public override string ToString() => $"{{ Pk3: {ArchivePath} }}";

    public override bool Contains(ReadOnlyMemory<char> relativePath)
    {
        return Assets.ContainsKey(relativePath);
    }

    public override async IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var entry in _archive.Entries)
        {
            if (!entry.FullName.HasExtension(".shader") || skipPredicate(entry.FullName))
                continue;

            await foreach (var shader in parser.Parse(new Pk3Asset(this, ArchivePath, entry), cancellationToken))
            {
                yield return shader;
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _archive.Dispose();
            base.Dispose();
        }
    }

    protected override IEnumerable<IAsset> EnumerateAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive.Entries
            .Where(entry => Tokens.PackableFile()
            .IsMatch(entry.FullName.GetExtension()))
            .Select(entry => new Pk3Asset(this, ArchivePath, entry));
    }

    public override FileInfo? GetShaderlist()
    {
        return null;
    }
}
