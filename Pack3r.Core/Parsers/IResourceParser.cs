using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IResourceParser
{
    public string Description { get; }

    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the archive path of the resource, e.g. <c>maps/mapName.script</c>
    /// </summary>
    string GetRelativePath(string mapName);
}
