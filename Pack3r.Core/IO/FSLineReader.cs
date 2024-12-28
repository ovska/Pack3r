using System.Collections;
using System.Text;
using Pack3r.Models;

namespace Pack3r.IO;

public class FSLineReader() : ILineReader
{
    public IAsyncEnumerable<Line> ReadLines(
        string path,
        CancellationToken cancellationToken)
    {
        return new AsyncEnumerator(path, cancellationToken);
    }

    public IAsyncEnumerable<Line> ReadLines(IAsset asset, CancellationToken cancellationToken)
    {
        return new AsyncEnumerator(asset, cancellationToken);
    }

    public IEnumerable<Line> ReadRawLines(string path)
    {
        return new Enumerator(path);
    }

    private sealed class Enumerator(string path) : IEnumerable<Line>, IEnumerator<Line>
    {
        private readonly StreamReader _reader = new(
            new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan),
            Encoding.ASCII,
            detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096,
            leaveOpen: false);

        public Line Current { get; private set; }
        object IEnumerator.Current => Current;

        public void Dispose() => _reader.Dispose();

        private int index;

        public bool MoveNext()
        {
            string? line;

            while ((line = _reader.ReadLine()) is not null)
            {
                index++;

                if (line.Length == 0)
                    continue;

                Current = new Line(path, index, line, keepRaw: true);

                if (Current.HasValue)
                    return true;
            }

            return false;
        }

        public void Reset() => throw new NotSupportedException();

        IEnumerator<Line> IEnumerable<Line>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }

    private sealed class AsyncEnumerator : IAsyncEnumerable<Line>, IAsyncEnumerator<Line>
    {
        private readonly StreamReader _reader;
        private readonly string _path;
        private readonly CancellationToken _cancellationToken;

        public AsyncEnumerator(string path, CancellationToken cancellationToken)
        {
            _path = path;
            _reader = new StreamReader(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, FileOptions.SequentialScan),
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);
            _cancellationToken = cancellationToken;
        }

        public AsyncEnumerator(IAsset asset, CancellationToken cancellationToken)
        {
            _path = asset.FullPath;
            _reader = new StreamReader(
                asset.OpenRead(isAsync: true),
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);
            _cancellationToken = cancellationToken;
        }

        public Line Current { get; private set; }

        private int index;

        public async ValueTask<bool> MoveNextAsync()
        {
            string? line;

            while ((line = await _reader.ReadLineAsync(_cancellationToken)) is not null)
            {
                index++;

                if (line.Length == 0)
                    continue;

                Current = new Line(_path, index, line, keepRaw: false);

                if (Current.HasValue)
                    return true;
            }

            return false;
        }

        public ValueTask DisposeAsync()
        {
            _reader.Dispose();
            return default;
        }

        IAsyncEnumerator<Line> IAsyncEnumerable<Line>.GetAsyncEnumerator(CancellationToken cancellationToken) => this;
    }
}
