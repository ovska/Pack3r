using Pack3r.Extensions;
using IOPath = System.IO.Path;

namespace Pack3r.Models;

public sealed record class ArchiveData(string ArchivePath, string EntryPath);

public sealed class Shader(
    ReadOnlyMemory<char> name,
    string absolutePath,
    ArchiveData? archiveData)
    : IEquatable<Shader>
{
    public string AbsolutePath { get; } = absolutePath;
    public ArchiveData? ArchiveData { get; } = archiveData;

    public ReadOnlyMemory<char> Name { get; } = name;

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

    public ReadOnlySpan<char> AssetDirectory
    {
        get
        {
            if (ArchiveData is not null)
                throw new NotSupportedException($"Attempting to get AssetDirectory for archived shader {AbsolutePath}");

            var scripts = IOPath.GetDirectoryName(AbsolutePath.AsSpan());
            var etmainOrPk3dir = IOPath.GetDirectoryName(scripts);
            return IOPath.GetFileName(etmainOrPk3dir);
        }
    }

    public string DestinationPath
    {
        get
        {
            if (_destinationPath is null)
            {
                if (ArchiveData is null)
                {
                    _destinationPath = IOPath.GetRelativePath(
                       IOPath.GetDirectoryName(IOPath.GetDirectoryName(AbsolutePath.AsSpan())).ToString(),
                       AbsolutePath);
                }
                else
                {
                    _destinationPath = ArchiveData.EntryPath;
                }
            }

            return _destinationPath;
        }
    }

    private string? _destinationPath;

    public bool Equals(Shader? other)
    {
        return other is not null
            && AbsolutePath.EqualsF(other.AbsolutePath)
            && ROMCharComparer.Instance.Equals(Name, other.Name);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(AbsolutePath, ROMCharComparer.Instance.GetHashCode(Name));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Shader);
    }
}
