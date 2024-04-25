using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Pack3r.Extensions;

namespace Pack3r.IO;

public readonly struct Pk3Contents(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);

    public HashSet<ReadOnlyMemory<char>> Shaders { get; } = new(ROMCharComparer.Instance);
    public HashSet<ReadOnlyMemory<char>> Resources { get; } = new(ROMCharComparer.Instance);
}

public interface IPk3Reader
{
    Task<Pk3Contents> ReadPk3(
        string path,
        CancellationToken cancellationToken);
}

public class Pk3Reader(
    ILogger<Pk3Reader> logger,
    IShaderParser shaderParser)
    : IPk3Reader
{
    public async Task<Pk3Contents> ReadPk3(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);

            var contents = new Pk3Contents(path);

            foreach (var entry in archive.Entries)
            {
                // skip directories
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                cancellationToken.ThrowIfCancellationRequested();

                if (Tokens.ShaderPath().IsMatch(entry.FullName))
                {
                    var entryPath = new ResourcePath(path, entry);

                    await foreach (var shader in shaderParser.Parse(entryPath, cancellationToken))
                    {
                        contents.Shaders.Add(shader.Name);
                    }
                }
                else
                {
                    var extension = GetExtension(entry);

                    // allow using jpg/tga as shaderless tex
                    if (extension != Extension.Other)
                    {
                        contents.Shaders.Add(entry.FullName.AsMemory(..^4));
                    }

                    // jpg textures can be referenced with tga paths in shaders
                    if (extension == Extension.Jpg)
                    {
                        contents.Resources.Add(Path.ChangeExtension(entry.FullName, "tga").AsMemory());
                    }

                    contents.Resources.Add(entry.FullName.AsMemory());
                }
            }

            return contents;
        }
        catch (FileNotFoundException)
        {
            logger.LogWarning("File {path} not found, skipping built-in asset discovery!", path);
            return new Pk3Contents(path);
        }
    }

    private static Extension GetExtension(ZipArchiveEntry entry)
    {
        var extension = Path.GetExtension(entry.FullName.AsSpan());

        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            return Extension.Tga;

        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return Extension.Jpg;

        return Extension.Other;
    }

    private enum Extension {  Other, Tga, Jpg }
}
