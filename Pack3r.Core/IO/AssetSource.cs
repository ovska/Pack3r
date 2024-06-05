using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Services;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract bool IsPak0 { get; }
    public abstract string RootPath { get; }
    public abstract FileInfo? GetShaderlist();

    internal protected Dictionary<ReadOnlyMemory<char>, IAsset> Assets => _assetsLazy.Value;

    private readonly Lazy<Dictionary<ReadOnlyMemory<char>, IAsset>> _assetsLazy;
    private readonly IIntegrityChecker _checker;

    protected bool _disposed;

    protected AssetSource(IIntegrityChecker checker)
    {
        _checker = checker;
        _assetsLazy = new(InitializeAssets, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public virtual void Dispose()
    {
        _disposed = true;
    }

    public abstract bool Contains(ReadOnlyMemory<char> relativePath);

    public bool TryHandleAsset(
        ZipArchive destination,
        ReadOnlyMemory<char> relativePath,
        out ZipArchiveEntry? entry)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Assets.TryGetValue(relativePath, out IAsset? asset))
        {
            if (IsPak0)
            {
                entry = null;
            }
            else
            {
                _checker.CheckIntegrity(asset);
                entry = asset.CreateEntry(destination);
            }

            return true;
        }

        entry = null;
        return false;
    }

    public bool TryRead(
       ReadOnlyMemory<char> resourcePath,
       ILineReader reader,
       LineOptions options,
       CancellationToken cancellationToken,
       [NotNullWhen(true)] out IAsyncEnumerable<Line>? lines)
    {
        if (Assets.TryGetValue(resourcePath, out IAsset? asset))
        {
            lines = reader.ReadLines(asset, options, cancellationToken);
            return true;
        }

        lines = null;
        return false;
    }

    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    public override bool Equals(object? obj)
    {
        return obj?.GetType() == GetType() && RootPath.EqualsF((obj as AssetSource)?.RootPath);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), RootPath);
    }

    protected abstract IEnumerable<IAsset> EnumerateAssets();

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
