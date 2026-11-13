using System.Diagnostics;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class DirectoryAssetSource(DirectoryInfo directory, bool notPacked) : AssetSource(notPacked)
{
    public DirectoryInfo Directory => directory;
    public override string RootPath => directory.FullName;

    public override string ToString() => $"{{ Dir: {Directory.FullName} }}";
    internal string DebuggerDisplay => $"{{ Dir src: '{Directory.Name}' (Excluded: {NotPacked}) }}";

    public override bool CanReadFilesConcurrently => true;

    public override IEnumerable<IAsset> EnumerateShaderFiles(Func<string, bool> skipPredicate)
    {
        foreach (var scriptsDir in Directory.EnumerateDirectories("scripts", SearchOption.TopDirectoryOnly))
        {
            foreach (var shaderFile in scriptsDir.EnumerateFiles("*.shader", SearchOption.TopDirectoryOnly))
            {
                if (skipPredicate(shaderFile.FullName))
                    continue;

                yield return new FileAsset(this, shaderFile);
            }
        }
    }

    protected override IEnumerable<IAsset> EnumerateAssets()
    {
        return Directory
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => Tokens.IncludeAsset(f.FullName))
            .Select(f => new FileAsset(this, f));
    }
}
