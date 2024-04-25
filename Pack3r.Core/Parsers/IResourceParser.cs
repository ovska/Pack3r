namespace Pack3r.Core.Parsers;

public interface IResourceParser
{
    public string Description { get; }

    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    string GetPath(Map map, string? rename = null);
}
