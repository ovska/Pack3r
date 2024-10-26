using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IResourceParser
{
    public string Description { get; }

    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the file can be found in any directory, instead of just the same root dir where .map file is.
    /// </summary>
    bool SearchModDirectories { get; }

    /// <summary>
    /// Returns the archive path of the resource, e.g. <c>maps/mapName.script</c>
    /// </summary>
    string GetRelativePath(string mapName);
}
