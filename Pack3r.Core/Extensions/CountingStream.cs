
namespace Pack3r.Extensions;

// used for dry run size estimation
public sealed class CountingStream : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _written;

    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    private long _written;

    public override void Write(byte[] buffer, int offset, int count)
    {
        _written += count;
        Null.Write(buffer, offset, count); // validate args
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _written += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _written += count;
        return Null.WriteAsync(buffer, offset, count, cancellationToken); // validate args
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _written += buffer.Length;
        return default;
    }

    public override void WriteByte(byte value)
    {
        _written++;
    }

    public override void Flush() {}
    public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
