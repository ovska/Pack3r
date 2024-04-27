using System.Collections.Immutable;
using System.Diagnostics;
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

public sealed class Map : MapAssets
{
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

    public ImmutableArray<DirectoryInfo> AssetDirectories
    {
        get => _assetDirs.IsDefault ? (_assetDirs = InitAssetDirectories().ToImmutableArray()) : _assetDirs;
    }

    private string? _root;
    private ImmutableArray<DirectoryInfo> _assetDirs;

    /// <summary>
    /// Gets the relative etmain of the map.<br/>
    /// <c>ET/etmain/maps/file.map</c> -> <c>ET/etmain/</c>
    /// <c>ET/etmain/myproject.pk3dir/maps/file.map</c> -> <c>ET/etmain/myproject.pk3dir</c>
    /// </summary>
    public string GetMapRoot()
    {
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
        return IOPath.GetRelativePath(AssetDirectories[0].FullName, fullPath);
    }

    private IEnumerable<DirectoryInfo> InitAssetDirectories()
    {
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
}
