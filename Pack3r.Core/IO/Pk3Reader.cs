using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Logging;

namespace Pack3r.IO;

public sealed class Pk3Contents(string path)
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

                await ProcessItem(
                    contents,
                    entry.FullName,
                    new ResourcePath(path, entry),
                    cancellationToken);
            }

            return contents;
        }
        catch (FileNotFoundException)
        {
            logger.Warn($"File {path} not found, skipping built-in asset discovery");
            return new Pk3Contents(path);
        }
    }

    private async ValueTask ProcessItem(
        Pk3Contents contents,
        string relativePath,
        ResourcePath resourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Tokens.ShaderPath().IsMatch(relativePath))
        {
            await foreach (var shader in shaderParser.Parse(resourcePath, cancellationToken))
            {
                contents.Shaders.Add(shader.Name);
            }
        }
        else
        {
            var extension = resourcePath.Path.GetTextureExtension();

            // allow using jpg/tga as shaderless tex
            if (extension != TextureExtension.Other)
            {
                contents.Shaders.Add(relativePath.AsMemory(..^4));
            }

            // jpg textures can be referenced with tga paths in shaders
            if (extension == TextureExtension.Jpg)
            {
                contents.Resources.Add(Path.ChangeExtension(relativePath, "tga").AsMemory());
            }

            contents.Resources.Add(relativePath.AsMemory());
        }
    }
}
