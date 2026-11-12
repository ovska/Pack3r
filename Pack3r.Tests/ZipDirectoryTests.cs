using System.IO.Compression;
using System.Text;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests;

public sealed class ZipDirectoryTests : IDisposable
{
    private static readonly DateTime _timestamp = new(2024, 5, 17, 13, 37, 42);
    private readonly string _directory = Directory.CreateTempSubdirectory("pack3r_tests_").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Should_Read_Same_Entries_As_ZipArchive()
    {
        string path = CreateZip(
            ("textures/deflated.tga", Compressible(500_000, 1), CompressionLevel.Optimal),
            ("textures/stored.tga", Compressible(10_000, 2), CompressionLevel.NoCompression),
            ("textures/empty.tga", [], CompressionLevel.Optimal),
            ("textures/ääkköset.tga", Compressible(1000, 3), CompressionLevel.Fastest));

        using var handle = File.OpenHandle(path);
        var directory = ZipDirectory.TryRead(handle);
        Assert.NotNull(directory);

        using var archive = ZipFile.OpenRead(path);
        Assert.Equal(archive.Entries.Count, directory.Count);

        foreach (var expected in archive.Entries)
        {
            Assert.True(directory.TryGetEntry(expected.FullName, out var entry), expected.FullName);
            Assert.Equal(expected.Crc32, entry.Crc32);
            Assert.Equal(expected.CompressedLength, entry.CompressedSize);
            Assert.Equal(expected.Length, entry.UncompressedSize);
            Assert.False(entry.IsEncrypted);

            using var data = new FileRangeStream(handle, directory.GetDataOffset(handle, entry), entry.CompressedSize);
            using Stream content = entry.Method switch
            {
                8 => new DeflateStream(data, CompressionMode.Decompress),
                0 => data,
                _ => throw new InvalidOperationException($"Unexpected method {entry.Method}"),
            };

            Assert.Equal(ReadAll(expected.Open()), ReadAll(content));
        }
    }

    [Fact]
    public async Task Should_Read_Entries_Concurrently_From_One_Handle()
    {
        var files = Enumerable.Range(0, 16)
            .Select(i => ($"textures/file_{i}.tga", Compressible(200_000 + i * 1000, i), CompressionLevel.Optimal))
            .ToArray();

        string path = CreateZip(files);

        using var handle = File.OpenHandle(path);
        var directory = ZipDirectory.TryRead(handle)!;

        await Parallel.ForEachAsync(files, async (file, _) =>
        {
            await Task.Yield();
            Assert.True(directory.TryGetEntry(file.Item1, out var entry));
            var data = new FileRangeStream(handle, directory.GetDataOffset(handle, entry), entry.CompressedSize);
            Assert.Equal(file.Item2, ReadAll(new DeflateStream(data, CompressionMode.Decompress)));
        });
    }

    [Fact]
    public void Should_Skip_Duplicate_Names()
    {
        string path = CreateZip(
            ("textures/dupe.tga", [1, 2, 3], CompressionLevel.Optimal),
            ("textures/dupe.tga", [4, 5, 6], CompressionLevel.Optimal),
            ("textures/unique.tga", [7, 8, 9], CompressionLevel.Optimal));

        using var handle = File.OpenHandle(path);
        var directory = ZipDirectory.TryRead(handle)!;

        Assert.False(directory.TryGetEntry("textures/dupe.tga", out _));
        Assert.True(directory.TryGetEntry("textures/unique.tga", out _));
    }

    [Fact]
    public void Should_Not_Read_Zip64()
    {
        // more entries than fit in the regular end of central directory record
        string path = Path.Combine(_directory, "zip64.pk3");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            for (int i = 0; i < ushort.MaxValue + 1; i++)
            {
                archive.CreateEntry($"{i}.txt", CompressionLevel.NoCompression);
            }
        }

        using var handle = File.OpenHandle(path);
        Assert.Null(ZipDirectory.TryRead(handle));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(10_000)]
    public void Should_Not_Read_Non_Zip(int length)
    {
        string path = Path.Combine(_directory, "notzip.pk3");
        File.WriteAllBytes(path, Compressible(length, 1));

        using var handle = File.OpenHandle(path);
        Assert.Null(ZipDirectory.TryRead(handle));
    }

    [Fact]
    public async Task Pk3Asset_Should_Copy_Deflated_And_Recompress_Stored_Entries()
    {
        var files = new (string, byte[], CompressionLevel)[]
        {
            ("textures/deflated.tga", Compressible(500_000, 1), CompressionLevel.Optimal),
            ("textures/fastest.tga", Compressible(400_000, 2), CompressionLevel.Fastest),
            ("textures/stored.tga", Compressible(300_000, 3), CompressionLevel.NoCompression),
            ("textures/empty.tga", [], CompressionLevel.Optimal),
        };

        string path = CreateZip(files);
        using var destination = new MemoryStream();

        using (var source = new Pk3AssetSource(path, notPacked: false))
        {
            // textures are also indexed without the extension, so the same asset appears more than once
            var assets = source.Assets.Values.Distinct().OrderBy(a => a.Name, StringComparer.Ordinal).ToList();
            Assert.Equal(files.Length, assets.Count);

            await using var writer = new Pk3Writer(destination);

            foreach (var asset in assets)
            {
                asset.AddTo(writer);
            }

            // transformed entries are decompressed on a compression thread
            assets.Single(a => a.Name == "textures/deflated.tga").AddTo(writer, "textures/transformed.tga", (input, output) =>
            {
                input.CopyTo(output);
                output.Write("appended"u8);
            });

            await writer.CompleteAsync();
        }

        destination.Position = 0;
        using var result = new ZipArchive(destination, ZipArchiveMode.Read);
        using var original = ZipFile.OpenRead(path);

        foreach (var (name, data, level) in files)
        {
            var entry = result.GetEntry(name)!;
            var originalEntry = original.GetEntry(name)!;

            Assert.Equal(data, ReadAll(entry.Open()));
            Assert.Equal(originalEntry.Crc32, entry.Crc32);
            Assert.Equal(_timestamp, entry.LastWriteTime.DateTime);

            if (level == CompressionLevel.NoCompression && data.Length > 0)
            {
                Assert.True(entry.CompressedLength < data.Length / 2, "stored entry was not compressed");
            }
            else
            {
                // copied as is, even if it's worse than what Optimal would give
                Assert.Equal(originalEntry.CompressedLength, entry.CompressedLength);
            }
        }

        byte[] transformed = ReadAll(result.GetEntry("textures/transformed.tga")!.Open());
        Assert.Equal([.. files[0].Item2, .. "appended"u8.ToArray()], transformed);
    }

    private string CreateZip(params (string name, byte[] data, CompressionLevel level)[] files)
    {
        string path = Path.Combine(_directory, $"{Guid.NewGuid():N}.pk3");

        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create, Encoding.UTF8))
        {
            archive.Comment = "test archive";

            foreach (var (name, data, level) in files)
            {
                var entry = archive.CreateEntry(name, level);
                entry.LastWriteTime = _timestamp;
                using var stream = entry.Open();
                stream.Write(data);
            }
        }

        return path;
    }

    private static byte[] ReadAll(Stream stream)
    {
        using (stream)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
    }

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
}
