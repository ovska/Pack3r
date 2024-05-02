using System.IO.Compression;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Progress;

namespace Pack3r.Parsers;

public interface IReferenceResourceParser
{
    Task ParseReferences(
        Map map,
        CancellationToken cancellationToken);
}

public class ReferenceResourceParser(
    ILogger<ReferenceResourceParser> logger,
    IEnumerable<IReferenceParser> parsers,
    IProgressManager progressManager)
    : IReferenceResourceParser
{
    public async Task ParseReferences(
        Map map,
        CancellationToken cancellationToken)
    {
        using var progress = progressManager.Create("Parsing md3, ase and skin files for assets", map.ReferenceResources.Count);
        int counter = 0;

        foreach (var resource in map.ReferenceResources)
        {
            progress.Report(++counter);

            foreach (var parser in parsers)
            {
                if (!parser.CanParse(resource))
                    continue;

                HashSet<Resource>? result = null;
                bool fileFound = false;

                foreach (var source in map.AssetSources)
                {
                    if (source is DirectoryAssetSource dirSource)
                    {
                        if (dirSource.Assets.TryGetValue(resource, out FileInfo? file))
                        {
                            fileFound = true;
                            result = await parser.Parse(file.FullName, cancellationToken);
                        }
                    }
                    else if (source is Pk3AssetSource pk3Source)
                    {
                        if (pk3Source.Assets.TryGetValue(resource, out ZipArchiveEntry? entry))
                        {
                            fileFound = true;
                            result = await parser.Parse(entry, pk3Source.ArchivePath, cancellationToken);
                        }
                    }

                    // found
                    if (result is not null)
                    {
                        break;
                    }
                }

                if (!fileFound)
                {
                    logger.Warn($"Reference resource not found: '{resource}'");
                }

                if (result is not null)
                {
                    foreach (var parsed in result)
                    {
                        (parsed.IsShader ? map.Shaders : map.Resources).Add(parsed.Value);
                    }
                }

                break;
            }
        }
    }
}
