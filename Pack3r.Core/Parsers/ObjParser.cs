using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public partial class ObjParser(
    ILineReader reader) : IReferenceParser
{
    public string Description => "model";

    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".obj");

    public async Task<ResourceList?> Parse(IAsset asset, CancellationToken cancellationToken)
    {
        ResourceList resources = [];

        await foreach (var line in reader.ReadLines(asset, cancellationToken))
        {
            const string UseMaterial = "usemtl ";

            if (line.HasValue && line.Value.Span.StartsWithF(UseMaterial))
            {
                QString materialName = ((QString)line.Value[UseMaterial.Length..].Trim()).TrimTextureExtension();

                if (!materialName.IsEmpty)
                {
                    resources.Add(Resource.Shader(materialName, in line));
                }
            }
        }

        return resources;
    }
}
