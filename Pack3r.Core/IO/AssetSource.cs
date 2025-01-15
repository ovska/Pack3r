using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract string RootPath { get; }
    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    protected abstract IEnumerable<IAsset> EnumerateAssets();

    /// <summary>
    /// Whether this source is used to discover files, but never pack them (pak0, mod files etc).
    /// </summary>
    public bool NotPacked { get; }

    /// <summary>
    /// Display name (folder/pk3 name).
    /// </summary>
    public string Name => _name ??= Path.GetFileName(RootPath);
    private string? _name;

    public Dictionary<QString, IAsset> Assets => _assetsLazy.Value;
    private readonly Lazy<Dictionary<QString, IAsset>> _assetsLazy;

    protected bool _disposed;

    protected AssetSource(bool notPacked)
    {
        NotPacked = notPacked;
        _assetsLazy = new(InitializeAssets);
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

    private Dictionary<QString, IAsset> InitializeAssets()
    {
        Dictionary<QString, IAsset> dict = new(QString.Comparer);
        var lookup = dict.GetAlternateLookup<ReadOnlySpan<char>>();
        Span<char> buffer = stackalloc char[128];

        foreach (var asset in EnumerateAssets())
        {
            QString key = asset.Name;

            var texExt = asset.FullPath.GetTextureExtension();

            if (texExt == TextureExtension.Other)
            {
                dict[key] = asset; // overwrite if same file is here multiple times
                // TODO: pick the lowercase one? or check edit timestamp and pick newest?
            }
            else if (texExt == TextureExtension.Jpg)
            {
                dict[key] = asset;

                // a texture with .tga extension will work with .jpg files as well
                if (key.Length <= buffer.Length)
                {
                    // extension is .jpg so it has a known length of 4
                    key.Span[..^4].CopyTo(buffer);
                    ".tga".CopyTo(buffer[(key.Length - 4)..]);
                    lookup.TryAdd(buffer[..key.Length], asset);
                }
                else
                {
                    dict.TryAdd($"{key[..^4]}.tga", asset);
                }

                // try to add extensionless asset for textures without a shader (see below)
                dict.TryAdd(key.TrimExtension(), asset);
            }
            else if (texExt == TextureExtension.Tga)
            {
                dict[key] = asset;

                // overwrite; tga takes priority over jpg if there is no texture
                dict[key.TrimExtension()] = asset;
            }
        }

        return dict;
    }
}
