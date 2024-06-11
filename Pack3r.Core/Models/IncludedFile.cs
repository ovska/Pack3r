using System.Diagnostics;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public class IncludedFile
{
    public AssetSource? Source { get; }
    public Shader? Shader { get; }
    public ReadOnlyMemory<char> SourcePath { get; init; }
    public ReadOnlyMemory<char> ArchivePath { get; init; }
    public bool SourceOnly { get; init; }
    public string? ReferencedIn { get; init; }
    public int? ReferencedLine { get; init; }

    public IncludedFile(
        ReadOnlyMemory<char> sourcePath,
        ReadOnlyMemory<char> archivePath)
    {
        SourcePath = sourcePath;
        ArchivePath = archivePath;
    }

    public IncludedFile(RenamableResource resource)
    {
        SourcePath = resource.AbsolutePath.AsMemory();
        ArchivePath = resource.ArchivePath.AsMemory();
    }

    public IncludedFile(AssetSource source, ReadOnlyMemory<char> relativePath, Resource resource, Shader? shader = null, bool devResource = false)
    {
        Source = source;
        SourcePath = Path.Combine(source.RootPath, relativePath.ToString()).NormalizePath().AsMemory();
        ArchivePath = relativePath;
        SourceOnly = resource.SourceOnly || devResource;
        ReferencedIn = resource.Line.Path;
        ReferencedLine = resource.Line.Index is int i and >= 0 ? i : null;
        Shader = shader;
    }

    internal string DebuggerDisplay => $"{{ File: {SourcePath} }}";
}

