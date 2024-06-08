using System.Diagnostics;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Shader(
    ReadOnlyMemory<char> name,
    IAsset asset,
    int line)
    : IEquatable<Shader>
{
    public string DestinationPath { get; } = asset.Name;
    public AssetSource Source { get; } = asset.Source;
    public int Line { get; } = line;

    public IAsset Asset { get; } = asset;

    public ReadOnlyMemory<char> Name { get; } = name;

    /// <summary>References to textures, models, videos etc</summary>
    public List<ReadOnlyMemory<char>> Resources { get; } = [];

    /// <summary>References to editorimages, lightimages etc</summary>
    public List<ReadOnlyMemory<char>> DevResources { get; } = [];

    /// <summary>References to other shaders</summary>
    public List<ReadOnlyMemory<char>> Shaders { get; } = [];

    /// <summary>Shader generates stylelights</summary>
    public bool HasLightStyles { get; set; }

    /// <summary>Shader includes references to any files needed in pk3</summary>
    public bool NeededInPk3 => Resources.Count > 0 || Shaders.Count > 0 || ImplicitMapping.HasValue;

    public string GetAbsolutePath()
    {
        var path = Path.Combine(Source.RootPath, DestinationPath);
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    /// <summary>
    /// Shader name used to resolve the texture used, texture name with or without extension.
    /// </summary>
    public ReadOnlyMemory<char>? ImplicitMapping { get; set; }

    public bool Equals(Shader? other)
    {
        return ReferenceEquals(this, other);
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException("Shader equality not implemented");
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Shader);
    }

    private string DebuggerDisplay => $"Shader '{Name}' in {Path.GetFileName(Source.RootPath)}/{DestinationPath}";
}
