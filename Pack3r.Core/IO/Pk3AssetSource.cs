using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;
using Pack3r.Extensions;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.IO;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class Pk3AssetSource(string path, bool notPacked) : AssetSource(notPacked)
{
    public string ArchivePath => path;
    public override string RootPath => ArchivePath;

    private readonly ZipArchive _archive = ZipFile.OpenRead(path);

    // separate handle for reading entry data from compression threads, opened when something is first packed
    private SafeFileHandle? _handle;
    private ZipDirectory? _directory;
    private bool _directoryRead;

    /// <summary>
    /// Gets the location of the entry's compressed data, so it can be read from any thread with
    /// <see cref="OpenData"/>. Returns <see langword="false"/> if the entry can only be read through
    /// <see cref="ZipArchive"/>, e.g. if the archive uses zip64 or the entry is encrypted.
    /// Not thread-safe, call from the packaging thread.
    /// </summary>
    /// <exception cref="IOException">Reading the archive failed</exception>
    internal bool TryGetEntryData(ZipArchiveEntry entry, out ZipDirectory.Entry data, out long dataOffset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_directoryRead)
        {
            _directoryRead = true;
            _handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.Read);

            try
            {
                _directory = ZipDirectory.TryRead(_handle);
            }
            catch (IOException)
            {
                // everything is read through ZipArchive instead
                _directory = null;
            }
        }

        dataOffset = -1;

        // sanity check that both readers agree about the entry
        if (_directory is null ||
            !_directory.TryGetEntry(entry.FullName, out data) ||
            data.IsEncrypted ||
            data.Crc32 != entry.Crc32 ||
            data.CompressedSize != entry.CompressedLength ||
            data.UncompressedSize != entry.Length)
        {
            data = default;
            return false;
        }

        try
        {
            dataOffset = _directory.GetDataOffset(_handle!, data);
            return true;
        }
        catch (InvalidDataException)
        {
            // let ZipArchive read it, and report the error if it's really corrupt
            data = default;
            return false;
        }
    }

    /// <summary>
    /// Opens a stream over the compressed data of an entry. Safe to read on any thread, concurrently with other
    /// streams from this archive.
    /// </summary>
    internal Stream OpenData(long dataOffset, long compressedSize)
    {
        ObjectDisposedException.ThrowIf(_disposed || _handle is null, this);
        return new FileRangeStream(_handle, dataOffset, compressedSize);
    }

    public override string ToString() => $"{{ Pk3: {ArchivePath} }}";
    internal string DebuggerDisplay => $"{{ Pk3 src: '{Path.GetFileName(ArchivePath)}' (Excluded: {NotPacked}) }}";

    public override async IAsyncEnumerable<Shader> EnumerateShaders(
        IShaderParser parser,
        Func<string, bool> skipPredicate,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var entry in _archive.Entries)
        {
            if (!entry.FullName.HasExtension(".shader") || skipPredicate(entry.FullName))
                continue;

            if (entry.FullName.Length > Global.MAX_QPATH)
                continue;

            await foreach (var shader in parser.Parse(new Pk3Asset(this, entry), cancellationToken))
            {
                yield return shader;
            }
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            _archive.Dispose();
            _handle?.Dispose();
            base.Dispose();
        }
    }

    protected override IEnumerable<IAsset> EnumerateAssets()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _archive
            .Entries
            .Where(entry => entry.FullName.Length < Global.MAX_QPATH && Tokens.IncludeAsset(entry.FullName))
            .Select(entry => new Pk3Asset(this, entry));
    }
}
