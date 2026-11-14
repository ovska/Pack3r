using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

namespace Pack3r.IO;

/// <summary>
/// Data that is already compressed with a zip compression method, see <see cref="Pk3Writer.Add"/>.
/// </summary>
/// <param name="Method">Compression method, 0 (stored) or 8 (deflate)</param>
/// <param name="Crc32">CRC-32 of the uncompressed data</param>
/// <param name="CompressedSize">Length of the compressed data</param>
/// <param name="UncompressedSize">Length of the uncompressed data</param>
/// <param name="Open">Opens a stream over the compressed data, called on the writer thread</param>
public sealed record PrecompressedData(
    ushort Method,
    uint Crc32,
    long CompressedSize,
    long UncompressedSize,
    Func<Stream> Open);

/// <summary>
/// Writes a zip archive while compressing its entries in parallel.
/// </summary>
/// <remarks>
/// <para>
/// Entries are queued with <see cref="Add"/> from a single producer thread, compressed on worker threads,
/// and written to the destination in the order they were added, so the output is deterministic.
/// The producer can change between threads as long as the adds don't overlap, e.g. one thread awaits a task
/// that added entries before adding more. <see cref="DisposeAsync"/> may be called while another thread is adding.
/// </para>
/// <para>
/// <see cref="ZipArchive"/> can only have one entry open at a time and has no way to accept data that is already
/// compressed, so this type writes the zip structures itself. Only stored and deflated entries are written,
/// and there is no zip64 support, since pk3 files are read by the game's old unzip implementation.
/// </para>
/// </remarks>
public sealed class Pk3Writer : IAsyncDisposable
{
    private const uint LocalFileHeaderSignature = 0x04034b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const int LocalFileHeaderSize = 30;
    private const int CentralDirectoryHeaderSize = 46;
    private const int EndOfCentralDirectorySize = 22;
    private const ushort MethodStored = 0;
    private const ushort MethodDeflate = 8;
    private const ushort VersionStored = 10;
    private const ushort VersionDeflate = 20;
    private const ushort FlagUtf8 = 0x0800;

    private readonly Stream _destination;
    private readonly byte[] _comment;
    private readonly CompressionLevel _compressionLevel;

    // a job, or one chunk of a chunked job
    private readonly Channel<WorkItem> _work = Channel.CreateUnbounded<WorkItem>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

    private readonly Channel<Job> _ordered = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

    /// <summary>Default for the maximum number of entries waiting to be written.</summary>
    public const int DefaultMaxEntriesInFlight = 256;

    /// <summary>Default for the maximum total input size of entries waiting to be written.</summary>
    public const long DefaultMaxBytesInFlight = 128 * 1024 * 1024;

    // size assumed for a source that can't report its length
    private const long UnknownSourceSize = 1024 * 1024;

    /// <summary>
    /// Files at least this large are compressed in chunks on several threads, so one large file (the bsp) doesn't
    /// keep a single thread busy after the rest are done. Depends only on the size, so the output is the same on
    /// any number of cores.
    /// </summary>
    public const long ChunkedMinimumSize = 2 * ChunkSize;

    /// <summary>Size of the chunks a large file is compressed in, costs ~0.1% in size.</summary>
    public const int ChunkSize = 2 * 1024 * 1024;

    private readonly InFlightLimit _inFlight;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;
    private readonly Task _writer;
    private readonly List<CentralDirectoryRecord> _records = new(capacity: 256);

    private ExceptionDispatchInfo? _failure;
    private Job? _writing;
    private long _position;
    private int _entriesAdded;
    private int _entriesWritten;
    private bool _completed;
    private bool _disposed;

