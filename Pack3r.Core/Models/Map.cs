using System.Collections.Immutable;
using System.Diagnostics;
using Pack3r.Extensions;
using Pack3r.IO;
using IOPath = System.IO.Path;

namespace Pack3r.Models;

public class MapAssets
{
    /// <summary>
    /// Shaders referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required HashSet<ReadOnlyMemory<char>> Shaders { get; init; }

    /// <summary>
    /// Model/audio/video files referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required HashSet<ReadOnlyMemory<char>> Resources { get; init; }

    /// <summary>
    /// Whether the map has stylelights, and the q3map_mapname.shader file needs to be included.
    /// </summary>
    public required bool HasStyleLights { get; init; }
}

public sealed class Map : MapAssets, IDisposable
{
    private bool _disposed;

    public Map(PackOptions options)
    {
        _options = options;
        _assetDirs = new(() => InitAssetDirectories().ToImmutableArray(), LazyThreadSafetyMode.ExecutionAndPublication);
        _assetSrcs = new(() => InitAssetSources().ToImmutableArray(), LazyThreadSafetyMode.ExecutionAndPublication);
        _pak0 = new(
            () => (AssetSource?)AssetSources.OfType<Pk3AssetSource>().FirstOrDefault(
                src => IOPath.GetFileName(src.ArchivePath).EqualsF("pak0.pk3"))
                ?? new AssetSource.Empty(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// .map file name without extension
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full path to .map
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// ETMain folder.
    /// </summary>
    public required DirectoryInfo ETMain { get; init; }

    public ImmutableArray<DirectoryInfo> AssetDirectories => _assetDirs.Value;
    public ImmutableArray<AssetSource> AssetSources => _assetSrcs.Value;
    public AssetSource Pak0 => _pak0.Value;

    private readonly PackOptions _options;
    private string? _root;
    private readonly Lazy<ImmutableArray<DirectoryInfo>> _assetDirs;
    private readonly Lazy<ImmutableArray<AssetSource>> _assetSrcs;
    private readonly Lazy<AssetSource> _pak0;

    /// <summary>
    /// Gets the relative etmain of the map.<br/>
    /// <c>ET/etmain/maps/file.map</c> -> <c>ET/etmain/</c>
    /// <c>ET/etmain/myproject.pk3dir/maps/file.map</c> -> <c>ET/etmain/myproject.pk3dir</c>
    /// </summary>
    public string GetMapRoot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_root is not null)
            return _root;

        var mapsDir = IOPath.GetDirectoryName(Path);

        if (mapsDir is not null)
        {
            var etmainOrPk3dir = IOPath.GetDirectoryName(mapsDir);

            if (etmainOrPk3dir is not null)
            {
                return _root ??= IOPath.GetFullPath(etmainOrPk3dir);
            }
        }

        throw new UnreachableException($"Could not get map root: {Path}");
    }

    /// <summary>
    /// Returns path <strong>relative to ETMain</strong>.
    /// </summary>
    public string GetArchivePath(string fullPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return IOPath.GetRelativePath(AssetDirectories[0].FullName, fullPath);
    }

    private IEnumerable<DirectoryInfo> InitAssetDirectories()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        HashSet<string> unique = [];

        unique.Add(GetMapRoot());
        yield return new DirectoryInfo(GetMapRoot());

        if (unique.Add(ETMain.FullName))
            yield return ETMain;

        foreach (var pk3dir in ETMain.EnumerateDirectories("*.pk3dir", SearchOption.TopDirectoryOnly))
        {
            if (unique.Add(pk3dir.FullName))
                yield return pk3dir;
        }
    }

    private IEnumerable<AssetSource> InitAssetSources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var dir in AssetDirectories)
        {
            yield return new DirectoryAssetSource(dir);

            if (_options.LoadPk3s || _options.ExcludedPk3s.Count > 0)
            {
                foreach (var file in dir.EnumerateFiles("*.pk3", SearchOption.TopDirectoryOnly))
                {
                    bool isBuiltin = false;

                    foreach (var builtinAssetName in _options.ExcludedPk3s)
                    {
                        if (IOPath.GetFileName(file.FullName.AsSpan()).EqualsF(builtinAssetName))
                        {
                            isBuiltin = true;
                            break;
                        }
                    }

                    if (isBuiltin || _options.LoadPk3s)
                        yield return new Pk3AssetSource(file.FullName, isBuiltin);
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_assetSrcs.IsValueCreated)
        {
            foreach (var src in _assetSrcs.Value)
            {
                src.Dispose();
            }
        }
    }
}
