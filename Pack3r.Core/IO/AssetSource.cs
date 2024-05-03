using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract bool IsPak0 { get; }
    public abstract string RootPath { get; }
    public abstract FileInfo? GetShaderlist();

    public virtual void Dispose() { }

    public abstract bool Contains(ReadOnlyMemory<char> relativePath);

    public abstract bool TryHandleAsset(
        ZipArchive destination,
        ReadOnlyMemory<char> relativePath,
        out ZipArchiveEntry? entry);

    public abstract bool TryRead(
        ReadOnlyMemory<char> resourcePath,
        ILineReader reader,
        LineOptions options,
        CancellationToken cancellationToken,
        [NotNullWhen(true)] out IAsyncEnumerable<Line>? lines);

    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    public override bool Equals(object? obj)
    {
        return obj?.GetType() == GetType() && RootPath.EqualsF((obj as AssetSource)?.RootPath);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), RootPath);
    }
}
