using System.Buffers;
using System.Diagnostics;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class FileAsset(DirectoryAssetSource source, FileInfo file) : IAsset
{
    public string Name { get; } = Path.GetRelativePath(source.RootPath, file.FullName).NormalizePath();
    public string FullPath { get; } = file.FullName.NormalizePath();
    public AssetSource Source => source;

    public Stream OpenRead() => new FileStream(
        file.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan);

    public void AddTo(Pk3Writer writer, string? archivePath = null, Action<Stream, Stream>? transform = null)
    {
        // opened here so a locked or missing file is reported to the caller, which can try another source
        writer.Add(archivePath ?? Name, file.LastWriteTime, OpenRead(), transform);
    }

    public IMemoryOwner<byte> GetBytes(int? sizeHint, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var arrayPoolBufferWriter = new ArrayPoolBufferWriter<byte>(sizeHint ?? 4096);

        using (var stream = OpenRead())
        {
            stream.CopyTo(arrayPoolBufferWriter.AsStream());
        }

        return arrayPoolBufferWriter;
    }

    internal string DebuggerDisplay => $"{{ FileAsset: '{Name}' from {Source.Name} }}";
}
