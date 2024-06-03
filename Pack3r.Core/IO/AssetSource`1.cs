using Pack3r.Extensions;

namespace Pack3r.IO;

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

    protected void VerifyIntegrity(string path, Stream stream)
    {
        stream.Position = 17;

        if (stream.ReadByte() == 0x20)
        {
            Console.WriteLine("warning: " + path);
        }
    }
}
