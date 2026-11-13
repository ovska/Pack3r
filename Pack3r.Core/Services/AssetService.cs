using System.Collections.Concurrent;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.Services;

public interface IAssetService
{
    Map GetPackingData(CancellationToken cancellationToken);
}

public class AssetService(
    PackOptions options,
    ILogger<AssetService> logger,
    IMapFileParser mapFileParser,
    IResourceParser[] resourceParsers,
    IResourceRefParser referenceParser)
    : IAssetService
{
    public Map GetPackingData(CancellationToken cancellationToken)
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

        MapAssets assets = mapFileParser.ParseMapAssets(options.MapFile.FullName, cancellationToken);

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
            logger.System($"Scanning: {Environment.NewLine}{srcMsg}");
        }
        else
        {
            string pk3msg = options.LoadPk3s ? " and pk3s" : "";
            string dirMsg = string.Join(Environment.NewLine + '\t', map.AssetDirectories.Select(d => FormatDir(etmainDirectory, d)));
            logger.System($"Scanning directories{pk3msg} for assets: {Environment.NewLine}\t{dirMsg}");
        }

        List<RenamableResource> mapFiles = GetMapFiles(options);

        foreach (var resource in mapFiles)
        {
            map.RenamableResources.Enqueue(resource);
        }

        if (!options.OnlySource)
        {
            FileInfo bsp = new(mapFiles[0].AbsolutePath);
            List<RenamableResource> lightmaps = [.. mapFiles.Where(r => r.Name == LightmapsName)];

            if (lightmaps.Count > 0)
            {
                map.HasLightmaps = true;
                bool timestampWarned = false;

                foreach (var lightmap in lightmaps)
                {
                    timestampWarned = timestampWarned ||
                        logger.CheckAndLogTimestampWarning("Lightmaps", bsp, new FileInfo(lightmap.AbsolutePath));
                }
            }
            else
            {
                logger.Trace($"Lightmaps skipped, not found in '{Path.ChangeExtension(map.Path, null)}'");
            }
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
                logger.Info($"Renamed version of '{arena.Name}' be packed");
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
                string searchDest = options.ModFolders is { Count: > 0 } ? "etmain or mod folders" : "etmain";
                logger.Trace($"Tracemap skipped, {map.Name}_tracemap.tga not found in {searchDest}.");
            }
        }

        ParseResources(map, cancellationToken);

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

    private const string LightmapsName = "lightmaps";

    /// <summary>
    /// Files found from the path of the map alone: the bsp and lightmaps, or the .map itself when packing sources.
    /// They don't depend on parsing anything, so they can be compressed while the map is being parsed.
    /// </summary>
    public static List<RenamableResource> GetMapFiles(PackOptions options)
    {
        string mapPath = options.MapFile.FullName;
        string name = options.Rename ?? Path.GetFileNameWithoutExtension(mapPath);

        if (options.OnlySource)
        {
            return
            [
                new()
                {
                    Name = "map source",
                    AbsolutePath = mapPath,
                    ArchivePath = Path.Combine("maps", $"{name}.map"),
                },
            ];
        }

        List<RenamableResource> files =
        [
            new()
            {
                Name = "bsp",
                AbsolutePath = Path.ChangeExtension(mapPath, "bsp"),
                ArchivePath = Path.Combine("maps", $"{name}.bsp"),
            },
        ];

        var lightmapDir = new DirectoryInfo(Path.ChangeExtension(mapPath, null));

        if (lightmapDir.Exists)
        {
            // sorted so the order in the pk3 doesn't depend on the file system
            foreach (var file in lightmapDir.GetFiles("lm_????.tga").OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                files.Add(new()
                {
                    Name = LightmapsName,
                    AbsolutePath = file.FullName,
                    ArchivePath = Path.Combine("maps", name, file.Name),
                });
            }
        }

        return files;
    }

    private void ParseResources(Map map, CancellationToken cancellationToken)
    {
        // Parse resources referenced by map/mapscript/soundscript/speakerscript in parallel
        var renamable = new RenamableResource?[resourceParsers.Length];
        var resources = new List<Resource>[resourceParsers.Length];

        Global.ParallelFor(resourceParsers.Length, cancellationToken, i =>
        {
            IResourceParser parser = resourceParsers[i];
            resources[i] = [];

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

            renamable[i] = new()
            {
                Name = parser.Description,
                AbsolutePath = file.FullName,
                ArchivePath = parser.GetRelativePath(options.Rename ?? map.Name),
            };

            resources[i].AddRange(parser.Parse(file.FullName, cancellationToken));
        });

        // merged in parser order, not in the order they finished, so the pk3 is the same every time
        HashSet<Resource> referencedResources = [];

        for (int i = 0; i < resourceParsers.Length; i++)
        {
            if (renamable[i] is { } resource)
            {
                map.RenamableResources.Enqueue(resource);
            }

            foreach (var item in resources[i])
            {
                if (!referencedResources.Add(item))
                    continue;

                if (item.CanReferenceResources)
                {
                    map.ReferenceResources.Add(item);
                }

                (item.IsShader ? map.Shaders : map.Resources).Add(item);
            }
        }

        referenceParser.ParseReferences(map, cancellationToken);
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
            var newest = files.OrderByDescending(f => f.LastWriteTimeUtc).First();
            logger.Debug(
                $"File '{relativePath.NormalizePath()}' found in multiple mod folders, picking the newest: '{newest.FullName.NormalizePath()}'");
            return newest;
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
