using System.Runtime.CompilerServices;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Services;

namespace Pack3r.IO;

public sealed class DirectoryAssetSource(DirectoryInfo directory, bool isPak0, IIntegrityChecker checker) : AssetSource(checker)
{
    public DirectoryInfo Directory => directory;
    public override string RootPath => directory.FullName;
    public override bool IsExcluded => isPak0;

    public override string ToString() => $"{{ Dir: {Directory.FullName} }}";

    public override bool Contains(ReadOnlyMemory<char> relativePath)
    {
        return Assets.ContainsKey(relativePath);
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

                await foreach (var shader in parser.Parse(new FileAsset(this, shaderFile), cancellationToken))
                {
                    yield return shader;
                }
            }
        }
    }

    protected override IEnumerable<IAsset> EnumerateAssets()
    {
        return Directory
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => Tokens.PackableFile().IsMatch(f.FullName))
            .Select(f => new FileAsset(this, f));
    }

    public override FileInfo? GetShaderlist()
    {
        return new FileInfo(Path.Combine(Directory.FullName, "scripts", "shaderlist.txt"));
    }
}
