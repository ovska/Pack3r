using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SkinParser(ILineReader reader) : IReferenceParser
{
    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".skin");

    public async Task<ResourceList?> Parse(IAsset asset, CancellationToken cancellationToken)
    {
        ResourceList result = [];

        await foreach (var line in reader.ReadLines(asset, default, cancellationToken).WithCancellation(cancellationToken))
        {
            int comma = line.Value.Span.IndexOf(',');

            if (comma >= 0)
            {
                result.Add(
                    new Resource(line.Value[(comma + 1)..].Trim().Trim('"'), isShader: true, in line));
            }
        }

        return result;
    }
}
