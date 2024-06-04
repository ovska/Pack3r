using System.IO.Compression;
using Pack3r.Extensions;
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
        // remove all misc_models that are referenced elsewhere, as all their assets are 100% needed in that case
        if (map.MiscModels.Count > 0)
        {
            foreach (var res in map.ReferenceResources.Concat(map.Resources))
            {
                if (map.MiscModels.Remove(res) && map.MiscModels.Count == 0)
                    break;
            }
        }

        int counter = 0;
        using var progress = progressManager.Create(
            "Parsing md3, ase and skin files for assets",
            map.ReferenceResources.Count + map.MiscModels.Count);

        foreach (var resource in map.ReferenceResources.Concat(map.MiscModels.Keys))
        {
            progress.Report(++counter);

            var result = await TryParse(map, resource, cancellationToken);

            if (result is null)
            {
                continue;
            }

            // if the misc_model is still present, this resource is ONLY a misc_model
            // and we can try to trim the values
            if (map.MiscModels.TryGetValue(resource, out var instances))
            {
                foreach (var item in result.ToArray()) // loop over a copy
                {
                    bool allRemapped = true;

                    foreach (ReferenceMiscModel instance in instances)
                    {
                        if (!instance.Remaps.TryGetValue(item.Value, out var remap) ||
                            item.Value.EqualsF(remap.Span))
                        {
                            allRemapped = false;
                            break;
                        }
                    }

                    // this resource is remapped on all instances using this
                    if (allRemapped)
                    {
                        result.Remove(item);
                    }
                }
            }

            foreach (var item in result)
            {
                (item.IsShader ? map.Shaders : map.Resources).Add(item.Value);
            }
        }
    }

    private Task<HashSet<Resource>?> TryParse(
        Map map,
        ReadOnlyMemory<char> resource,
        CancellationToken cancellationToken)
    {
        IReferenceParser? parser = null;

        foreach (var item in parsers)
        {
            if (item.CanParse(resource))
            {
                parser = item;
                break;
            }
        }

        if (parser is null)
        {
            logger.Warn($"Unsupported reference resource type: {resource}");
            return Task.FromResult(default(HashSet<Resource>));
        }

        foreach (var source in map.AssetSources)
        {
            if (source is DirectoryAssetSource dirSource)
            {
                if (dirSource.Assets.TryGetValue(resource, out FileInfo? file))
                {
                    return parser.Parse(file.FullName, cancellationToken);
                }
            }
            else if (source is Pk3AssetSource pk3Source)
            {
                if (pk3Source.Assets.TryGetValue(resource, out ZipArchiveEntry? entry))
                {
                    return parser.Parse(entry, pk3Source.ArchivePath, cancellationToken);
                }
            }
        }

        logger.Warn($"Reference resource not found in source(s): '{resource}'");
        return Task.FromResult(default(HashSet<Resource>));
    }
}
