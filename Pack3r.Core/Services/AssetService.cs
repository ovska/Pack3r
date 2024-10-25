using System.Collections.Concurrent;
using Pack3r.Extensions;
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
    IResourceParser[] resourceParsers,
    IResourceRefParser referenceParser)
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
            ReferenceResources = assets.ReferenceResources,
            MiscModels = assets.MiscModels,
            Shaders = assets.Shaders,
            HasStyleLights = assets.HasStyleLights,
        };

        if (options.ReferenceDebug)
        {
            string srcMsg = string.Join(
                Environment.NewLine,
                map.AssetSources.Select(src => $"\t{src.RootPath}{(src.IsExcluded ? " (not packed)" : "")}"));
            logger.System($"Using sources for discovery: {Environment.NewLine}{srcMsg}");
        }
        else
        {
            string pk3msg = options.LoadPk3s ? " and pk3s" : "";
            string dirMsg = string.Join(", ", map.AssetDirectories.Select(d => FormatDir(etmainDirectory, d)));
            logger.System($"Using directories{pk3msg} for discovery: {dirMsg}");
        }

        // add bsp
        FileInfo bsp = new(Path.ChangeExtension(map.Path, "bsp"));
        map.RenamableResources.Enqueue(new()
        {
            Name = "bsp",
            AbsolutePath = bsp.FullName,
            ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.bsp")
        });

        // add .map
        if (options.IncludeSource)
        {
            map.RenamableResources.Enqueue(new()
            {
                Name = "map source",
                AbsolutePath = map.Path,
                ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.map"),
            });
        }

        var lightmapDir = new DirectoryInfo(Path.ChangeExtension(map.Path, null));

        if (lightmapDir.Exists && lightmapDir.GetFiles("lm_????.tga") is { Length: > 0 } lmFiles)
        {
            map.HasLightmaps = true;
            bool timestampWarned = false;

            for (int i = 0; i < lmFiles.Length; i++)
            {
                FileInfo? file = lmFiles[i];
                timestampWarned = timestampWarned || logger.CheckAndLogTimestampWarning("Lightmaps", bsp, file);

                map.RenamableResources.Enqueue(new()
                {
                    Name = "lightmaps",
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
            map.RenamableResources.Enqueue(new()
            {
                Name = "objdata",
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
            string lineToReplace = $"map \"{map.Name}\"";

            map.RenamableResources.Enqueue(new()
            {
                Name = "arena",
                AbsolutePath = arena.FullName,
                ArchivePath = Path.Combine("scripts", $"{options.Rename ?? map.Name}.arena"),
                Convert = (line, options) =>
                {
                    if (line.AsSpan().Trim().EqualsF(lineToReplace))
                    {
                        return $"{line.Replace(lineToReplace, $"map \"{options.Rename}\"")} {Global.Disclaimer}";
                    }

                    return line;
                },
            });

            if (options.Rename is not null)
            {
                logger.Info($"Packed {arena.Name} will be modified to account for --rename");
            }
        }
        else
        {
            logger.Info($"Arena skipped, file not found in '{arena.FullName}'");
        }

        FileInfo levelshot = new(Path.Combine(map.GetMapRoot(), "levelshots", $"{map.Name}.tga"));

        if (!levelshot.Exists)
        {
            levelshot = new(Path.ChangeExtension(levelshot.FullName, ".jpg"));
        }

        if (levelshot.Exists)
        {
            map.RenamableResources.Enqueue(new()
            {
                Name = null,
                AbsolutePath = levelshot.FullName,
                ArchivePath = Path.Combine(
                    "levelshots",
                    Path.ChangeExtension(options.Rename ?? map.Name, Path.GetExtension(levelshot.FullName))),
            });
        }
        else
        {
            logger.Info(
                $"Levelshot skipped, file not found in '{Path.GetFileNameWithoutExtension(levelshot.FullName.AsSpan())}.tga/.jpg'");
        }

        await ParseResources(map, cancellationToken);

        return map;
    }

    private async Task ParseResources(Map map, CancellationToken cancellationToken)
    {
        // Parse resources referenced by map/mapscript/soundscript/speakerscript in parallel
        ConcurrentDictionary<Resource, object?> referencedResources = [];

        await Parallel.ForEachAsync(resourceParsers, Global.ParallelOptions(cancellationToken), async (parser, ct) =>
        {
            string path = parser.GetPath(map);

            if (!File.Exists(path))
            {
                logger.Debug($"Skipped {parser.Description}, file '{path}' not found");
                return;
            }

            map.RenamableResources.Enqueue(new()
            {
                Name = parser.Description,
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
            (resource.IsShader ? map.Shaders : map.Resources).Add(resource);
        }

        await referenceParser.ParseReferences(map, cancellationToken);
    }

    private static string FormatDir(DirectoryInfo etmain, DirectoryInfo directory)
    {
        if (etmain.Parent is { } parent)
            return Path.GetRelativePath(parent.FullName, directory.FullName).NormalizePath();

        return directory.FullName.NormalizePath();
    }
}
