using System.IO.Compression;
using Microsoft.VisualBasic;
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

    public async Task<Pk3Contents> ReadPk3Dir(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var contents = new Pk3Contents(path);

            var dir = new DirectoryInfo(path);

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MatchType = MatchType.Simple,
                BufferSize = 8192,
                AttributesToSkip = FileAttributes.Directory | FileAttributes.Offline | FileAttributes.System,
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.PlatformDefault,
            };

            foreach (var file in dir.EnumerateFiles("*", options))
            {
                await ProcessItem(
                    contents,
                    Path.GetRelativePath(path, file.FullName),
                    new ResourcePath(file.FullName),
                    cancellationToken);
            }

            return contents;
        }
        catch (DirectoryNotFoundException)
        {
            logger.Warn($"Pk3dir {path} not found, skipping asset discovery");
            return new Pk3Contents(path);
        }
    }

    private async ValueTask ProcessItem(
        Pk3Contents contents,
        string relativePath,
        ResourcePath path,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Tokens.ShaderPath().IsMatch(relativePath))
        {
            await foreach (var shader in shaderParser.Parse(path, cancellationToken))
            {
                contents.Shaders.Add(shader.Name);
            }
        }
        else
        {
            var extension = GetExtension(path.Path);

            // allow using jpg/tga as shaderless tex
            if (extension != Extension.Other)
            {
                contents.Shaders.Add(relativePath.AsMemory(..^4));
            }

            // jpg textures can be referenced with tga paths in shaders
            if (extension == Extension.Jpg)
            {
                contents.Resources.Add(Path.ChangeExtension(relativePath, "tga").AsMemory());
            }

            contents.Resources.Add(relativePath.AsMemory());
        }
    }

    private static Extension GetExtension(ReadOnlySpan<char> path)
    {
        var extension = Path.GetExtension(path);

        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            return Extension.Tga;

        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return Extension.Jpg;

        return Extension.Other;
    }

    private enum Extension { Other, Tga, Jpg }
}
