using Pack3r.Extensions;

namespace Pack3r.Models;

public class MapAssets
{
    /// <summary>
    /// Shaders referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required ResourceList Shaders { get; init; }

    /// <summary>
    /// Model/audio/video files referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required ResourceList Resources { get; init; }

    /// <summary>
    /// Models and skins that may reference other files.
    /// </summary>
    public required ResourceList ReferenceResources { get; init; }

    /// <summary>
    /// misc_models which may have remaps
    /// </summary>
    public required Dictionary<Resource, List<ReferenceMiscModel>> MiscModels { get; init; }

    /// <summary>
    /// Whether the map has stylelights, and the q3map_mapname.shader file needs to be included.
    /// </summary>
    public required bool HasStyleLights { get; init; }
}

public sealed class ReferenceMiscModel
{
    public ReadOnlyMemory<char> Model { get; }
    public Dictionary<ReadOnlyMemory<char>, ReadOnlyMemory<char>> Remaps { get; }

    public ReferenceMiscModel(
        ReadOnlyMemory<char> model,
        IEnumerable<(ReadOnlyMemory<char>, ReadOnlyMemory<char>)> entitydata)
    {
        Model = model;

        Remaps = new(ROMCharComparer.Instance);

        Span<Range> ranges = stackalloc Range[16];

        foreach (var (key, value) in entitydata)
        {
            if (!key.StartsWithF("_remap"))
            {
                continue;
            }

            int count = value.Span.Split(ranges, ';', StringSplitOptions.TrimEntries);

            if (count != 2)
                continue;

            // TODO: check if the first or last key is preserved by Q3Map2
            Remaps[value[ranges[0]]] = value[ranges[1]];
        }
    }
}
