using System.IO.Compression;
using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IReferenceParser
{
    bool CanParse(ReadOnlyMemory<char> resource);

    Task<HashSet<Resource>?> Parse(
        string path,
        CancellationToken cancellationToken);

    Task<HashSet<Resource>?> Parse(
        ZipArchiveEntry entry,
        string archivePath,
        CancellationToken cancellationToken);
}
