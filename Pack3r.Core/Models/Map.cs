using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
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

    private readonly ConcurrentBag<Resource> _allResources = [];

    public void LogResource(in Resource resource)
    {
        if (_options.ReferenceDebug)
            _allResources.Add(resource);
    }

    public IEnumerable<Resource> TryGetAllResources()
    {
        if (_options.ReferenceDebug)
        {
            return _allResources
                .OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Line)
                .ThenBy(r => r.IsShader)
                .ThenBy(r => r.Value, ROMCharComparer.Instance);
        }

        return [];
    }

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

    public string GetRelativeToRoot(string path) => IOPath.GetRelativePath(GetMapRoot(), path);

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

        if (unique.Add(ETMain.FullName))
            yield return ETMain;

        foreach (var pk3dir in ETMain
            .EnumerateDirectories("*.pk3dir", SearchOption.TopDirectoryOnly)
            .OrderByDescending(dir => dir.Name, StringComparer.OrdinalIgnoreCase))
        {
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
            // never ignore etmain
            if (!dir.FullName.EqualsF(ETMain.FullName) && IsExcluded(dir) == SourceFilter.Ignored)
            {
                continue;
            }

            // is exclude support needed for dirs?
            list.Add(new DirectoryAssetSource(dir, _integrityChecker));

            if (_options.LoadPk3s || _options.ExcludeSources.Count > 0)
            {
                foreach (var file in dir.EnumerateFiles("*.pk3", SearchOption.TopDirectoryOnly))
                {
                    var pk3Filter = IsExcluded(file);

                    if (pk3Filter == SourceFilter.Ignored)
                        continue;

                    if (_options.LoadPk3s || pk3Filter == SourceFilter.Excluded)
                        list.Add(new Pk3AssetSource(file.FullName, pk3Filter == SourceFilter.Excluded, _integrityChecker));
                }
            }
        }

        /*
            Same ordering as in AssetDirectories, but:
            1. pak0 is always first
            2. all other pk3s are always last, in reverse alphabetical order
        */
        return list
            .OrderBy(s => s switch
            {
                DirectoryAssetSource d => AssetDirectories.IndexOf(d.Directory),
                Pk3AssetSource p => p.IsPak0 ? int.MinValue : int.MaxValue,
                _ => 0,
            })
            .ThenByDescending(s => IOPath.GetFileNameWithoutExtension(s.RootPath))
            .ToImmutableArray();
    }

    private SourceFilter IsExcluded(FileSystemInfo item)
    {
        scoped ReadOnlySpan<char> dirOrPk3;

        if (item is FileInfo file)
        {
            dirOrPk3 = IOPath.GetFileName(file.FullName.AsSpan());
        }
        else if (item is DirectoryInfo dir)
        {
            dirOrPk3 = IOPath.GetDirectoryName(dir.FullName.AsSpan());
        }
        else
        {
            return SourceFilter.Ignored;
        }

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
