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
            _ => throw new EnvironmentException($"maps-directory should be in etmain or etmain/*.pk3dir: '{options.MapFile.FullName}'"),
        };

        MapAssets assets = await mapFileParser.ParseMapAssets(options.MapFile.FullName, cancellationToken);

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
                map.AssetSources.Select(src => $"\t{src.RootPath}{(src.NotPacked ? " (not packed)" : "")}"));
            logger.System($"Using sources for discovery: {Environment.NewLine}{srcMsg}");
        }
        else
        {
            string pk3msg = options.LoadPk3s ? " and pk3s" : "";
            string dirMsg = string.Join(", ", map.AssetDirectories.Select(d => FormatDir(etmainDirectory, d)));
            logger.System($"Using directories{pk3msg} for discovery: {dirMsg}");
        }

        if (!options.OnlySource)
        {
            // add bsp
            FileInfo bsp = new(Path.ChangeExtension(map.Path, "bsp"));
            map.RenamableResources.Enqueue(new()
            {
                Name = "bsp",
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
                logger.Trace($"Lightmaps skipped, files not found in '{lightmapDir.FullName}'");
            }
        }
        else
        {
            // add .map
            map.RenamableResources.Enqueue(new()
            {
                Name = "map source",
                AbsolutePath = map.Path,
                ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}.map"),
            });
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
            logger.Trace($"Objdata skipped, file not found in '{objdata.FullName}'");
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
            logger.Trace($"Arena skipped, file not found in '{arena.FullName}'");
        }

        if (TryGetLevelshot(out FileInfo levelshot))
        {
            map.RenamableResources.Enqueue(new()
            {
                Name = "levelshot image",
                AbsolutePath = levelshot.FullName,
                ArchivePath = Path.Combine(
                    "levelshots",
                    Path.ChangeExtension(options.Rename ?? map.Name, Path.GetExtension(levelshot.FullName))),
            });
        }
        else
        {
            logger.Trace(
                $"Levelshot skipped, file not found in '{Path.GetFileNameWithoutExtension(levelshot.FullName.AsSpan())}.tga/.jpg'");
        }

        if (!options.OnlySource)
        {
            if (FindFileFromMods(map, Path.Combine("maps", $"{map.Name}_tracemap.tga")) is { Exists: true } tracemap)
            {
                map.RenamableResources.Enqueue(new()
                {
                    Name = "tracemap",
                    AbsolutePath = tracemap.FullName,
                    ArchivePath = Path.Combine("maps", $"{options.Rename ?? map.Name}_tracemap.tga")
                });
            }
            else
            {
                logger.Trace($"Tracemap skipped, not found in any directory.");
            }
        }

        await ParseResources(map, cancellationToken);

        return map;

        bool TryGetLevelshot(out FileInfo levelshot)
        {
            levelshot = new FileInfo(Path.Combine(map.GetMapRoot(), "levelshots", $"{map.Name}.tga"));

            if (levelshot.Exists)
            {
                return true;
            }

            levelshot = new FileInfo(Path.Combine(map.GetMapRoot(), "levelshots", $"{map.Name}.jpg"));
            return levelshot.Exists;
        }
    }

    private async Task ParseResources(Map map, CancellationToken cancellationToken)
    {
        // Parse resources referenced by map/mapscript/soundscript/speakerscript in parallel
        ConcurrentDictionary<Resource, object?> referencedResources = [];

        await Parallel.ForEachAsync(resourceParsers, Global.ParallelOptions(cancellationToken), async (parser, ct) =>
        {
            string relativePath = parser.GetRelativePath(map.Name);
            FileInfo? file = parser.SearchModDirectories
                ? FindFileFromMods(map, relativePath)
                : new FileInfo(Path.Combine(map.GetMapRoot(), relativePath));

            if (file is not { Exists: true })
            {
                logger.Log(
                    options.ReferenceDebug ? LogLevel.Warn : LogLevel.Debug,
                    $"Skipped {parser.Description}, file '{relativePath}' not found{(parser.SearchModDirectories ? " in any sources" : "")}");
                return;
            }

            map.RenamableResources.Enqueue(new()
            {
                Name = parser.Description,
                AbsolutePath = file.FullName,
                ArchivePath = parser.GetRelativePath(options.Rename ?? map.Name),
            });

            await foreach (var resource in parser.Parse(file.FullName, ct).ConfigureAwait(false))
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

    private FileInfo? FindFileFromMods(Map map, string relativePath)
    {
        var files = EnumerateFolders()
            .Select(dir => new FileInfo(Path.Combine(dir, relativePath)))
            .Where(file => file.Exists)
            .ToList();

        if (files.Count == 0)
        {
            return null;
        }

        if (files.Count > 1)
        {
            logger.Debug($"File '{relativePath.NormalizePath()}' found in multiple folders, picking the newest: '{files[0].FullName}'");
        }

        return files[0];

        IEnumerable<string> EnumerateFolders()
        {
            if (options.ModFolders.Count > 0 && map.ETMain.Parent?.FullName is { Length: > 0 } etInstallFolder)
            {
                foreach (var mod in options.ModFolders)
                {
                    yield return Path.Combine(etInstallFolder, mod);
                }
            }

            yield return map.GetMapRoot();

            if (map.GetMapRoot().NormalizePath() != map.ETMain.FullName.NormalizePath())
            {
                yield return map.ETMain.FullName;
            }
        }
    }

    private static string FormatDir(DirectoryInfo etmain, DirectoryInfo directory)
    {
        if (etmain.Parent is { } parent)
            return Path.GetRelativePath(parent.FullName, directory.FullName).NormalizePath();

        return directory.FullName.NormalizePath();
    }
}
