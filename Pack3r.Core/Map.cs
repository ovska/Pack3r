using CommunityToolkit.Diagnostics;

namespace Pack3r;

public interface IFileProvider
{
    public ICollection<ReadOnlyMemory<char>> Files { get; }
}

public interface ITextureProvider
{
    public ICollection<ReadOnlyMemory<char>> Textures { get; }
}

public interface IShaderProvider
{
    public ICollection<ReadOnlyMemory<char>> Shaders { get; }
}

public readonly record struct Shader(
    string FilePath,
    ReadOnlyMemory<char> Name)
    : IFileProvider, IShaderProvider, ITextureProvider
{
    public List<ReadOnlyMemory<char>> Textures { get; } = [];
    public List<ReadOnlyMemory<char>> Files { get; } = [];
    public List<ReadOnlyMemory<char>> Shaders { get; } = [];

    ICollection<ReadOnlyMemory<char>> IShaderProvider.Shaders => Shaders;
    ICollection<ReadOnlyMemory<char>> IFileProvider.Files => Files;
    ICollection<ReadOnlyMemory<char>> ITextureProvider.Textures => Textures;
}

public readonly record struct Model(string Name) : IFileProvider, IShaderProvider
{
    public ICollection<ReadOnlyMemory<char>> Files { get; }
    public ICollection<ReadOnlyMemory<char>> Shaders { get; }
}

public sealed class Map
{
    /// <summary>
    /// .map file name without extension
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Full path to .map
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// ETMain folder
    /// </summary>
    public DirectoryInfo ETMain { get; }

    public Map(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(Path);

        var parent = Directory.GetParent(Path);
        Guard.IsNotNull(parent);
        Guard.IsEqualTo("maps", parent.Name);

        parent = Directory.GetParent(parent.FullName);
        Guard.IsNotNull(parent);
        Guard.IsEqualTo("etmain", parent.Name);

        ETMain = parent;
    }

    public HashSet<Model> Models { get; } = [];
    public HashSet<Shader> Shaders { get; } = [];
    public HashSet<string> Sounds { get; } = [];
    public HashSet<string> Terrains { get; } = [];
}
