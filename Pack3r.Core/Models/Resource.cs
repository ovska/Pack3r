using System.Diagnostics;
using Pack3r.IO;

namespace Pack3r.Models;

/// <summary>
/// Generic resource referenced in a map.
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Resource : IEquatable<Resource>
{
    public static Resource Shader(string value, in Line line) => new(value.AsMemory(), true, in line);
    public static Resource Shader(ReadOnlyMemory<char> value, in Line line) => new(value, true, in line);

    public static Resource File(string value, in Line line) => new(value.AsMemory(), false, in line);
    public static Resource File(ReadOnlyMemory<char> value, in Line line) => new(value, false, in line);

    public static Resource FromModel(QPath value, string filePath)
        => new(value, isShader: true, new Line(filePath, -1, "", true));

    public QString Value { get; } // should this be a QPath ?
    public bool IsShader { get; }
    public Line Line { get; }
    public bool SourceOnly { get; }

    public bool Equals(Resource? other)
    {
        return IsShader == other?.IsShader && Value.Equals(other.Value);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsShader, Value);
    }

    public override bool Equals(object? obj)
    {
        return obj is Resource resource && Equals(resource);
    }

    internal string DebuggerDisplay => $"{{ Resource: {Value} ({(IsShader ? "shader" : "file")}) }}";

    /// <param name="Value">Path to the resource</param>
    /// <param name="IsShader">Whether the path is to a shader and not a file</param>
    public Resource(
        QString value,
        bool isShader,
        in Line line,
        bool sourceOnly = false)
    {
        if (isShader)
            value = value.TrimTextureExtension();

        Global.EnsureQPathLength(value);
        Value = value;
        IsShader = isShader;
        Line = line;
        SourceOnly = sourceOnly;
    }
}

