using System.IO.Compression;
using Pack3r.Services;

namespace Pack3r.Tests;

public sealed class NoopChecker : IIntegrityChecker
{
    public void CheckIntegrity(string path)
    {
    }

    public void CheckIntegrity(string archivePath, ZipArchiveEntry entry)
    {
    }

    public void Log()
    {
    }
}
