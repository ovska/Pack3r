using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SkinParser(ILineReader reader) : IReferenceParser
{
    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".skin");

    public Task<HashSet<Resource>?> Parse(string path, CancellationToken cancellationToken)
    {
        return ParseCore(reader.ReadLines(path, default, cancellationToken))!;
    }

    public Task<HashSet<Resource>?> Parse(ZipArchiveEntry entry, string archivePath, CancellationToken cancellationToken)
    {
        return ParseCore(reader.ReadLines(archivePath, entry, default, cancellationToken))!;
    }

    private static async Task<HashSet<Resource>> ParseCore(IAsyncEnumerable<Line> lines)
    {
        var result = new HashSet<Resource>();

        await foreach (var line in lines)
        {
            int comma = line.Value.Span.IndexOf(',');

            if (comma >= 0)
            {
                result.Add(
                    new Resource(line.Value[(comma + 1)..].Trim().Trim('"'), IsShader: true));
            }
        }

        return result;
    }
}
