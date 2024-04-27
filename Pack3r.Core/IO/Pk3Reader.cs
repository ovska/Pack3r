using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Logging;

namespace Pack3r.IO;

public sealed class Pk3Contents(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);

    public bool IsPk3Dir => System.IO.Path.GetExtension(Path.AsSpan()).Equals(".pk3dir", StringComparison.OrdinalIgnoreCase);

    public HashSet<ReadOnlyMemory<char>> Shaders { get; } = new(ROMCharComparer.Instance);
    public HashSet<ReadOnlyMemory<char>> Resources { get; } = new(ROMCharComparer.Instance);

    public string GetResourcePath(string relativePath)
    {
        Debug.Assert(IsPk3Dir, $"GetResourcePath called with '{relativePath}' on non-pk3dir: {Path}");
        return System.IO.Path.Join(Path, relativePath);
    }
}

public interface IPk3Reader
{
    Task<Pk3Contents> ReadPk3(
        string path,
        CancellationToken cancellationToken);

    Task<List<Pk3Contents>> ReadPk3Dirs(
        DirectoryInfo etmain,
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

    public async Task<List<Pk3Contents>> ReadPk3Dirs(
        DirectoryInfo etmain,
        CancellationToken cancellationToken)
    {
        List<Pk3Contents> allContents = [];

        foreach (var dir in etmain.EnumerateDirectories("*.pk3dir", SearchOption.TopDirectoryOnly))
        {
            string path = dir.FullName;

            try
            {
                Debug.Assert(
                    Path.GetExtension(path.AsSpan()).Equals(".pk3dir", StringComparison.OrdinalIgnoreCase),
                    $"Invalid pk3dir path: {path}");

                var contents = new Pk3Contents(path);

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

                allContents.Add(contents);
            }
            catch (IOException)
            {
                logger.Warn($"Failed to read data from pk3dir '{path}', skipped");
            }
        }

        return allContents;
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
