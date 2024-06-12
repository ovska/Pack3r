using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Pk3AssetSource(string path, bool isExcluded) : AssetSource(isExcluded)
{
    public string ArchivePath => path;
    public override string RootPath => ArchivePath;

    private readonly ZipArchive _archive = ZipFile.OpenRead(path);

    public override string ToString() => $"{{ Pk3: {ArchivePath} }}";
    internal string DebuggerDisplay => $"{{ Pk3 src: '{Path.GetFileName(ArchivePath)}' (Excluded: {IsExcluded}) }}";

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

            if (entry.FullName.Length > Global.MAX_QPATH)
                continue;

            await foreach (var shader in parser.Parse(new Pk3Asset(this, entry), cancellationToken))
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
            .Where(entry => entry.FullName.Length < Global.MAX_QPATH && Tokens.PackableFile().IsMatch(entry.FullName.GetExtension()))
            .Select(entry => new Pk3Asset(this, entry));
    }

    public override IAsset? GetShaderlist()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ZipArchiveEntry? entry =
            _archive.GetEntry("scripts/shaderlist.txt") ??
            _archive.GetEntry("scripts\\shaderlist.txt");

        if (entry is not null)
        {
            return new Pk3Asset(this, entry);
        }

        return null;
    }
}
