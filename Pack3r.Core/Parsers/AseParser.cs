using Pack3r.Core.Parsers;

namespace Pack3r.Parsers;

public class AseParser : IResourceParser
{
    public string Description => null!;

    public string GetPath(Map map, string? rename = null) => throw new NotImplementedException();

    public IAsyncEnumerable<Resource> Parse(string path, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