    /// <param name="destination">Stream to write to, does not need to be seekable. Not disposed by this type.</param>
    /// <param name="comment">Archive comment</param>
    /// <param name="compressionLevel">Deflate compression level for all entries</param>
    /// <param name="maxDegreeOfParallelism">Number of compression threads, defaults to processor count - 1</param>
    /// <param name="maxEntriesInFlight">
    /// Maximum number of entries waiting to be written. Each holds its source open, e.g. a file handle.
    /// </param>
    /// <param name="maxBytesInFlight">
    /// Maximum total input size of entries waiting to be written, as each holds its compressed data in memory
    /// until then. An entry larger than this is still accepted when nothing else is waiting.
    /// </param>
    /// <param name="cancellationToken">Token to cancel the compression</param>
    public Pk3Writer(
        Stream destination,
        string? comment = null,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        int? maxDegreeOfParallelism = null,
        int maxEntriesInFlight = DefaultMaxEntriesInFlight,
        long maxBytesInFlight = DefaultMaxBytesInFlight,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism ?? 1, 1, nameof(maxDegreeOfParallelism));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxEntriesInFlight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytesInFlight, 1);

        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable", nameof(destination));

        _destination = destination;
        _comment = Encoding.UTF8.GetBytes(comment ?? "");
        _compressionLevel = compressionLevel;

        if (_comment.Length > ushort.MaxValue)
            throw new ArgumentException("Archive comment is too long", nameof(comment));

        int workerCount = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount - 1);

        // entries are written in the order they were added, so while a large entry at the head of the queue (e.g.
        // the bsp) is being compressed, the rest wait for it after they're done. a generous limit keeps the other
        // workers busy meanwhile; it's by size, as an entry holds its compressed data until it has been written
        _inFlight = new InFlightLimit(maxEntriesInFlight, maxBytesInFlight);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // dedicated threads, as the producer blocks while waiting for room in the queue
        _workers = new Task[workerCount];

        for (int i = 0; i < _workers.Length; i++)
        {
            _workers[i] = Task.Factory.StartNew(
                CompressLoop,
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        _writer = Task.Factory.StartNew(
            WriteLoop,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
    }

    /// <summary>Compression level of the entries compressed by this writer.</summary>
    public CompressionLevel CompressionLevel => _compressionLevel;

    /// <summary>Number of entries queued with <see cref="Add"/>.</summary>
    public int EntriesAdded => _entriesAdded;

    /// <summary>Number of entries compressed and written to the destination.</summary>
    public int EntriesWritten => Volatile.Read(ref _entriesWritten);

    /// <summary>
    /// Queues an entry to be compressed and written. Blocks if too many entries are waiting to be written.
    /// </summary>
    /// <param name="name">Path of the entry in the archive</param>
    /// <param name="lastWriteTime">Timestamp of the entry</param>
    /// <param name="source">
    /// Data of the entry. Read on another thread, so it must not share state with other streams.
    /// Disposed by this type, also if this method throws.
    /// </param>
    /// <param name="transform">
    /// Optional function that writes the entry data, given the source and a destination stream.
    /// Runs on another thread. If <see langword="null"/>, the source is copied as is.
    /// </param>
    /// <param name="alternative">
    /// Optional copy of the same data that is already compressed, such as the entry in a source archive. It is
    /// written instead if it's not larger than the result of compressing <paramref name="source"/>, and its CRC
    /// matches. Can't be combined with <paramref name="transform"/>.
    /// </param>
    /// <exception cref="OperationCanceledException">The token passed to the constructor was canceled</exception>
    /// <remarks>
    /// If compressing or writing a previous entry failed, that exception is thrown.
    /// </remarks>
    public void Add(
        string name,
        DateTime lastWriteTime,
        Stream source,
        Action<Stream, Stream>? transform = null,
        PrecompressedData? alternative = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        long size = source.CanSeek ? source.Length : alternative?.UncompressedSize ?? UnknownSourceSize;

        Enqueue(source, size, () =>
        {
            if (alternative is not null)
            {
                if (transform is not null)
                    throw new ArgumentException("Precompressed alternative can't be used with a transform", nameof(alternative));

                ValidateRaw(alternative.Method, alternative.CompressedSize, alternative.UncompressedSize);
            }

            var job = new Job(name, lastWriteTime, source, transform) { Alternative = alternative, InFlightSize = size };

            // a file is read with positional reads by each chunk, a transform needs the whole input in one pass
            if (transform is null && source is FileStream { CanSeek: true } file && size >= ChunkedMinimumSize)
                job.SplitIntoChunks(file.SafeFileHandle, size);

            return job;
        }, compress: true);
    }

    private static void ValidateRaw(ushort method, long compressedSize, long uncompressedSize)
    {
        if (method is not (MethodStored or MethodDeflate))
            throw new ArgumentOutOfRangeException(nameof(method), method, "Only stored and deflate are supported");

        ArgumentOutOfRangeException.ThrowIfNegative(compressedSize);
        ArgumentOutOfRangeException.ThrowIfNegative(uncompressedSize);

        if (method == MethodStored && compressedSize != uncompressedSize)
            throw new ArgumentException("Stored entry must have equal compressed and uncompressed size");
    }

    /// <summary>
    /// Queues an entry whose data is already compressed, such as one from another zip archive. The data is
    /// copied to the destination as is on the writer thread, without being buffered. Blocks if too many entries
    /// are waiting to be written.
    /// </summary>
    /// <param name="name">Path of the entry in the archive</param>
    /// <param name="lastWriteTime">Timestamp of the entry</param>
    /// <param name="method">Compression method of the data, 0 (stored) or 8 (deflate)</param>
    /// <param name="crc32">CRC-32 of the uncompressed data</param>
    /// <param name="compressedSize">Length of the compressed data</param>
    /// <param name="uncompressedSize">Length of the uncompressed data</param>
    /// <param name="compressedData">
    /// Stream containing at least <paramref name="compressedSize"/> bytes of compressed data. Read on another thread,
    /// so it must not share state with other streams. Disposed by this type, also if this method throws.
    /// </param>
    /// <remarks>
    /// The data is not validated, so a corrupt source entry is copied as is.
    /// If compressing or writing a previous entry failed, that exception is thrown.
    /// </remarks>
    public void AddRaw(
        string name,
        DateTime lastWriteTime,
        ushort method,
        uint crc32,
        long compressedSize,
        long uncompressedSize,
        Stream compressedData)
    {
        ArgumentNullException.ThrowIfNull(compressedData);

        // streamed to the destination by the writer, so it holds a source open but no data in memory
        Enqueue(compressedData, size: 0, () =>
        {
            ValidateRaw(method, compressedSize, uncompressedSize);

            return Job.Raw(name, lastWriteTime, method, crc32, compressedSize, uncompressedSize, compressedData);
        }, compress: false);
    }

    private void Enqueue(Stream source, long size, Func<Job> createJob, bool compress)
    {
        bool queued = false;

        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_completed)
                throw new InvalidOperationException("Cannot add entries after the archive has been completed");

            _failure?.Throw();

            try
            {
                _inFlight.Acquire(size, _cts.Token);
            }
            catch (OperationCanceledException) when (_failure is not null)
            {
                _failure.Throw();
            }

            Job job;

            try
            {
                job = createJob();
                ArgumentException.ThrowIfNullOrEmpty(job.Name, "name");
            }
            catch
            {
                _inFlight.Release(size);
                throw;
            }

            // writer sees jobs in the order they were added, workers may finish them in any order.
            // both channels are unbounded, so writing only fails if DisposeAsync completed them on another thread
            // while this was adding. a job that got into the ordered channel is released when it's drained there
            if (!_ordered.Writer.TryWrite(job) || (compress && !TryQueueWork(job)))
                throw new ObjectDisposedException(nameof(Pk3Writer));

            _entriesAdded++;
            queued = true;
        }
        finally
        {
            if (!queued)
                source.Dispose();
        }
    }

    private bool TryQueueWork(Job job)
    {
        if (job.ChunkCount == 0)
            return _work.Writer.TryWrite(new WorkItem(job, Chunk: -1));

        for (int i = 0; i < job.ChunkCount; i++)
        {
            if (!_work.Writer.TryWrite(new WorkItem(job, i)))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Waits for all entries to be written, and writes the central directory.
    /// </summary>
    /// <returns>Total size of the archive in bytes</returns>
    /// <remarks>
    /// If compressing or writing an entry failed, that exception is thrown.
    /// </remarks>
    public async Task<long> CompleteAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_completed)
            throw new InvalidOperationException("The archive has already been completed");

        _completed = true;
        _ordered.Writer.TryComplete();
        _work.Writer.TryComplete();

        try
        {
            await Task.WhenAll([.. _workers, _writer]).ConfigureAwait(false);
        }
        catch when (_failure is not null)
        {
        }

        _failure?.Throw();

        WriteCentralDirectory();
        _destination.Flush();
        return _position;
    }

    /// <summary>
    /// Stops compressing and releases all resources. Does not write the central directory if
    /// <see cref="CompleteAsync"/> was not called, and does not dispose the destination stream.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (!_completed)
        {
            _completed = true;
            _cts.Cancel();
            _ordered.Writer.TryComplete();
            _work.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll([.. _workers, _writer]).ConfigureAwait(false);
        }
        catch
        {
            // the failure has been observed via Add or CompleteAsync, or the caller is already handling an exception
        }

        // all threads have exited; release whatever was not written. every job is either still in the ordered
        // queue, the one the writer was waiting for, or already written and released
        _writing?.Release();

        while (_ordered.Reader.TryRead(out Job? job))
        {
            job.Release();
        }

        _cts.Dispose();
    }

    private void Fail(Exception exception)
    {
        if (Interlocked.CompareExchange(ref _failure, ExceptionDispatchInfo.Capture(exception), null) is null)
        {
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void CompressLoop()
    {
        CancellationToken token = _cts.Token;
        ChannelReader<WorkItem> reader = _work.Reader;

        try
        {
            while (WaitToRead(reader, token))
            {
                while (reader.TryRead(out WorkItem item))
                {
                    Job job = item.Job;

                    try
                    {
                        token.ThrowIfCancellationRequested();

                        if (item.Chunk < 0)
                        {
                            job.Compress(_compressionLevel);
                            job.Done.TrySetResult();
                        }
                        else if (job.CompressChunk(item.Chunk, _compressionLevel))
                        {
                            // the last chunk to finish completes the job
                            job.Done.TrySetResult();
                        }
                    }
                    catch (Exception e)
                    {
                        Fail(e);
                        job.Done.TrySetException(e);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
    }

    private void WriteLoop()
    {
        CancellationToken token = _cts.Token;
        ChannelReader<Job> reader = _ordered.Reader;

        try
        {
            while (WaitToRead(reader, token))
            {
                while (reader.TryRead(out Job? job))
                {
                    _writing = job;

                    // throws the worker's exception if compressing failed. the token is needed: after a failure or
                    // cancellation, jobs still in the work channel are never completed by a worker
                    job.Done.Task.WaitAsync(token).GetAwaiter().GetResult();

                    WriteEntry(job);
                    _writing = null;
                    job.Release();

                    Interlocked.Increment(ref _entriesWritten);
                    _inFlight.Release(job.InFlightSize);
                }
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private static bool WaitToRead<T>(ChannelReader<T> reader, CancellationToken token)
    {
        ValueTask<bool> task = reader.WaitToReadAsync(token);
        return task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();
    }

    private void WriteEntry(Job job)
    {
        if (_records.Count >= ushort.MaxValue)
            throw new IOException($"Too many entries in archive, maximum is {ushort.MaxValue - 1}");

        byte[] name = Encoding.UTF8.GetBytes(job.Name);

        if (name.Length > ushort.MaxValue)
            throw new IOException($"Entry name is too long: '{job.Name}'");

        ushort method;
        long compressedSize;

        job.UseAlternativeIfSmaller();

        if (job.IsRaw)
        {
            method = job.RawMethod;
            compressedSize = job.RawCompressedSize;
        }
        else if (job.UncompressedSize == 0)
        {
            // match ZipArchive: empty entries are stored without data
            method = MethodStored;
            compressedSize = 0;
        }
        else
        {
            method = MethodDeflate;
            compressedSize = job.CompressedLength;
        }

        if (job.UncompressedSize >= uint.MaxValue || compressedSize >= uint.MaxValue || _position >= uint.MaxValue)
            throw new IOException($"Archive or entry '{job.Name}' is too large, zip64 is not supported");

        var record = new CentralDirectoryRecord(
            Name: name,
            Flags: Ascii.IsValid(name) ? (ushort)0 : FlagUtf8,
            Method: method,
            Version: method == MethodStored ? VersionStored : VersionDeflate,
            DosTime: ToDosTime(job.LastWriteTime),
            Crc32: job.Crc32,
            CompressedSize: (uint)compressedSize,
            UncompressedSize: (uint)job.UncompressedSize,
            Offset: (uint)_position);

        Span<byte> header = stackalloc byte[LocalFileHeaderSize];
        WriteUInt32(header, LocalFileHeaderSignature);
        WriteUInt16(header[4..], record.Version);
        WriteUInt16(header[6..], record.Flags);
        WriteUInt16(header[8..], record.Method);
        WriteUInt32(header[10..], record.DosTime);
        WriteUInt32(header[14..], record.Crc32);
        WriteUInt32(header[18..], record.CompressedSize);
        WriteUInt32(header[22..], record.UncompressedSize);
        WriteUInt16(header[26..], (ushort)name.Length);
        WriteUInt16(header[28..], 0); // extra field length

        _destination.Write(header);
        _destination.Write(name);

        if (job.IsRaw)
        {
            // streamed from the source archive, never buffered
            job.CopyRawTo(_destination);
        }
        else
        {
            job.WriteCompressedTo(_destination);
        }

        _records.Add(record);
        _position += header.Length + name.Length + compressedSize;
    }

    private void WriteCentralDirectory()
    {
        long start = _position;
        Span<byte> header = stackalloc byte[CentralDirectoryHeaderSize];

        foreach (var record in _records)
        {
            header.Clear();
            WriteUInt32(header, CentralDirectorySignature);
            WriteUInt16(header[4..], record.Version); // made by (MS-DOS)
            WriteUInt16(header[6..], record.Version); // needed to extract
            WriteUInt16(header[8..], record.Flags);
            WriteUInt16(header[10..], record.Method);
            WriteUInt32(header[12..], record.DosTime);
            WriteUInt32(header[16..], record.Crc32);
            WriteUInt32(header[20..], record.CompressedSize);
            WriteUInt32(header[24..], record.UncompressedSize);
            WriteUInt16(header[28..], (ushort)record.Name.Length);
            // extra field length, comment length, disk number, internal and external attributes are all zero
            WriteUInt32(header[42..], record.Offset);

            _destination.Write(header);
            _destination.Write(record.Name);
            _position += header.Length + record.Name.Length;
        }

        long size = _position - start;

        if (_position >= uint.MaxValue)
            throw new IOException("Archive is too large, zip64 is not supported");

        Span<byte> end = stackalloc byte[EndOfCentralDirectorySize];
        WriteUInt32(end, EndOfCentralDirectorySignature);
        WriteUInt16(end[4..], 0); // number of this disk
        WriteUInt16(end[6..], 0); // disk where central directory starts
        WriteUInt16(end[8..], (ushort)_records.Count);
        WriteUInt16(end[10..], (ushort)_records.Count);
        WriteUInt32(end[12..], (uint)size);
        WriteUInt32(end[16..], (uint)start);
        WriteUInt16(end[20..], (ushort)_comment.Length);

        _destination.Write(end);
        _destination.Write(_comment);
        _position += end.Length + _comment.Length;
    }

    /// <summary>
    /// Converts the timestamp to MS-DOS format, time in the low and date in the high 16 bits. Timestamps that the
    /// format can't represent are written as 1980-01-01 00:00, same as <see cref="ZipArchive"/>.
    /// </summary>
    public static uint ToDosTime(DateTime value)
    {
        if (value.Year is < 1980 or > 2107)
            value = new DateTime(1980, 1, 1, 0, 0, 0);

        int date = ((value.Year - 1980) << 9) | (value.Month << 5) | value.Day;
        int time = (value.Hour << 11) | (value.Minute << 5) | (value.Second / 2);
        return (uint)((date << 16) | time);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt16(Span<byte> destination, ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteUInt32(Span<byte> destination, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
    }

    private readonly record struct CentralDirectoryRecord(
        byte[] Name,
        ushort Flags,
        ushort Method,
        ushort Version,
        uint DosTime,
        uint Crc32,
        uint CompressedSize,
        uint UncompressedSize,
        uint Offset);

    private sealed class Job(string name, DateTime lastWriteTime, Stream source, Action<Stream, Stream>? transform)
    {
        private Stream? _source = source;
        private int _released;

        public string Name => name;
        public DateTime LastWriteTime => lastWriteTime;
        public TaskCompletionSource Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ArrayPoolBufferWriter<byte>? Compressed { get; private set; }
        public uint Crc32 { get; private set; }
        public long UncompressedSize { get; private set; }

        /// <summary>Size counted against <see cref="InFlightLimit"/> until the entry is written.</summary>
        public long InFlightSize { get; init; }

        /// <summary>Data is already compressed, and is copied from the source by the writer.</summary>
        public bool IsRaw { get; private set; }
        public ushort RawMethod { get; private set; }
        public long RawCompressedSize { get; private set; }

        public PrecompressedData? Alternative { get; init; }

        // chunked compression of a large file, see SplitIntoChunks
        private SafeFileHandle? _file;
        private long _fileLength;
        private Chunk[]? _chunks;
        private int _pendingChunks;

        /// <summary>Number of chunks the job is compressed in, 0 if it's compressed in one go.</summary>
        public int ChunkCount => _chunks?.Length ?? 0;

        /// <summary>Length of the compressed data, once compressed.</summary>
        public long CompressedLength
        {
            get
            {
                if (_chunks is null)
                    return Compressed!.WrittenCount;

                long length = 0;

                foreach (var chunk in _chunks)
                    length += chunk.Length;

                return length;
            }
        }

        /// <summary>
        /// Compresses the file in chunks of <see cref="ChunkSize"/>, each read with positional reads, so different
        /// threads can compress them at the same time.
        /// </summary>
        public void SplitIntoChunks(SafeFileHandle file, long length)
        {
            _file = file;
            _fileLength = length;
            _chunks = new Chunk[(int)((length + ChunkSize - 1) / ChunkSize)];
            _pendingChunks = _chunks.Length;
        }

        /// <summary>
        /// Compresses one chunk. Every chunk but the last ends with a sync flush, which ends the deflate data on
        /// a byte boundary without ending the stream, so the chunks written one after another are a single valid
        /// deflate stream. Each chunk starts without the previous chunk's data as history, which costs a little
        /// in size. Returns true for the last chunk to finish, which completes the job.
        /// </summary>
        public bool CompressChunk(int index, CompressionLevel level)
        {
            long offset = (long)index * ChunkSize;
            int length = (int)Math.Min(ChunkSize, _fileLength - offset);
            bool last = index == _chunks!.Length - 1;

            var output = new ArrayPoolBufferWriter<byte>(Math.Max(length / 2, 4096));
            byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            var crc = new System.IO.Hashing.Crc32();

            try
            {
                long usable;

                using (var deflate = new DeflateStream(output.AsStream(), level, leaveOpen: true))
                {
                    int done = 0;

                    while (done < length)
                    {
                        int read = RandomAccess.Read(_file!, buffer.AsSpan(0, Math.Min(buffer.Length, length - done)), offset + done);

                        if (read == 0)
                            throw new IOException($"File '{Name}' got smaller while it was being compressed");

                        crc.Append(buffer.AsSpan(0, read));
                        deflate.Write(buffer, 0, read);
                        done += read;
                    }

                    if (!last)
                    {
                        deflate.Flush();
                        usable = output.WrittenCount;
                    }
                    else
                    {
                        usable = -1;
                    }
                }

                // disposing a non-final chunk wrote an empty final block after the flush, which is left out
                _chunks[index] = new Chunk(output, usable < 0 ? output.WrittenCount : usable, crc.GetCurrentHashAsUInt32(), length);
                output = null;
            }
            finally
            {
                output?.Dispose();
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (Interlocked.Decrement(ref _pendingChunks) != 0)
                return false;

            uint combined = _chunks[0].Crc32;

            for (int i = 1; i < _chunks.Length; i++)
                combined = Crc32Combine.Combine(combined, _chunks[i].Crc32, _chunks[i].UncompressedLength);

            Crc32 = combined;
            UncompressedSize = _fileLength;

            _source?.Dispose();
            _source = null;
            return true;
        }

        /// <summary>Writes the compressed data, from one buffer or one chunk at a time.</summary>
        public void WriteCompressedTo(Stream destination)
        {
            if (_chunks is null)
            {
                destination.Write(Compressed!.WrittenSpan);
                return;
            }

            foreach (var chunk in _chunks)
                destination.Write(chunk.Output.WrittenSpan[..(int)chunk.Length]);
        }

        private void ReleaseChunks()
        {
            if (_chunks is null)
                return;

            for (int i = 0; i < _chunks.Length; i++)
            {
                _chunks[i].Output?.Dispose();
                _chunks[i] = default;
            }
        }

        /// <summary>
        /// Called by the writer after compressing: switches to the precompressed alternative if it's not larger.
        /// The CRC of the alternative must match the data that was just compressed, so a mismatching source
        /// is never copied.
        /// </summary>
        public void UseAlternativeIfSmaller()
        {
            if (Alternative is not { } alternative ||
                IsRaw ||
                UncompressedSize == 0 ||
                alternative.Crc32 != Crc32 ||
                alternative.UncompressedSize != UncompressedSize ||
                alternative.CompressedSize > CompressedLength)
            {
                return;
            }

            _source?.Dispose();
            _source = alternative.Open();
            Compressed?.Dispose();
            Compressed = null;
            ReleaseChunks();
            _chunks = null;
            IsRaw = true;
            RawMethod = alternative.Method;
            RawCompressedSize = alternative.CompressedSize;
        }

        public static Job Raw(
            string name,
            DateTime lastWriteTime,
            ushort method,
            uint crc32,
            long compressedSize,
            long uncompressedSize,
            Stream compressedData)
        {
            var job = new Job(name, lastWriteTime, compressedData, transform: null)
            {
                IsRaw = true,
                RawMethod = method,
                RawCompressedSize = compressedSize,
                Crc32 = crc32,
                UncompressedSize = uncompressedSize,
            };

            job.Done.SetResult();
            return job;
        }

        /// <summary>Copies the compressed data of a raw entry to the destination, and disposes the source.</summary>
        public void CopyRawTo(Stream destination)
        {
            Stream input = _source!;
            byte[] buffer = ArrayPool<byte>.Shared.Rent((int)Math.Clamp(RawCompressedSize, 1, 1 << 16));

            try
            {
                long remaining = RawCompressedSize;

                while (remaining > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(remaining, buffer.Length));

                    if (read == 0)
                        throw new InvalidDataException($"Unexpected end of compressed data for '{Name}'");

                    destination.Write(buffer, 0, read);
                    remaining -= read;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                input.Dispose();
                _source = null;
            }
        }

        public void Compress(CompressionLevel level)
        {
            Stream input = _source!;

            try
            {
                int initialCapacity = 4096;

                if (input.CanSeek)
                {
                    // most compressible data (tga, bsp) shrinks by more than half
                    initialCapacity = (int)Math.Clamp(input.Length / 2, 4096, 1 << 24);
                }

                Compressed = new ArrayPoolBufferWriter<byte>(initialCapacity);

                using var sink = new CrcDeflateStream(Compressed.AsStream(), level);

                if (transform is null)
                {
                    input.CopyTo(sink);
                }
                else
                {
                    transform(input, sink);
                }

                sink.Finish();
                Crc32 = sink.Crc32;
                UncompressedSize = sink.BytesWritten;
            }
            finally
            {
                input.Dispose();
                _source = null;
            }
        }

        /// <summary>
        /// Disposes the source and returns the compressed buffer to the pool. Must not be called while a worker is
        /// compressing this job.
        /// </summary>
        public void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _source?.Dispose();
            _source = null;
            Compressed?.Dispose();
            Compressed = null;
            ReleaseChunks();
        }
    }

    private readonly record struct WorkItem(Job Job, int Chunk);

    private readonly record struct Chunk(ArrayPoolBufferWriter<byte> Output, long Length, uint Crc32, int UncompressedLength);

    /// <summary>
    /// Limits the number and total size of entries waiting to be written.
    /// </summary>
    private sealed class InFlightLimit(int maxEntries, long maxBytes)
    {
        private readonly object _lock = new();
        private int _entries;
        private long _bytes;

        /// <summary>
        /// Waits until there is room for an entry of the given size. An entry larger than the limit is let
        /// through when nothing else is in flight, so it can't wait forever.
        /// </summary>
        public void Acquire(long size, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_lock)
            {
                if (HasRoom(size))
                {
                    _entries++;
                    _bytes += size;
                    return;
                }
            }

            using var registration = cancellationToken.Register(
                static state =>
                {
                    var limit = (InFlightLimit)state!;
                    lock (limit._lock) Monitor.PulseAll(limit._lock);
                },
                this);

            lock (_lock)
            {
                while (!HasRoom(size))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Monitor.Wait(_lock);
                }

                cancellationToken.ThrowIfCancellationRequested();
                _entries++;
                _bytes += size;
            }
        }

        public void Release(long size)
        {
            lock (_lock)
            {
                _entries--;
                _bytes -= size;
                Monitor.PulseAll(_lock);
            }
        }

        private bool HasRoom(long size) => _entries < maxEntries && (_entries == 0 || _bytes + size <= maxBytes);
    }

    /// <summary>
    /// Write-only stream that computes the CRC and length of the data while deflating it.
    /// Not seekable, so <see cref="StreamWriter"/> writes the encoding preamble, same as with a zip entry stream.
    /// </summary>
    private sealed class CrcDeflateStream(Stream output, CompressionLevel level) : Stream
    {
        private readonly DeflateStream _deflate = new(output, level, leaveOpen: true);
        private readonly Crc32 _crc = new();
        private bool _finished;

        public long BytesWritten { get; private set; }
        public uint Crc32 => _crc.GetCurrentHashAsUInt32();

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !_finished;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            ObjectDisposedException.ThrowIf(_finished, this);
            _crc.Append(buffer);
            _deflate.Write(buffer);
            BytesWritten += buffer.Length;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            Write(buffer.AsSpan(offset, count));
        }

        public override void WriteByte(byte value) => Write([value]);

        /// <summary>No-op, flushing deflate in the middle of an entry only makes the output larger.</summary>
        public override void Flush()
        {
        }

        /// <summary>Writes the final deflate block.</summary>
        public void Finish()
        {
            if (!_finished)
            {
                _finished = true;
                _deflate.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Finish();

            base.Dispose(disposing);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
