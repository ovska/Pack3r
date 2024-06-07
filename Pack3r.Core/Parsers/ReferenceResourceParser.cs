using Pack3r.Extensions;
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
    PackOptions options,
    ILogger<ReferenceResourceParser> logger,
    IReferenceParser[] parsers,
    IProgressManager progressManager)
    : IReferenceResourceParser
{
    public async Task ParseReferences(
        Map map,
        CancellationToken cancellationToken)
    {
        // remove all misc_models that are referenced elsewhere, as all their assets are 100% needed in that case
/*        if (map.MiscModels.Count > 0)
        {
            foreach (var res in map.ReferenceResources.Concat(map.Resources))
            {
                if (map.MiscModels.Remove(res) && map.MiscModels.Count == 0)
                    break;
            }
        }*/

        // TODO: fix remapped textures on misc_models not showing as source-only textures

        int counter = 0;
        using var progress = progressManager.Create(
            "Parsing md3, ase and skin files for assets",
            map.ReferenceResources.Count + map.MiscModels.Count);

        HashSet<Resource> handled = [];

        foreach (var resource in map.ReferenceResources.Concat(map.MiscModels.Keys))
        {
            progress.Report(++counter);

            if (!handled.Add(resource))
                continue;

            ResourceList? result = await TryParse(map, resource, cancellationToken);

            if (result is null)
            {
                continue;
            }

            // if the misc_model is still present, this resource is ONLY a misc_model
            // and we can try to trim the values
            if (!options.IncludeSource &&
                map.MiscModels.TryGetValue(resource, out var instances))
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
                (item.IsShader ? map.Shaders : map.Resources).Add(item);
            }
        }
    }

    private Task<ResourceList?> TryParse(
        Map map,
        Resource resource,
        CancellationToken cancellationToken)
    {
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
            return Task.FromResult(default(ResourceList));
        }

        foreach (var source in map.AssetSources)
        {
            if (source.Assets.TryGetValue(resource.Value, out IAsset? asset))
            {
                return parser.Parse(asset, cancellationToken);
            }
        }

        logger.Warn($"File not found in sources: '{resource}'");
        return Task.FromResult(default(ResourceList));
    }
}
