using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SkinParser(ILineReader reader) : IReferenceParser
{
    public string Description => "skin";

    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".skin");

    public ResourceList? Parse(IAsset asset, CancellationToken cancellationToken)
    {
        ResourceList result = [];

        foreach (var line in reader.ReadLines(asset, cancellationToken))
        {
            int comma = line.Value.IndexOf((byte)',');

            if (comma >= 0)
            {
                var value = line.Value[(comma + 1)..].TrimWhitespace().Trim((byte)'"');
                result.Add(new Resource(value.ToQString(), isShader: true, line.ToLine()));
            }
        }

        return result;
    }
}
