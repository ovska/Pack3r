using CommunityToolkit.Diagnostics;
using PPath = System.IO.Path;

namespace Pack3r;

public class MapAssets
{
    /// <summary>
    /// Shaders referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required HashSet<ReadOnlyMemory<char>> Shaders { get; init; }

    /// <summary>
    /// Model/audio/video files referenced by the .map (and possibly its mapscript etc.)
    /// </summary>
    public required HashSet<ReadOnlyMemory<char>> Resources { get; init; }

    /// <summary>
    /// Whether the map has stylelights, and the q3map_mapname.shader file needs to be included.
    /// </summary>
    public required bool HasStyleLights { get; init; }
}

public sealed class Map : MapAssets
{
    /// <summary>
    /// .map file name without extension
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full path to .map
    /// </summary>
    public required string Path { get; init; }

    /// <summary>
    /// ETMain folder
    /// </summary>
    public required DirectoryInfo ETMain { get; init; }

    public string RelativePath(string fullPath)
    {
        if (fullPath.StartsWith(ETMain.FullName))
        {
            return fullPath
                .AsMemory(ETMain.FullName.Length)
                .TrimStart([PPath.DirectorySeparatorChar, PPath.AltDirectorySeparatorChar])
                .ToString();
        }

        // uri.makerelative? ensure etmain has / behind it
        return ThrowHelper.ThrowInvalidOperationException<string>("Invalid fullPath: " + fullPath);
    }
}
