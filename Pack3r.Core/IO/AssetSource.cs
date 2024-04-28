using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract string RootPath { get; }
    public abstract FileInfo? GetShaderlist();

    public virtual void Dispose() { }

    public abstract bool Contains(ReadOnlyMemory<char> relativePath);

    public abstract bool TryHandleAsset(
            ZipArchive destination,
            ReadOnlyMemory<char> relativePath,
            out ZipArchiveEntry? entry);

    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    public sealed class Empty : AssetSource
    {
        public override string RootPath => throw new NotSupportedException();

        public override IAsyncEnumerable<Shader> EnumerateShaders(IShaderParser parser, Func<string, bool> skipPredicate, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override FileInfo? GetShaderlist() => null;
        public override bool Contains(ReadOnlyMemory<char> relativePath) => false;

        public override string ToString() => "<empty asset source>";

        public override bool TryHandleAsset(ZipArchive destination, ReadOnlyMemory<char> relativePath, out ZipArchiveEntry? entry)
        {
            entry = null;
            return false;
        }
    }
}

public abstract class AssetSource<TAsset> : AssetSource
{
    protected abstract ReadOnlyMemory<char> GetKey(TAsset asset);
    protected abstract IEnumerable<TAsset> EnumerateAssets();

    internal protected Dictionary<ReadOnlyMemory<char>, TAsset> Assets => _assetsLazy.Value;

    private readonly Lazy<Dictionary<ReadOnlyMemory<char>, TAsset>> _assetsLazy;

    protected AssetSource()
    {
        _assetsLazy = new(InitializeAssets, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private Dictionary<ReadOnlyMemory<char>, TAsset> InitializeAssets()
    {
        Dictionary<ReadOnlyMemory<char>, TAsset> dict = new(ROMCharComparer.Instance);

        foreach (var asset in EnumerateAssets())
        {
            var key = GetKey(asset);

            var texExt = key.GetTextureExtension();

            if (texExt == TextureExtension.Other)
            {
                dict.Add(key, asset);
            }
            else if (texExt == TextureExtension.Jpg)
            {
                // if a jpg path was already added by a tga file, overwrite it
                dict[key] = asset;

                // add tga since "downcasting" works from tga
                dict.TryAdd(key.ChangeExtension(".tga"), asset);

                // try to add extensionless asset for textures without shader
                dict.TryAdd(key.ChangeExtension(""), asset);
                continue;
            }
            else if (texExt == TextureExtension.Tga)
            {
                dict[key] = asset;

                // try to add extensionless asset for textures without shader
                dict.TryAdd(key.ChangeExtension(""), asset);
            }
        }

        return dict;
    }
}
