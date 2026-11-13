using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Tests;

public class Pk3WriterTests
{
    private static readonly DateTime _timestamp = new(2024, 5, 17, 13, 37, 42);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Should_Roundtrip_Entries_In_Order(int threads)
    {
        var entries = new List<(string name, byte[] data)>
        {
            ("maps/test.bsp", Compressible(3_000_000, seed: 1)),
            ("maps/test/lm_0000.tga", Compressible(5_000_000, seed: 2)),
            ("maps/test/lm_0001.tga", Compressible(1_000_000, seed: 3)),
            ("textures/random.jpg", Random(200_000, seed: 4)),
            ("textures/empty.tga", []),
            ("sound/tiny.wav", [1, 2, 3]),
            ("textures/ääkköset.tga", Compressible(10_000, seed: 5)),
        };

        // lots of small entries to go past the in-flight limit
        for (int i = 0; i < 300; i++)
        {
            entries.Add(($"models/small_{i:000}.md3", Compressible(1000 + i * 37, seed: 100 + i)));
        }

        using var destination = new MemoryStream();
        long size;

        await using (var writer = new Pk3Writer(destination, comment: "Created with test", maxDegreeOfParallelism: threads))
        {
            foreach (var (name, data) in entries)
            {
                writer.Add(name, _timestamp, new MemoryStream(data));
            }

            Assert.Equal(entries.Count, writer.EntriesAdded);
            size = await writer.CompleteAsync();
            Assert.Equal(entries.Count, writer.EntriesWritten);
        }

        Assert.Equal(destination.Length, size);

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);

        Assert.Equal("Created with test", archive.Comment);
        Assert.Equal(entries.Select(e => e.name), archive.Entries.Select(e => e.FullName));

