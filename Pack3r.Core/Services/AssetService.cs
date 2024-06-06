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
    IReferenceResourceParser referenceParser,
    IIntegrityChecker integrityChecker)
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

        Map map = new(options, integrityChecker)
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
                map.AssetSources.Select(src => $"\t{src.RootPath}{(src.IsPak0 ? " (not packed)" : "")}"));
            logger.System($"Using source(s) for discovery: {Environment.NewLine}{srcMsg}");

            foreach (var res in map.Resources)
                map.LogResource(new Resource(res, false, options.MapFile.FullName));

            foreach (var res in map.ReferenceResources)
                map.LogResource(new Resource(res, false, options.MapFile.FullName));

            foreach (var res in map.Shaders)
                map.LogResource(new Resource(res, true, options.MapFile.FullName));

            foreach (var (mm, _) in map.MiscModels)
                map.LogResource(new Resource(mm, false, options.MapFile.FullName));
        }
        else
        {
            string pk3msg = options.LoadPk3s ? " and pk3s" : "";
            string dirMsg = string.Join(", ", map.AssetDirectories.Select(d => FormatDir(etmainDirectory, d)));
            logger.System($"Using directories{pk3msg} for discovery: {dirMsg}");
        }

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
                map.LogResource(in resource);
            }
        }).ConfigureAwait(false);

        foreach (var (resource, _) in referencedResources)
        {
            (resource.IsShader ? map.Shaders : map.Resources).Add(resource.Value);
        }

        await referenceParser.ParseReferences(map, cancellationToken);

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
                timestampWarned = timestampWarned || logger.CheckAndLogTimestampWarning("Lightmaps", bsp, file);

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
            string lineToReplace = $"map \"{map.Name}\"";

            map.RenamableResources.Add(new()
            {
                AbsolutePath = arena.FullName,
                ArchivePath = Path.Combine("scripts", $"{options.Rename ?? map.Name}.arena"),
                Convert = (line, options) =>
                {
                    if (line.AsSpan().Trim().EqualsF(lineToReplace))
                    {
                        return line.Replace(lineToReplace, $"map \"{options.Rename}\"");
                    }

                    return line;
                },
            });
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
            map.RenamableResources.Add(new()
            {
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

        // TODO: levelshots_cc

        return map;
    }

    private static string FormatDir(DirectoryInfo etmain, DirectoryInfo directory)
    {
        if (etmain.Parent is { } parent)
            return Path.GetRelativePath(parent.FullName, directory.FullName).NormalizePath();

        return directory.FullName.NormalizePath();
    }
}
