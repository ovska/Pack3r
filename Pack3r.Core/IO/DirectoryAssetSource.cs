using System.Diagnostics;
using System.Runtime.CompilerServices;
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
}
