using System.Diagnostics;
using Pack3r.Extensions;
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

    public static Resource FromModel(
        ReadOnlyMemory<char> value,
        bool isShader,
        string filePath) => new(value, isShader, new Line(filePath, -1, "", true));

    public ReadOnlyMemory<char> Value { get; }
    public bool IsShader { get; }
    public Line Line { get; }
    public bool SourceOnly { get; }

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

    /// <param name="Value">Path to the resource</param>
    /// <param name="IsShader">Whether the path is to a shader and not a file</param>
    public Resource(
        ReadOnlyMemory<char> value,
        bool isShader,
        in Line line,
        bool sourceOnly = false)
    {
        Global.EnsureQPathLength(value);
        Value = value;
        IsShader = isShader;
        Line = line;
        SourceOnly = sourceOnly;
    }
}

