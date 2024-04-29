using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;

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
                    path,
                    entry,
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
        string archivePath,
        ZipArchiveEntry archiveEntry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new NotSupportedException();
        //if (Tokens.ShaderPath().IsMatch(archiveEntry.FullName))
        //{
        //    await foreach (var shader in shaderParser.Parse(archivePath, archiveEntry, cancellationToken))
        //    {
        //        contents.Shaders.Add(shader.Name);
        //    }
        //}
        //else
        //{
        //    var extension = archiveEntry.FullName.GetTextureExtension();

        //    // TODO: fix hack
        //    // allow using jpg/tga as shaderless tex
        //    if (extension != TextureExtension.Other)
        //    {
        //        contents.Shaders.Add(archiveEntry.FullName.AsMemory(..^4));
        //    }

        //    // jpg textures can be referenced with tga paths in shaders
        //    if (extension == TextureExtension.Jpg)
        //    {
        //        contents.Resources.Add(Path.ChangeExtension(archiveEntry.FullName, "tga").AsMemory());
        //    }

        //    contents.Resources.Add(archiveEntry.FullName.AsMemory());
        //}
    }
}
