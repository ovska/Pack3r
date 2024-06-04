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
        return ParseCore(reader.ReadLines(path, default, cancellationToken), cancellationToken);
    }

    public Task<HashSet<Resource>?> Parse(ZipArchiveEntry entry, string archivePath, CancellationToken cancellationToken)
    {
        return ParseCore(reader.ReadLines(archivePath, entry, default, cancellationToken), cancellationToken);
    }

    private static async Task<HashSet<Resource>?> ParseCore(IAsyncEnumerable<Line> lines, CancellationToken cancellationToken)
    {
        var result = new HashSet<Resource>();

        await foreach (var line in lines.WithCancellation(cancellationToken))
        {
            int comma = line.Value.Span.IndexOf(',');

            if (comma >= 0)
            {
                result.Add(
                    new Resource(line.Value[(comma + 1)..].Trim().Trim('"'), isShader: true, in line));
            }
        }

        return result;
    }
}
