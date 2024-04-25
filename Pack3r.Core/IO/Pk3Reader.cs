using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Pack3r.Extensions;

namespace Pack3r.IO;

public readonly struct Pk3Contents(string path)
{
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);

    public HashSet<ReadOnlyMemory<char>> Shaders { get; } = new(ROMCharComparer.Instance);
    public HashSet<string> Resources { get; } = new(StringComparer.OrdinalIgnoreCase);
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
                    contents.Resources.Add(entry.Name);
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
}
