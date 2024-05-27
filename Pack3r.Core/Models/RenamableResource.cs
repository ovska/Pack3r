namespace Pack3r.Models;

public sealed class RenamableResource
{
    public required string AbsolutePath { get; init; }
    public required string ArchivePath
    {
        get => _archivePath;
        init => _archivePath = value.Replace(Path.DirectorySeparatorChar, '/');
    }

    private string _archivePath = null!;

    public Func<string, PackOptions, string>? Convert { get; init; }
}
