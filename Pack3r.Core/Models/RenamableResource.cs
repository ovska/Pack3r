namespace Pack3r.Models;

public sealed class RenamableResource
{
    public required string AbsolutePath { get; init; }
    public required string ArchivePath { get; init; }
    public Func<string, PackOptions, string>? Convert { get; init; }
}
