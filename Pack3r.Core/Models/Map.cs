using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Services;
using IOPath = System.IO.Path;

namespace Pack3r.Models;

public sealed class Map : MapAssets, IDisposable
{
    private bool _disposed;

    public Map(PackOptions options, IIntegrityChecker integrityChecker)
    {
        _options = options;
        _integrityChecker = integrityChecker;
        _assetDirs = new(() => InitAssetDirectories().ToImmutableArray(), LazyThreadSafetyMode.ExecutionAndPublication);
        _assetSrcs = new(InitAssetSources, LazyThreadSafetyMode.ExecutionAndPublication);
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

    public bool HasLightmaps { get; set; }

    /// <summary>
    /// Renamable resources (mapscript etc)
    /// </summary>
    public ConcurrentBag<RenamableResource> RenamableResources { get; } = [];

    public ImmutableArray<DirectoryInfo> AssetDirectories => _assetDirs.Value;
    public ImmutableArray<AssetSource> AssetSources => _assetSrcs.Value;

    private readonly PackOptions _options;
    private readonly IIntegrityChecker _integrityChecker;
    private string? _root;
    private readonly Lazy<ImmutableArray<DirectoryInfo>> _assetDirs;
    private readonly Lazy<ImmutableArray<AssetSource>> _assetSrcs;

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

        ReadOnlySpan<char> mapsDir = IOPath.GetDirectoryName(Path.AsSpan());

        if (!mapsDir.IsEmpty)
        {
            ReadOnlySpan<char> etmainOrPk3dir = IOPath.GetDirectoryName(mapsDir);

            if (!etmainOrPk3dir.IsEmpty)
            {
                return _root ??= IOPath.GetFullPath(etmainOrPk3dir.ToString());
            }
        }

        throw new UnreachableException($"Could not get map root: {Path}");
    }

    public string GetRelativeToRoot(string path) => IOPath.GetRelativePath(GetMapRoot(), path).NormalizePath();

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

        /*
        try to mimic ET's priority order:
        1. directory where the map resides
        2. etmain (if not 1)
        3. pk3dirs in reverse alphabetical order
        */

        unique.Add(GetMapRoot());
        yield return new DirectoryInfo(GetMapRoot());

        // try to add etmain second in case .map was in a pk3dir
        if (unique.Add(ETMain.FullName))
            yield return ETMain;

        foreach (var pk3dir in ETMain
            .EnumerateDirectories("*.pk3dir", SearchOption.TopDirectoryOnly)
            .OrderByDescending(dir => dir.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (IsExcluded(pk3dir) == SourceFilter.Ignored)
                continue;

            if (unique.Add(pk3dir.FullName))
                yield return pk3dir;
        }
    }

    private ImmutableArray<AssetSource> InitAssetSources()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var list = new List<AssetSource>();

        foreach (var dir in AssetDirectories)
        {
            var dirFilter = IsExcluded(dir);

            // never ignore etmain
            if (dirFilter == SourceFilter.Ignored && !dir.FullName.EqualsF(ETMain.FullName))
            {
                continue;
            }

            list.Add(new DirectoryAssetSource(dir, isExcluded: dirFilter == SourceFilter.Excluded, _integrityChecker));

            // pk3s need to be loaded always if there are pk3/dir excludes
            if (_options.LoadPk3s || _options.ExcludeSources.Count > 0)
            {
                foreach (var file in dir.EnumerateFiles("*.pk3", SearchOption.TopDirectoryOnly))
                {
                    var pk3Filter = IsExcluded(file);

                    if (pk3Filter == SourceFilter.Ignored)
                        continue;

                    // exclude all pk3s in an excluded pk3dir
                    bool pk3isExcluded = dir.FullName.HasExtension("pk3dir")
                        ? (dirFilter == SourceFilter.Excluded || pk3Filter == SourceFilter.Excluded)
                        : pk3Filter == SourceFilter.Excluded;

                    if (_options.LoadPk3s || pk3isExcluded)
                        list.Add(new Pk3AssetSource(file.FullName, isExcluded: pk3isExcluded, _integrityChecker));
                }
            }
        }

        /*
            Same ordering as in AssetDirectories, but:
            1. pak0 is always first (and other excluded sources)
            2. all other pk3s are always last, in reverse alphabetical order
        */
#pragma warning disable IDE0305 // Simplify collection initialization
        byte[] sortKeys = new byte[512];

        return list
            .OrderByDescending(s => (s.IsExcluded, s.Name.EqualsF("pak0.pk3"))) // pak0 first, then other excludes
            .ThenBy(s => s is DirectoryAssetSource d ? AssetDirectories.IndexOf(d.Directory) : int.MaxValue)
            .ThenByDescending(s => IOPath.GetFileNameWithoutExtension(s.RootPath))
            .ToImmutableArray();
#pragma warning restore IDE0305 // Simplify collection initialization
    }

    private SourceFilter IsExcluded(FileSystemInfo item)
    {
        ReadOnlySpan<char> dirOrPk3 = IOPath.GetFileName(item.FullName.AsSpan());

        foreach (var value in _options.IgnoreSources)
        {
            if (dirOrPk3.EqualsF(value))
                return SourceFilter.Ignored;
        }

        foreach (var value in _options.ExcludeSources)
        {
            if (dirOrPk3.EqualsF(value))
                return SourceFilter.Excluded;
        }

        return SourceFilter.None;
    }

    private enum SourceFilter { None, Excluded, Ignored }

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
