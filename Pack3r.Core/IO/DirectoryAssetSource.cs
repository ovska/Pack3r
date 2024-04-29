using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public sealed class DirectoryAssetSource(DirectoryInfo directory) : AssetSource<FileInfo>
{
    public DirectoryInfo Directory { get; } = directory;
    public override string RootPath => Directory.FullName;
    public override bool IsPak0 => false;

    public override string ToString() => Directory.FullName;

    public override bool Contains(ReadOnlyMemory<char> relativePath)
    {
        return Assets.ContainsKey(relativePath);
    }

    public override bool TryHandleAsset(
        ZipArchive destination,
        ReadOnlyMemory<char> relativePath,
        out ZipArchiveEntry? entry)
    {
        if (Assets.TryGetValue(relativePath, out var file))
        {
            if (IsPak0)
            {
                entry = null;
            }
            else
            {
                string archivePath = Path.GetRelativePath(Directory.FullName, file.FullName);
                entry = destination.CreateEntryFromFile(file.FullName, archivePath, GetCompressionLevel(file.FullName));
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
        foreach (var scriptsDir in Directory.EnumerateDirectories("scripts", SearchOption.TopDirectoryOnly))
        {
            foreach (var shaderFile in scriptsDir.EnumerateFiles("*.shader", SearchOption.TopDirectoryOnly))
            {
                if (skipPredicate(shaderFile.FullName))
                    continue;

                await foreach (var shader in parser.Parse(this, shaderFile.FullName, cancellationToken))
                {
                    yield return shader;
                }
            }
        }
    }

    protected override ReadOnlyMemory<char> GetKey(FileInfo asset)
    {
        return Path.GetRelativePath(Directory.FullName, asset.FullName).Replace('\\', '/').AsMemory();
    }

    protected override IEnumerable<FileInfo> EnumerateAssets()
    {
        return Directory
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => Tokens.PackableFile().IsMatch(f.FullName));
    }

    public override FileInfo? GetShaderlist()
    {
        return new FileInfo(Path.Combine(Directory.FullName, "scripts", "shaderlist.txt"));
    }
}
