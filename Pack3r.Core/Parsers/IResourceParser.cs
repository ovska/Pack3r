namespace Pack3r.Core.Parsers;

public interface IResourceParser
{
    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    string GetPath(Map map, string? rename = null);
}