        foreach (var ((name, data), entry) in entries.Zip(archive.Entries))
        {
            Assert.Equal(data.Length, entry.Length);
            Assert.Equal(Crc32.HashToUInt32(data), entry.Crc32);
            Assert.Equal(_timestamp, entry.LastWriteTime.DateTime);
            Assert.Equal(data, ReadAll(entry));

            if (data.Length > 100_000 && name.EndsWith(".tga"))
            {
                Assert.True(entry.CompressedLength < entry.Length / 2, $"{name} was not compressed");
            }
        }
    }

    [Fact]
    public async Task Should_Write_Same_Compressed_Data_As_ZipArchive()
    {
        byte[] data = Compressible(2_000_000, seed: 7);

        using var expected = new MemoryStream();

        using (var archive = new ZipArchive(expected, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("maps/test/lm_0000.tga", CompressionLevel.Optimal);
            using var stream = entry.Open();
            stream.Write(data);
        }

        using var actual = new MemoryStream();

        await using (var writer = new Pk3Writer(actual))
        {
            writer.Add("maps/test/lm_0000.tga", _timestamp, new MemoryStream(data));
            await writer.CompleteAsync();
        }

        Assert.Equal(CompressedLength(expected), CompressedLength(actual));

        static long CompressedLength(MemoryStream stream)
        {
            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return archive.Entries.Single().CompressedLength;
        }
    }

    [Fact]
    public async Task Should_Apply_Transform()
    {
        using var destination = new MemoryStream();

        await using (var writer = new Pk3Writer(destination))
        {
            writer.Add(
                "scripts/test.arena",
                _timestamp,
                new MemoryStream(Encoding.UTF8.GetBytes("map \"test\"\nlongname \"Test\"\n")),
                (input, output) =>
                {
                    using var reader = new StreamReader(input);
                    using var textWriter = new StreamWriter(output, Encoding.UTF8);
                    string? line;

                    while ((line = reader.ReadLine()) is not null)
                    {
                        textWriter.WriteLine(line.Replace("test", "renamed"));
                    }
                });

            await writer.CompleteAsync();
        }

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        byte[] bytes = ReadAll(archive.Entries.Single());

        // same as writing to a zip entry stream with StreamWriter: UTF-8 preamble is included
        Assert.Equal(Encoding.UTF8.Preamble.ToArray(), bytes[..3]);
        Assert.Equal(
            $"map \"renamed\"{Environment.NewLine}longname \"Test\"{Environment.NewLine}",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task Should_Compute_Crc_Of_Partial_Writes()
    {
        byte[] buffer = "xxhelloxx"u8.ToArray();
        using var destination = new MemoryStream();

        await using (var writer = new Pk3Writer(destination))
        {
            // every write overload with an offset into a larger buffer
            writer.Add("entry.txt", _timestamp, new MemoryStream(), (_, output) =>
            {
                output.Write(buffer, 2, 2);
                output.Write(buffer.AsSpan(4, 1));
                output.WriteAsync(buffer, 5, 1).GetAwaiter().GetResult();
                output.WriteAsync(buffer.AsMemory(6, 1)).AsTask().GetAwaiter().GetResult();
            });

            await writer.CompleteAsync();
        }

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        var entry = archive.Entries.Single();

        Assert.Equal("hello"u8.ToArray(), ReadAll(entry));
        Assert.Equal(Crc32.HashToUInt32("hello"u8), entry.Crc32);
        Assert.Equal(5, entry.Length);
    }

    [Fact]
    public async Task Should_Write_To_NonSeekable_Stream()
    {
        using var destination = new CountingStream();

        await using var writer = new Pk3Writer(destination);
        writer.Add("a.txt", _timestamp, new MemoryStream(Compressible(10_000, seed: 1)));
        writer.Add("b.txt", _timestamp, new MemoryStream(Compressible(10_000, seed: 2)));
        long size = await writer.CompleteAsync();

        Assert.Equal(destination.Position, size);
    }

    [Fact]
    public async Task Should_Throw_Original_Exception_From_Failed_Entry()
    {
        using var destination = new MemoryStream();
        var sources = new List<TrackingStream>();

        await using var writer = new Pk3Writer(destination, maxDegreeOfParallelism: 2);

        writer.Add("ok.txt", _timestamp, Track(new TrackingStream(new byte[1000])));
        writer.Add("bad.txt", _timestamp, Track(new TrackingStream(new byte[1000])), (_, _) => throw new InvalidDataException("bad data"));

        // keeps adding until the failure is observed, or the archive is completed
        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                writer.Add($"more_{i}.txt", _timestamp, Track(new TrackingStream(new byte[1000])));
            }

            await writer.CompleteAsync();
        });

        Assert.Equal("bad data", exception.Message);

        await writer.DisposeAsync();
        Assert.All(sources, s => Assert.True(s.Disposed));

        TrackingStream Track(TrackingStream stream)
        {
            sources.Add(stream);
            return stream;
        }
    }

    [Fact]
    public async Task Should_Release_Sources_When_Disposed_Without_Completing()
    {
        using var destination = new MemoryStream();
        var sources = Enumerable.Range(0, 5).Select(_ => new TrackingStream(Compressible(100_000, seed: 1))).ToList();

        var writer = new Pk3Writer(destination, maxDegreeOfParallelism: 1);

        foreach (var source in sources)
        {
            writer.Add("file.tga", _timestamp, source);
        }

        await writer.DisposeAsync();

        Assert.All(sources, s => Assert.True(s.Disposed));
        Assert.Throws<ObjectDisposedException>(() => writer.Add("x", _timestamp, new MemoryStream()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Should_Release_Sources_When_Disposed_While_Adding_On_Another_Thread(int delayMs)
    {
        for (int iteration = 0; iteration < 20; iteration++)
        {
            using var destination = new MemoryStream();
            var sources = new System.Collections.Concurrent.ConcurrentQueue<TrackingStream>();
            var writer = new Pk3Writer(destination, maxDegreeOfParallelism: 2);

            // like the map files queued while the map is parsed
            Task producer = Task.Run(() =>
            {
                for (int i = 0; i < 200; i++)
                {
                    var source = new TrackingStream(Compressible(20_000, seed: i));
                    sources.Enqueue(source);
                    writer.Add($"file_{i}.tga", _timestamp, source);
                }
            });

            await Task.Delay(delayMs);
            await writer.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));

            Exception? exception = await Record.ExceptionAsync(() => producer.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(
                exception is null or ObjectDisposedException or OperationCanceledException,
                $"Unexpected exception: {exception}");

            Assert.All(sources, s => Assert.True(s.Disposed));
        }
    }

    [Fact]
    public async Task Should_Dispose_Source_If_Add_Fails()
    {
        using var destination = new MemoryStream();
        await using var writer = new Pk3Writer(destination);
        await writer.CompleteAsync();

        var source = new TrackingStream([1, 2, 3]);
        Assert.Throws<InvalidOperationException>(() => writer.Add("late.txt", _timestamp, source));
        Assert.True(source.Disposed);
    }

    [Fact]
    public async Task Should_Throw_When_Canceled()
    {
        using var cts = new CancellationTokenSource();
        using var destination = new MemoryStream();
        await using var writer = new Pk3Writer(destination, maxDegreeOfParallelism: 1, cancellationToken: cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            for (int i = 0; i < 1000; i++)
            {
                writer.Add($"file_{i}.txt", _timestamp, new MemoryStream(new byte[1000]));
            }

            await writer.CompleteAsync();
        });
    }

    [Fact]
    public async Task Should_Copy_Raw_Entries_As_Is()
    {
        byte[] data = Compressible(1_000_000, seed: 3);
        byte[] deflated = Deflate(data, CompressionLevel.Fastest);

        using var destination = new MemoryStream();

        await using (var writer = new Pk3Writer(destination, maxDegreeOfParallelism: 2))
        {
            writer.Add("compressed.tga", _timestamp, new MemoryStream(data));
            writer.AddRaw("raw.tga", _timestamp, 8, Crc32.HashToUInt32(data), deflated.Length, data.Length, new MemoryStream(deflated));
            writer.AddRaw("stored.txt", _timestamp, 0, Crc32.HashToUInt32("abc"u8), 3, 3, new MemoryStream("abc"u8.ToArray()));
            writer.AddRaw("empty.txt", _timestamp, 0, 0, 0, 0, new MemoryStream());
            await writer.CompleteAsync();
        }

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);

        Assert.Equal(["compressed.tga", "raw.tga", "stored.txt", "empty.txt"], archive.Entries.Select(e => e.FullName));
        Assert.Equal(data, ReadAll(archive.GetEntry("compressed.tga")!));
        Assert.Equal(data, ReadAll(archive.GetEntry("raw.tga")!));
        Assert.Equal(deflated.Length, archive.GetEntry("raw.tga")!.CompressedLength);
        Assert.Equal("abc"u8.ToArray(), ReadAll(archive.GetEntry("stored.txt")!));
        Assert.Empty(ReadAll(archive.GetEntry("empty.txt")!));
    }

    [Fact]
    public async Task Should_Throw_If_Raw_Data_Is_Truncated()
    {
        using var destination = new MemoryStream();
        await using var writer = new Pk3Writer(destination);

        writer.AddRaw("raw.tga", _timestamp, 8, 0, compressedSize: 100, uncompressedSize: 1000, new MemoryStream(new byte[50]));

        await Assert.ThrowsAsync<InvalidDataException>(writer.CompleteAsync);
    }

    [Fact]
    public async Task Should_Validate_Raw_Entry()
    {
        using var destination = new MemoryStream();
        await using var writer = new Pk3Writer(destination);

        var source = new TrackingStream([1, 2, 3]);
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.AddRaw("x", _timestamp, 9, 0, 3, 3, source));
        Assert.True(source.Disposed);

        source = new TrackingStream([1, 2, 3]);
        Assert.Throws<ArgumentException>(() => writer.AddRaw("x", _timestamp, 0, 0, 3, 4, source));
        Assert.True(source.Disposed);

        // the failed adds released their slots
        for (int i = 0; i < 100; i++)
        {
            writer.AddRaw($"ok_{i}", _timestamp, 0, 0, 0, 0, new MemoryStream());
        }

        await writer.CompleteAsync();
        Assert.Equal(100, writer.EntriesWritten);
    }

    [Theory]
    [InlineData(true, true)] // alternative is smaller, and matches
    [InlineData(false, true)] // alternative is larger
    [InlineData(true, false)] // alternative is smaller, but has the wrong CRC
    public async Task Should_Use_Alternative_If_Smaller(bool alternativeSmaller, bool crcMatches)
    {
        byte[] data = Compressible(500_000, seed: 4);
        byte[] alternative = alternativeSmaller
            ? Deflate(data, CompressionLevel.SmallestSize)
            : Deflate(data, CompressionLevel.NoCompression);
        long compressedByWriter = Deflate(data, CompressionLevel.Fastest).Length;

        bool opened = false;
        using var destination = new MemoryStream();

        await using (var writer = new Pk3Writer(destination, compressionLevel: CompressionLevel.Fastest))
        {
            writer.Add("entry.tga", _timestamp, new MemoryStream(data), alternative: new PrecompressedData(
                8,
                crcMatches ? Crc32.HashToUInt32(data) : 12345u,
                alternative.Length,
                data.Length,
                () =>
                {
                    opened = true;
                    return new MemoryStream(alternative);
                }));

            await writer.CompleteAsync();
        }

        destination.Position = 0;
        using var archive = new ZipArchive(destination, ZipArchiveMode.Read);
        var entry = archive.Entries.Single();

        bool expectAlternative = alternativeSmaller && crcMatches;
        Assert.Equal(data, ReadAll(entry));
        Assert.Equal(expectAlternative, opened);
        Assert.Equal(expectAlternative ? alternative.Length : compressedByWriter, entry.CompressedLength);
    }

    [Fact]
    public async Task Should_Not_Allow_Alternative_With_Transform()
    {
        using var destination = new MemoryStream();
        await using var writer = new Pk3Writer(destination);
        var source = new TrackingStream([1, 2, 3]);

        Assert.Throws<ArgumentException>(() => writer.Add(
            "x", _timestamp, source, (i, o) => i.CopyTo(o), new PrecompressedData(8, 0, 3, 3, () => new MemoryStream())));
        Assert.True(source.Disposed);
    }

    private static byte[] Deflate(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();

        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    [Theory]
    [InlineData(1970, 1, 1, 0, 0, 0, 0x00210000u)] // too early, clamped to 1980-01-01
    [InlineData(1980, 1, 1, 0, 0, 0, 0x00210000u)]
    [InlineData(2024, 5, 17, 13, 37, 42, 0x58B16CB5u)]
    public void Should_Convert_Dos_Time(int year, int month, int day, int hour, int minute, int second, uint expected)
    {
        Assert.Equal(expected, Pk3Writer.ToDosTime(new DateTime(year, month, day, hour, minute, second)));
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>Repetitive data that compresses roughly like a texture.</summary>
    private static byte[] Compressible(int length, int seed)
    {
        var random = new Random(seed);
        var data = new byte[length];
        byte value = 0;

        for (int i = 0; i < data.Length; i++)
        {
            if (random.Next(16) == 0)
                value = (byte)random.Next(8);

            data[i] = value;
        }

        return data;
    }

    private static byte[] Random(int length, int seed)
    {
        var data = new byte[length];
        new Random(seed).NextBytes(data);
        return data;
    }

    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
