using System.Text;
using Pack3r.IO;

namespace Pack3r.Tests;

public sealed class LineReaderTests : IDisposable
{
    private readonly string _path = Path.Combine(Directory.CreateTempSubdirectory("pack3r_lines_").FullName, "test.shader");

    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_path)!, recursive: true);

    public static TheoryData<string> Contents => new()
    {
        "",
        "single line without newline",
        "unix\nline\nendings\n",
        "windows\r\nline\r\nendings\r\n",
        "old mac\rline\rendings",
        "mixed\r\n\n\r\nempty lines\n\n\nand \r\r\r\n trailing \r",
        "  // comment only\n\ttextures/foo // trailing comment\n   \n{\n\tmap $lightmap\n}\n",
        "non-ascii äö bytes\n",
        "﻿bom at start\nsecond\n",
    };

    [Theory]
    [MemberData(nameof(Contents))]
    public void Whole_File_And_Streaming_Should_Read_The_Same_Lines(string content)
    {
        File.WriteAllText(_path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var whole = new FSLineReader();
        var streaming = new FSLineReader { WholeFileThreshold = -1 };

        Assert.Equal(Describe(streaming.ReadLines(_path, default)), Describe(whole.ReadLines(_path, default)));
        Assert.Equal(Describe(streaming.ReadRawLines(_path)), Describe(whole.ReadRawLines(_path)));
    }

    [Fact]
    public void Should_Read_Past_Stale_Length()
    {
        // FileAsset's length comes from when the directory was listed; the file may have grown since
        File.WriteAllText(_path, "a\nb\n");
        var info = new FileInfo(_path);
        _ = info.Length;
        File.WriteAllText(_path, string.Concat(Enumerable.Range(0, 10_000).Select(i => $"line {i}\n")));

        var lines = new FSLineReader().ReadLines(new MockFileAsset(info), default).ToList();

        Assert.Equal(10_000, lines.Count);
        Assert.Equal("line 9999", lines[^1].Value.ToString());
    }

    private static List<string> Describe(IEnumerable<Line> lines)
    {
        return [.. lines.Select(l => $"{l.Index}|{l.Raw}|{l.Value}|{l.FirstChar}")];
    }

    private sealed class MockFileAsset(FileInfo file) : Pack3r.Models.IAsset
    {
        public string Name => file.Name;
        public string FullPath => file.FullName;
        public AssetSource Source => null!;
        public long Length => file.Length;
        public Stream OpenRead() => File.OpenRead(file.FullName);
        public System.Buffers.IMemoryOwner<byte> GetBytes(int? sizeHint, CancellationToken cancellationToken) => throw new NotSupportedException();
        public void AddTo(Pk3Writer writer, string? archivePath = null, Action<Stream, Stream>? transform = null) => throw new NotSupportedException();
    }
}
