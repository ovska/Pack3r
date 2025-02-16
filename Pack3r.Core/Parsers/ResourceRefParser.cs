using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Progress;

namespace Pack3r.Parsers;

public interface IResourceRefParser
{
    Task ParseReferences(
        Map map,
        CancellationToken cancellationToken);
}

public class ResourceRefParser(
    PackOptions options,
    ILogger<ResourceRefParser> logger,
    IReferenceParser[] parsers,
    IProgressManager progressManager)
    : IResourceRefParser
{
    public async Task ParseReferences(
        Map map,
        CancellationToken cancellationToken)
    {
        int counter = 0;

        using var progress = progressManager.Create(
            "Parsing md3, ase and skin files for assets",
            map.ReferenceResources.Count + map.MiscModels.Count);

        ResourceList handled = [];

        foreach (var resource in map.ReferenceResources.Concat(map.MiscModels.Keys))
        {
            progress.Report(++counter);

            if (!handled.Add(resource))
                continue;

            ResourceList? result = await TryParse(map, resource, cancellationToken);

            if (result is null || result.Count == 0)
                continue;

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
                            item.Value.Equals(remap))
                        {
                            allRemapped = false;
                            break;
                        }
                    }

                    // this resource is remapped on all instances using this
                    if (allRemapped)
                    {
                        result.Remove(item);

                        if (options.OnlySource)
                        {
                            result.Add(
                                new Resource(
                                    item.Value,
                                    item.IsShader,
                                    item.Source,
                                    sourceOnly: true));
                        }
                    }
                }

                foreach (var instance in instances)
                {
                    foreach (var (_, target) in instance.Remaps)
                    {
                        map.Shaders.Add(Resource.Shader((QString)target, in instance.Line));
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in result)
            {
                (item.IsShader ? map.Shaders : map.Resources).Add(item);
            }
        }
    }

    private async Task<ResourceList?> TryParse(
        Map map,
        Resource resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReferenceParser? parser = null;

        foreach (var item in parsers)
        {
            if (item.CanParse(resource.Value))
            {
                parser = item;
                break;
            }
        }

        if (parser is null)
        {
            logger.Warn($"Unsupported reference resource type: {resource}");
            return null;
        }

        bool found = false;

        foreach (var source in map.AssetSources)
        {
            if (source.Assets.TryGetValue(resource.Value, out IAsset? asset))
            {
                // shut up the logger about missing file, but don't bother parsing it since
                // the asset is not packed, its likely the resources aren't either
                if (source.NotPacked)
                {
                    found = true;
                }
                else
                {
                    return await parser.Parse(asset, cancellationToken);
                }
            }
        }

        if (!found)
        {
            logger.Warn($"Can't resolve files used by {parser.Description}, file not found: '{resource.Value}'");
        }

        return null;
    }
}
