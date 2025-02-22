using Pack3r.Extensions;

namespace Pack3r.Models;

public enum PositionType { Unknown, Byte, Line };

public interface IResourceSource
{
    int Position { get; }
    PositionType Type { get; }
    string File { get; }

    public string Format(Map map)
    {
        string pos = Type switch
        {
            PositionType.Line => $" line {Position}",
            PositionType.Byte => $" byte offset {Position}",
            _ => "",
        };

        return $"'{map.GetRelativeToRoot(File).NormalizePath()}'{pos}";
    }
}

public sealed class ShaderResourceSource(Shader shader) : IResourceSource
{
    public int Position => shader.Line;
    public PositionType Type => PositionType.Line;
    public string File => shader.Asset.FullPath;
}

public sealed class BinaryResourceSource(string filePath, int bytePosition) : IResourceSource
{
    public int Position => bytePosition;
    public PositionType Type => PositionType.Byte;
    public string File => filePath;
}