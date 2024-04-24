namespace Pack3r;

public interface IResourceParser
{
    IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken);

    string GetPath(Map map, string? rename = null);
}
