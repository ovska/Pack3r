using System.Runtime.InteropServices;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract string RootPath { get; }
    public abstract FileInfo? GetShaderlist();
    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    protected abstract IEnumerable<IAsset> EnumerateAssets();

    /// <summary>
    /// Whether this source is used to discover files, but never pack them (pak0, mod files etc).
    /// </summary>
    public bool IsExcluded { get; }

    /// <summary>
    /// Display name (folder/pk3 name).
    /// </summary>
    public string Name => _name ??= Path.GetFileName(RootPath);
    private string? _name;

    public Dictionary<ReadOnlyMemory<char>, IAsset> Assets => _assetsLazy.Value;
    private readonly Lazy<Dictionary<ReadOnlyMemory<char>, IAsset>> _assetsLazy;

    protected bool _disposed;

    protected AssetSource(bool isExcluded)
    {
        IsExcluded = isExcluded;
        _assetsLazy = new(InitializeAssets, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public virtual void Dispose()
    {
        _disposed = true;
    }

    public override bool Equals(object? obj)
    {
        return obj?.GetType() == GetType() && RootPath.EqualsF((obj as AssetSource)?.RootPath);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), RootPath);
    }

    private Dictionary<ReadOnlyMemory<char>, IAsset> InitializeAssets()
    {
        Dictionary<ReadOnlyMemory<char>, IAsset> dict = new(ROMCharComparer.Instance);

        foreach (var asset in EnumerateAssets())
        {
            ReadOnlyMemory<char> key = asset.Name.AsMemory();

            var texExt = asset.FullPath.GetTextureExtension();

            if (texExt == TextureExtension.Other)
            {
                dict.Add(key, asset);
            }
            else if (texExt == TextureExtension.Jpg)
            {
                // if a jpg path was already added by a tga file, overwrite it
                dict[key] = asset;

                // add tga since "downcasting" works from tga to jpg
                dict.TryAdd(key.ChangeExtension(".tga"), asset);

                // try to add extensionless asset for textures without shader
                dict.TryAdd(key.ChangeExtension(""), asset);
                continue;
            }
            else if (texExt == TextureExtension.Tga)
            {
                dict[key] = asset;

                // tga takes prio over jpg if there is no texture
                dict[key.ChangeExtension("")] = asset;
            }
        }

        return dict;
    }
}
