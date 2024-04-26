
namespace Pack3r.Extensions;

public sealed class CountingStream : Stream
{
    public override bool CanRead => Null.CanRead;
    public override bool CanSeek => Null.CanSeek;
    public override bool CanWrite => Null.CanWrite;
    public override long Length => Null.Length;
    public override long Position
    {
        get => _written;
        set => throw new NotSupportedException();
    }

    private long _written;

    public override void Write(byte[] buffer, int offset, int count)
    {
        _written += count;
        Null.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _written += buffer.Length;
        Null.Write(buffer);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _written += count;
        return Null.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _written += buffer.Length;
        return Null.WriteAsync(buffer, cancellationToken);
    }

    public override void WriteByte(byte value)
    {
        _written++;
        Null.WriteByte(value);
    }

    public override long Seek(long offset, SeekOrigin origin) => Null.Seek(offset, origin);
    public override void Flush() => Null.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => Null.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();
    public override void SetLength(long value) => throw new NotImplementedException();
}
