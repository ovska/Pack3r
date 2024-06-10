using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IReferenceParser
{
    string Description { get; }

    bool CanParse(ReadOnlyMemory<char> resource);

    Task<ResourceList?> Parse(
        IAsset asset,
        CancellationToken cancellationToken);
}
