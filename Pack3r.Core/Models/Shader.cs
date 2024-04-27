using Pack3r.Extensions;

namespace Pack3r.Models;

/// <summary>
/// x
/// </summary>
/// <param name="Path">Location of the shader file</param>
/// <param name="Name">Shader name, e.g. <c>textures/common/caulk</c></param>
public sealed record Shader(
    ResourcePath Path,
    ReadOnlyMemory<char> Name)
    : IEquatable<Shader>
{
    /// <summary>References to texture files</summary>
    public List<ReadOnlyMemory<char>> Textures { get; } = [];

    /// <summary>References to other files, such as models or videos</summary>
    public List<ReadOnlyMemory<char>> Files { get; } = [];

    /// <summary>References to other shaders</summary>
    public List<ReadOnlyMemory<char>> Shaders { get; } = [];

    /// <summary>Shader generates stylelights</summary>
    public bool HasLightStyles { get; set; }

    /// <summary>Shader includes references to any files needed in pk3</summary>
    public bool NeededInPk3 =>
        Textures.Count > 0 ||
        Files.Count > 0 ||
        Shaders.Count > 0 ||
        ImplicitMapping.HasValue;

    /// <summary>
    /// Shader name used to resolve the texture used, can be either a generic path without extension or shader name.
    /// </summary>
    public ReadOnlyMemory<char>? ImplicitMapping { get; set; }

    public bool Equals(Shader? other)
    {
        return other is not null
            && Path.Equals(other.Path)
            && ROMCharComparer.Instance.Equals(Name, other.Name);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Path, ROMCharComparer.Instance.GetHashCode(Name));
    }
}
