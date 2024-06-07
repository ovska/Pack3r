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

    public IncludedFile(AssetSource source, ReadOnlyMemory<char> relativePath, Resource resource, Shader? shader = null)
    {
        Source = source;
        SourcePath = Path.Combine(source.RootPath, relativePath.ToString()).NormalizePath().AsMemory();
        ArchivePath = relativePath;
        SourceOnly = resource.SourceOnly;
        ReferencedIn = resource.Line.Path;
        ReferencedLine = resource.Line.Index is int i and >= 0 ? i : null;
        Shader = shader;
    }

    internal string DebuggerDisplay => $"{{ File: {SourcePath} }}";
}

/// <summary>
/// Generic resource referenced in a map.
/// </summary>
/// <param name="Value">Path to the resource</param>
/// <param name="IsShader">Whether the path is to a shader and not a file</param>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Resource(
    ReadOnlyMemory<char> value,
    bool isShader,
    in Line line,
    bool sourceOnly = false)
    : IEquatable<Resource>
{
    public static Resource Shader(string value, in Line line) => new(value.AsMemory(), true, in line);
    public static Resource Shader(ReadOnlyMemory<char> value, in Line line) => new(value, true, in line);

    public static Resource File(string value, in Line line) => new(value.AsMemory(), false, in line);
    public static Resource File(ReadOnlyMemory<char> value, in Line line) => new(value, false, in line);

    public static Resource FromModel(
        ReadOnlyMemory<char> value,
        bool isShader,
        string filePath) => new(value, isShader, new Line(filePath, -1, "", true));

    public ReadOnlyMemory<char> Value { get; } = value;
    public bool IsShader { get; } = isShader;
    public Line Line { get; } = line;
    public bool SourceOnly { get; } = sourceOnly;

    public bool Equals(Resource? other)
    {
        return IsShader == other?.IsShader
            && ROMCharComparer.Instance.Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsShader, ROMCharComparer.Instance.GetHashCode(Value));
    }

    public override bool Equals(object? obj)
    {
        return obj is Resource resource && Equals(resource);
    }

    internal string DebuggerDisplay => $"{{ Resource: {Value} ({(IsShader ? "shader" : "file")}) }}";
}

