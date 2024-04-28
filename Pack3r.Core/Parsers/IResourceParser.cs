using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IResourceParser
{
    public string Description { get; }

    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    string GetPath(Map map, string? rename = null);
}
