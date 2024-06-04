using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

/// <summary>
/// Generic resource referenced in a map.
/// </summary>
/// <param name="Value">Path to the resource</param>
/// <param name="IsShader">Whether the path is to a shader and not a file</param>
public readonly struct Resource : IEquatable<Resource>
{
    public ReadOnlyMemory<char> Value { get; }
    public bool IsShader { get; }

    public int? Line { get; }
    public string Source { get; }

    public Resource(ReadOnlyMemory<char> value, bool isShader, in Line line)
    {
        Value = value;
        IsShader = isShader;

        Line = line.Index;
        Source = line.Path;
    }

    public Resource(ReadOnlyMemory<char> value, bool isShader, string path)
    {
        Value = value;
        IsShader = isShader;

        Source = path;
    }

    public bool Equals(Resource other)
    {
        return IsShader == other.IsShader
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
}
