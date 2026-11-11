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

    public Stream OpenRead(bool isAsync = false) => new FileStream(
        file.FullName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan | (isAsync ? FileOptions.Asynchronous : 0));

    public void AddTo(Pk3Writer writer, string? archivePath = null, Action<Stream, Stream>? transform = null)
    {
        // opened here so a locked or missing file is reported to the caller, which can try another source
        writer.Add(archivePath ?? Name, file.LastWriteTime, OpenRead(), transform);
    }

    public async ValueTask<IMemoryOwner<byte>> GetBytes(int? sizeHint, CancellationToken cancellationToken)
    {
        var arrayPoolBufferWriter = new ArrayPoolBufferWriter<byte>(sizeHint ?? 4096);

        await using (var stream = OpenRead(true))
        {
            await stream.CopyToAsync(arrayPoolBufferWriter.AsStream(), cancellationToken);
        }

        return arrayPoolBufferWriter;
    }

    internal string DebuggerDisplay => $"{{ FileAsset: '{Name}' from {Source.Name} }}";
}
