using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

public abstract class AssetSource : IDisposable
{
    public abstract string RootPath { get; }
    public abstract FileInfo? GetShaderlist();

    public virtual void Dispose() { }

    public abstract bool Contains(ReadOnlyMemory<char> relativePath);

    public abstract bool TryHandleAsset(
            ZipArchive destination,
            ReadOnlyMemory<char> relativePath,
            out ZipArchiveEntry? entry);

    public abstract IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        CancellationToken cancellationToken);

    public sealed class Empty : AssetSource
    {
        public override string RootPath => throw new NotSupportedException();

        public override IAsyncEnumerable<Shader> EnumerateShaders(IShaderParser parser, Func<string, bool> skipPredicate, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public override FileInfo? GetShaderlist() => null;
        public override bool Contains(ReadOnlyMemory<char> relativePath) => false;

        public override string ToString() => "<empty asset source>";

        public override bool TryHandleAsset(ZipArchive destination, ReadOnlyMemory<char> relativePath, out ZipArchiveEntry? entry)
        {
            entry = null;
            return false;
        }
    }

    public override bool Equals(object? obj)
    {
        return obj?.GetType() == GetType() && RootPath.EqualsF((obj as AssetSource)?.RootPath);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(GetType(), RootPath);
    }
}
