using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Services;

public sealed class PackingData
{
    public required Map Map { get; init; }
    public required Pk3Contents Pak0 { get; init; }
}

public interface IAssetService
{
    Task<PackingData> GetPackingData(CancellationToken cancellationToken);
}

public class AssetService(
    PackOptions options,
    ILogger<AssetService> logger,
    IPk3Reader pk3Reader,
    IMapFileParser mapFileParser,
    IEnumerable<IResourceParser> resourceParsers)
    : IAssetService
{
    public async Task<PackingData> GetPackingData(CancellationToken cancellationToken)
    {
        if (options.MapFile.Directory is not { Name: "maps", Parent: DirectoryInfo mapsParent })
        {
            throw new EnvironmentException($".map file not in maps-directory: '{options.MapFile.FullName}'");
        }

        DirectoryInfo etmainDirectory;

        if (mapsParent.Name is "etmain")
        {
            etmainDirectory = mapsParent;
        }
        else if (mapsParent.Name.GetExtension().EqualsF(".pk3dir") &&
            mapsParent.Parent is { Name: "etmain" } pk3dirParent)
        {
            etmainDirectory = pk3dirParent;
        }
        else
        {
            throw new EnvironmentException($"maps-directory should be in etmain or a pk3dir in etmain: '{options.MapFile.FullName}'");
        }

        // start pak0 parse task in background
        var pk3task = GetBuiltinContents(etmainDirectory, cancellationToken);

        MapAssets assets = await mapFileParser.ParseMapAssets(options.MapFile.FullName, cancellationToken).ConfigureAwait(false);

        Map map = new()
        {
            Name = Path.GetFileNameWithoutExtension(options.MapFile.FullName),
            Path = options.MapFile.FullName,
            ETMain = etmainDirectory,
            Resources = assets.Resources,
            Shaders = assets.Shaders,
            HasStyleLights = assets.HasStyleLights,
        };

        logger.System($"Using directories for discovery: {string.Join(", ", map.AssetDirectories.Select(d => d.FullName))}");

        // Parse resources referenced by map/mapscript/soundscript/speakerscript in parallel
        ConcurrentDictionary<Resource, object?> referencedResources = [];

        await Parallel.ForEachAsync(resourceParsers, cancellationToken, async (parser, ct) =>
        {
            string path = parser.GetPath(map);

            if (!File.Exists(path))
            {
                logger.Debug($"Skipped {parser.Description}, file '{path}' not found");
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
            Pak0 = await pk3task.ConfigureAwait(false),
        };
    }

    private async Task<Pk3Contents> GetBuiltinContents(
        DirectoryInfo etmain,
        CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();

        var pak0task = pk3Reader.ReadPk3(Path.Combine(etmain.FullName, "pak0.pk3"), cancellationToken);

        List<Task<Pk3Contents?>> auxiliary =
        [
            TryLoadPk3(Path.Combine(etmain.FullName, "sd-mapobjects.pk3"))
        ];

        if (etmain.Parent is { } etfolder &&
            etfolder.GetDirectories(options.ETJumpDir ?? "etjump") is { Length: 1 } etjumpdirs &&
            etjumpdirs[0].GetFiles("etjump-*.pk3").OrderByDescending(f => f.Name).FirstOrDefault() is { } etjumpPk3)
        {
            auxiliary.Add(TryLoadPk3(etjumpPk3.FullName));
        }

        var pak0 = await pak0task;

        List<string> pakNames = [pak0.Name];

        foreach (var auxTask in auxiliary)
        {
            if (await auxTask is { } pk3)
            {
                pakNames.Add(pk3.Name);
                pak0.Shaders.UnionWith(pk3.Shaders);
                pak0.Resources.UnionWith(pk3.Resources);
            }
        }

        timer.Stop();

        logger.System($"{string.Join(", ", pakNames)} processed successfully in {timer.ElapsedMilliseconds} ms");

        return pak0;

        async Task<Pk3Contents?> TryLoadPk3(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return await pk3Reader.ReadPk3(path, cancellationToken);
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            return null;
        }
    }
}
