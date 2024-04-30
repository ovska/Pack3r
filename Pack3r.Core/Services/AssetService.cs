using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.Services;

public interface IAssetService
{
    Task<Map> GetPackingData(CancellationToken cancellationToken);
}

public class AssetService(
    PackOptions options,
    ILogger<AssetService> logger,
    IMapFileParser mapFileParser,
    IEnumerable<IResourceParser> resourceParsers)
    : IAssetService
{
    public async Task<Map> GetPackingData(CancellationToken cancellationToken)
    {
        if (options.MapFile.Directory is not { Name: "maps", Parent: DirectoryInfo mapsParent })
        {
            throw new EnvironmentException($".map file not in maps-directory: '{options.MapFile.FullName}'");
        }

        DirectoryInfo etmainDirectory = mapsParent switch
        {
            { Name: "etmain" } => mapsParent,
            { Parent: { Name: "etmain" } pk3dirParent } when mapsParent.Name.HasExtension(".pk3dir") => pk3dirParent,
            _ => throw new EnvironmentException($"maps-directory should be in etmain or a pk3dir in etmain: '{options.MapFile.FullName}'"),
        };

        MapAssets assets = await mapFileParser.ParseMapAssets(options.MapFile.FullName, cancellationToken).ConfigureAwait(false);

        Map map = new(options)
        {
            Name = Path.GetFileNameWithoutExtension(options.MapFile.FullName),
            Path = options.MapFile.FullName,
            ETMain = etmainDirectory,
            Resources = assets.Resources,
            Shaders = assets.Shaders,
            HasStyleLights = assets.HasStyleLights,
            RenamableResources = [],
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

            map.RenamableResources.Add(new()
            {
                AbsolutePath = path,
                ArchivePath = map.GetRelativeToRoot(parser.GetPath(map, options.Rename))
            });

            await foreach (var resource in parser.Parse(path, ct).ConfigureAwait(false))
            {
                referencedResources.TryAdd(resource, null);
            }
        }).ConfigureAwait(false);

        foreach (var (resource, _) in referencedResources)
        {
            (resource.IsShader ? map.Shaders : map.Resources).Add(resource.Value);
        }

        // add .map
        if (options.IncludeSource)
        {
            map.RenamableResources.Add(new()
            {
                AbsolutePath = map.Path,
                ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.map"),
            });
        }

        // add bsp
        FileInfo bsp = new(Path.ChangeExtension(map.Path, "bsp"));
        map.RenamableResources.Add(new()
        {
            AbsolutePath = bsp.FullName,
            ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.bsp")
        });

        var lightmapDir = new DirectoryInfo(Path.ChangeExtension(map.Path, null));

        if (lightmapDir.Exists && lightmapDir.GetFiles("lm_????.tga") is { Length: > 0 } lmFiles)
        {
            map.HasLightmaps = true;
            bool timestampWarned = false;

            for (int i = 0; i < lmFiles.Length; i++)
            {
                FileInfo? file = lmFiles[i];
                timestampWarned = timestampWarned || logger.CheckAndLogTimestampWarning("Lightmap", bsp, file);

                map.RenamableResources.Add(new()
                {
                    AbsolutePath = file.FullName,
                    ArchivePath = Path.Combine("maps", options.Rename ?? map.Name, file.Name)
                });
            }
        }
        else
        {
            logger.Info($"Lightmaps skipped, files not found in '{lightmapDir.FullName}'");
        }

        var objdata = new FileInfo(Path.ChangeExtension(map.Path, "objdata"));

        if (objdata.Exists)
        {
            map.RenamableResources.Add(new()
            {
                AbsolutePath = objdata.FullName,
                ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.objdata")
            });
        }
        else
        {
            logger.Info($"Objdata skipped, file not found in '{objdata.FullName}'");
        }

        var arena = new FileInfo(Path.Combine(map.GetMapRoot(), "scripts", $"{map.Name}.arena"));

        if (arena.Exists)
        {
            map.RenamableResources.Add(new()
            {
                AbsolutePath = arena.FullName,
                ArchivePath = Path.Combine("scripts", $"{options.Rename ?? map.Name}.arena"),
                Convert = static (line, options) => Tokens.ArenaMapName().IsMatch(line) ? $"map \"{options.Rename}\"" : line,
            });
        }
        else
        {
            logger.Info($"Arena skipped, file not found in '{arena.FullName}'");
        }

        // TODO :levelshots

        return map;
    }
}
