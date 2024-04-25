using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Pack3r.Core.Parsers;
using Pack3r.IO;

namespace Pack3r;

public sealed class PackingData
{
    public required Map Map { get; init; }
    public required Pk3Contents Pak0 { get; init; }
}

public interface IAssetService
{
    Task<PackingData> GetPackingData(string path, CancellationToken cancellationToken);
}

public class AssetService(
    ILogger<AssetService> logger,
    IPk3Reader pk3Reader,
    IMapFileParser mapFileParser,
    IEnumerable<IResourceParser> resourceParsers)
    : IAssetService
{
    public async Task<PackingData> GetPackingData(string path, CancellationToken cancellationToken)
    {
        // normalize
        path = Path.GetFullPath(new Uri(path).LocalPath);

        if (Path.GetExtension(path) != ".map")
            throw new ArgumentException($"Path not to a .map file: {path}", nameof(path));

        var bspPath = Path.ChangeExtension(path, "bsp");

        if (!File.Exists(bspPath))
            throw new InvalidOperationException($"Compiled bsp-file '{bspPath}' not found for map!");

        var mapsDirectory = Directory.GetParent(path);
        var etmainDirectory = mapsDirectory is null ? null : Directory.GetParent(mapsDirectory.FullName);

        if (mapsDirectory is not { Name: "maps" } || etmainDirectory is not { Name: "etmain" })
        {
            throw new InvalidOperationException($".map file not in etmain/maps: '{path}'");
        }

        // start pak0 parse task in background
        var pak0task = GetBuiltinContents(etmainDirectory, cancellationToken);

        MapAssets assets = await mapFileParser.ParseMapAssets(path, cancellationToken).ConfigureAwait(false);

        Map map = new()
        {
            Name = Path.GetFileNameWithoutExtension(path),
            Path = path,
            ETMain = etmainDirectory,
            Resources = assets.Resources,
            Shaders = assets.Shaders,
            HasStyleLights = assets.HasStyleLights,
        };

        // Parse resources referenced by map/mapscript/soundscript/speakerscript in parallel
        ConcurrentDictionary<Resource, object?> referencedResources = [];

        await Parallel.ForEachAsync(resourceParsers, cancellationToken, async (parser, ct) =>
        {
            string path = parser.GetPath(map);

            if (!File.Exists(path))
            {
                logger.LogInformation("{description} file '{path}' not found, skipping...",
                    CultureInfo.InvariantCulture.TextInfo.ToTitleCase(parser.Description),
                    map.RelativePath(path));
                return;
            }

            await foreach (var resource in parser.Parse(path, ct).ConfigureAwait(false))
            {
                referencedResources.TryAdd(resource, null);
            }
        }).ConfigureAwait(false);

        foreach (var (resource, _) in referencedResources)
        {
            (resource.IsShader ? map.Shaders : map.Resources).Add(resource.Value);
        }

        return new PackingData
        {
            Map = map,
            Pak0 = await pak0task.ConfigureAwait(false),
        };
    }

    private async Task<Pk3Contents> GetBuiltinContents(
        DirectoryInfo etmain,
        CancellationToken cancellationToken)
    {
        var pak0task = pk3Reader.ReadPk3(Path.Combine(etmain.FullName, "pak0.pk3"), cancellationToken);
        Pk3Contents? mapObjects = null;

        try
        {
            var sdMapObjects = Path.Combine(etmain.FullName, "sd-mapobjects.pk3");

            if (File.Exists(sdMapObjects))
            {
                mapObjects = await pk3Reader.ReadPk3(sdMapObjects, cancellationToken);
            }
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }

        var pak0 = await pak0task;

        if (mapObjects is { Resources: var resources, Shaders: var shaders })
        {
            pak0.Shaders.UnionWith(shaders);
            pak0.Resources.UnionWith(resources);
        }

        return pak0;
    }
}
