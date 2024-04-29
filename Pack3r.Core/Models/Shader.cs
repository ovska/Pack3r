using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

public sealed record class ArchiveData(string ArchivePath, string EntryPath);

public sealed class Shader(
    ReadOnlyMemory<char> name,
    string relativePath,
    AssetSource source)
    : IEquatable<Shader>
{
    public string DestinationPath { get; } = relativePath;
    public AssetSource Source { get; } = source;

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

    public string GetAbsolutePath() => Path.Combine(Source.RootPath, DestinationPath);

    /// <summary>
    /// Shader name used to resolve the texture used, can be either a generic path without extension or shader name.
    /// </summary>
    public ReadOnlyMemory<char>? ImplicitMapping { get; set; }

    public bool Equals(Shader? other)
    {
        return other is not null
            && ReferenceEquals(Source, other.Source)
            && DestinationPath.EqualsF(other.DestinationPath)
            && ROMCharComparer.Instance.Equals(Name, other.Name);
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException();
        //return HashCode.Combine(
        //    Source,
        //    DestinationPath,
        //    ROMCharComparer.Instance.GetHashCode(Name));
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Shader);
    }

    public bool ContentsEqual(Shader other)
    {
        if (ReferenceEquals(this, other))
            return true;

        var cmp = ROMCharComparer.Instance;

        return cmp.Equals(Name, other.Name)
            && NeededInPk3 == other.NeededInPk3
            && ImplicitMapping.HasValue == other.ImplicitMapping.HasValue
            && cmp.Equals(ImplicitMapping.GetValueOrDefault(), other.ImplicitMapping.GetValueOrDefault())
            && HasLightStyles == other.HasLightStyles
            && Textures.SequenceEqual(other.Textures, cmp)
            && Files.SequenceEqual(other.Files, cmp)
            && Shaders.SequenceEqual(other.Shaders, cmp);
    }
}
