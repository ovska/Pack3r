using Pack3r.Extensions;

namespace Pack3r;

public sealed record Shader(
    string FilePath,
    ReadOnlyMemory<char> Name)
    : IEquatable<Shader>
{
    public List<ReadOnlyMemory<char>> Textures { get; } = [];
    public List<ReadOnlyMemory<char>> Files { get; } = [];
    public List<ReadOnlyMemory<char>> Shaders { get; } = [];
    public bool HasLightStyles { get; set; }

    public bool Equals(Shader? other)
    {
        return other is not null
            && FilePath.Equals(other.FilePath)
            && ROMCharComparer.Instance.Equals(Name, other.Name);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            FilePath,
            ROMCharComparer.Instance.GetHashCode(Name));
    }
}
