using Pack3r.Extensions;

namespace Pack3r.Models;

/// <summary>
/// Generic resource referenced in a map.
/// </summary>
/// <param name="Value">Path to the resource</param>
/// <param name="IsShader">Whether the path is to a shader and not a file</param>
public readonly record struct Resource(ReadOnlyMemory<char> Value, bool IsShader) : IEquatable<Resource>
{
    public bool Equals(Resource other)
    {
        return IsShader == other.IsShader
            && ROMCharComparer.Instance.Equals(Value, other.Value);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(IsShader, ROMCharComparer.Instance.GetHashCode(Value));
    }
}
