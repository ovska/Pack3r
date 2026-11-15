using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public partial class ObjParser(
    ILineReader reader) : IReferenceParser
{
    public string Description => "model";

    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".obj");

    public ResourceList? Parse(IAsset asset, CancellationToken cancellationToken)
    {
        ResourceList resources = [];

        foreach (var line in reader.ReadLines(asset, cancellationToken))
        {
            ReadOnlySpan<byte> useMaterial = "usemtl "u8;

            if (line.HasValue && line.Value.StartsWithIgnoreCase(useMaterial))
            {
                QString materialName = line.Value[useMaterial.Length..].TrimWhitespace().ToQString().TrimTextureExtension();

                if (!materialName.IsEmpty)
                {
                    resources.Add(Resource.Shader(materialName, line.ToLine()));
                }
            }
        }

        return resources;
    }
}
