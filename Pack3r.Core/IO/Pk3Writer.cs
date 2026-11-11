using System.Buffers.Binary;
using System.IO.Compression;
using System.IO.Hashing;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Channels;
using CommunityToolkit.HighPerformance;
using CommunityToolkit.HighPerformance.Buffers;

namespace Pack3r.IO;

/// <summary>
/// Writes a zip archive while compressing its entries in parallel.
/// </summary>
/// <remarks>
/// <para>
/// Entries are queued with <see cref="Add"/> from a single producer thread, compressed on worker threads,
/// and written to the destination in the order they were added, so the output is deterministic.
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

    private readonly Channel<Job> _work = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = false });

    private readonly Channel<Job> _ordered = Channel.CreateUnbounded<Job>(
        new UnboundedChannelOptions { SingleWriter = true, SingleReader = true });

    private readonly SemaphoreSlim _inFlight;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;
    private readonly Task _writer;
    private readonly List<CentralDirectoryRecord> _records = [];

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
    /// <param name="cancellationToken">Token to cancel the compression</param>
    public Pk3Writer(
        Stream destination,
        string? comment = null,
        CompressionLevel compressionLevel = CompressionLevel.Optimal,
        int? maxDegreeOfParallelism = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDegreeOfParallelism ?? 1, 1, nameof(maxDegreeOfParallelism));

        if (!destination.CanWrite)
            throw new ArgumentException("Destination stream must be writable", nameof(destination));

        _destination = destination;
        _comment = Encoding.UTF8.GetBytes(comment ?? "");
        _compressionLevel = compressionLevel;

        if (_comment.Length > ushort.MaxValue)
            throw new ArgumentException("Archive comment is too long", nameof(comment));

        int workerCount = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount - 1);

        // bounds memory use: an entry holds its compressed data until it has been written. a bit of headroom lets
        // workers keep going while the writer waits for a large entry (e.g. a lightmap) at the head of the queue
        _inFlight = new SemaphoreSlim(workerCount * 2 + 2);
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
    /// <exception cref="OperationCanceledException">The token passed to the constructor was canceled</exception>
    /// <remarks>
    /// If compressing or writing a previous entry failed, that exception is thrown.
    /// </remarks>
    public void Add(string name, DateTime lastWriteTime, Stream source, Action<Stream, Stream>? transform = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        bool queued = false;

        try
        {
            ArgumentException.ThrowIfNullOrEmpty(name);
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_completed)
                throw new InvalidOperationException("Cannot add entries after the archive has been completed");

            _failure?.Throw();

            try
            {
                _inFlight.Wait(_cts.Token);
            }
            catch (OperationCanceledException) when (_failure is not null)
            {
                _failure.Throw();
            }

            var job = new Job(name, lastWriteTime, source, transform);

            // writer sees jobs in the order they were added, workers may finish them in any order.
            // both channels are unbounded and only completed by this producer, so the writes always succeed
            _ordered.Writer.TryWrite(job);
            _work.Writer.TryWrite(job);
            _entriesAdded++;
            queued = true;
        }
        finally
        {
            if (!queued)
                source.Dispose();
        }
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
        _inFlight.Dispose();
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
        ChannelReader<Job> reader = _work.Reader;

        try
        {
            while (WaitToRead(reader, token))
            {
                while (reader.TryRead(out Job? job))
                {
                    try
                    {
                        token.ThrowIfCancellationRequested();
                        job.Compress(_compressionLevel);
                        job.Done.TrySetResult();
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

                    // throws the worker's exception if compressing failed
                    job.Done.Task.WaitAsync(token).GetAwaiter().GetResult();

                    WriteEntry(job);
                    _writing = null;
                    job.Release();

                    Interlocked.Increment(ref _entriesWritten);
                    _inFlight.Release();
                }
            }
        }
        catch (Exception e)
        {
            Fail(e);
        }
    }

    private static bool WaitToRead(ChannelReader<Job> reader, CancellationToken token)
    {
        ValueTask<bool> task = reader.WaitToReadAsync(token);
        return task.IsCompletedSuccessfully ? task.Result : task.AsTask().GetAwaiter().GetResult();
    }

    private void WriteEntry(Job job)
    {
        if (_records.Count >= ushort.MaxValue)
            throw new IOException($"Too many entries in archive, maximum is {ushort.MaxValue - 1}");

        byte[] name = EncodeName(job.Name, out bool utf8);

        if (name.Length > ushort.MaxValue)
            throw new IOException($"Entry name is too long: '{job.Name}'");

        // match ZipArchive: empty entries are stored without data
        bool stored = job.UncompressedSize == 0;
        ReadOnlySpan<byte> data = stored ? default : job.Compressed!.WrittenSpan;

        if (job.UncompressedSize >= uint.MaxValue || _position >= uint.MaxValue)
            throw new IOException($"Archive or entry '{job.Name}' is too large, zip64 is not supported");

        var record = new CentralDirectoryRecord(
            Name: name,
            Flags: utf8 ? FlagUtf8 : (ushort)0,
            Method: stored ? MethodStored : MethodDeflate,
            Version: stored ? VersionStored : VersionDeflate,
            DosTime: ToDosTime(job.LastWriteTime),
            Crc32: job.Crc32,
            CompressedSize: (uint)data.Length,
            UncompressedSize: (uint)job.UncompressedSize,
            Offset: (uint)_position);

        Span<byte> header = stackalloc byte[LocalFileHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, LocalFileHeaderSignature);
        BinaryPrimitives.WriteUInt16LittleEndian(header[4..], record.Version);
        BinaryPrimitives.WriteUInt16LittleEndian(header[6..], record.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(header[8..], record.Method);
        BinaryPrimitives.WriteUInt32LittleEndian(header[10..], record.DosTime);
        BinaryPrimitives.WriteUInt32LittleEndian(header[14..], record.Crc32);
        BinaryPrimitives.WriteUInt32LittleEndian(header[18..], record.CompressedSize);
        BinaryPrimitives.WriteUInt32LittleEndian(header[22..], record.UncompressedSize);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 0); // extra field length

        _destination.Write(header);
        _destination.Write(name);
        _destination.Write(data);

        _records.Add(record);
        _position += header.Length + name.Length + data.Length;
    }

    private void WriteCentralDirectory()
    {
        long start = _position;
        Span<byte> header = stackalloc byte[CentralDirectoryHeaderSize];

        foreach (var record in _records)
        {
            header.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(header, CentralDirectorySignature);
            BinaryPrimitives.WriteUInt16LittleEndian(header[4..], record.Version); // made by (MS-DOS)
            BinaryPrimitives.WriteUInt16LittleEndian(header[6..], record.Version); // needed to extract
            BinaryPrimitives.WriteUInt16LittleEndian(header[8..], record.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(header[10..], record.Method);
            BinaryPrimitives.WriteUInt32LittleEndian(header[12..], record.DosTime);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], record.Crc32);
            BinaryPrimitives.WriteUInt32LittleEndian(header[20..], record.CompressedSize);
            BinaryPrimitives.WriteUInt32LittleEndian(header[24..], record.UncompressedSize);
            BinaryPrimitives.WriteUInt16LittleEndian(header[28..], (ushort)record.Name.Length);
            // extra field length, comment length, disk number, internal and external attributes are all zero
            BinaryPrimitives.WriteUInt32LittleEndian(header[42..], record.Offset);

            _destination.Write(header);
            _destination.Write(record.Name);
            _position += header.Length + record.Name.Length;
        }

        long size = _position - start;

        if (_position >= uint.MaxValue)
            throw new IOException("Archive is too large, zip64 is not supported");

        Span<byte> end = stackalloc byte[EndOfCentralDirectorySize];
        BinaryPrimitives.WriteUInt32LittleEndian(end, EndOfCentralDirectorySignature);
        BinaryPrimitives.WriteUInt16LittleEndian(end[4..], 0); // number of this disk
        BinaryPrimitives.WriteUInt16LittleEndian(end[6..], 0); // disk where central directory starts
        BinaryPrimitives.WriteUInt16LittleEndian(end[8..], (ushort)_records.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(end[10..], (ushort)_records.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(end[12..], (uint)size);
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], (uint)start);
        BinaryPrimitives.WriteUInt16LittleEndian(end[20..], (ushort)_comment.Length);

        _destination.Write(end);
        _destination.Write(_comment);
        _position += end.Length + _comment.Length;
    }

    private static byte[] EncodeName(string name, out bool utf8)
    {
        // same rule as ZipArchive: anything outside printable ASCII is written as UTF-8 and flagged as such
        utf8 = false;

        foreach (char c in name)
        {
            if (c is < (char)32 or > (char)126)
            {
                utf8 = true;
                break;
            }
        }

        return utf8 ? Encoding.UTF8.GetBytes(name) : Encoding.ASCII.GetBytes(name);
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
        }
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
