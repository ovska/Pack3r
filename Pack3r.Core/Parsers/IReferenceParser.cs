using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IReferenceParser
{
    bool CanParse(ReadOnlyMemory<char> resource);

    Task<ResourceList?> Parse(
        IAsset asset,
        CancellationToken cancellationToken);
}
