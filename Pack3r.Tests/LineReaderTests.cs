using System.Text;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Tests;

public class LineReaderTests
{
    private static readonly string[] Contents =
    [
        "",
        "single line without newline",
        "unix\nline\nendings\n",
        "windows\r\nline\r\nendings\r\n",
        "old mac\rline\rendings",
        "mixed\r\n\n\r\nempty lines\n\n\nand \r\r\r\n trailing \r",
        "  // comment only\n\ttextures/foo // trailing comment\n   \n{\n\tmap $lightmap\n}\n",
        "non-ascii äö bytes\n",
        "﻿bom at start\nsecond\n",
        "a very long line " + new string('x', 200) + "\r\nshort\r\n" + new string('y', 150),
    ];

    public static TheoryData<string, int> ContentsAndBufferSizes()
    {
        var data = new TheoryData<string, int>();

        foreach (var content in Contents)
            foreach (int size in new[] { 1, 2, 3, 7, 64, LineEnumerator.DefaultBufferSize })
                data.Add(content, size);

        return data;
    }

    /// <summary>
    /// Every line break position, \r\n split across reads, and lines longer than the buffer: the result must be the
    /// same as reading the file with an ASCII StreamReader, as before.
    /// </summary>
    [Theory]
    [MemberData(nameof(ContentsAndBufferSizes))]
    public void Should_Read_Same_Lines_As_StreamReader(string content, int bufferSize)
    {
        byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content);

        foreach (bool keepRaw in new[] { false, true })
        {
            List<string> actual = [];

            foreach (var line in LineEnumerable.FromStream(new MemoryStream(bytes), "test", keepRaw, default, bufferSize))
                actual.Add(Describe(line.Index, line.RawSpan, line.Value, line.FirstChar));

            Assert.Equal(Expected(bytes, keepRaw), actual);
        }
    }

    [Fact]
    public void Should_Keep_Line_After_Buffer_Is_Reused()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 1000).Select(i => $"line {i} // comment\n")));
        List<Line> kept = [];

        foreach (var line in LineEnumerable.FromStream(new MemoryStream(bytes), "test", keepRaw: false, default, bufferSize: 16))
        {
            if (line.Index % 100 == 0)
                kept.Add(line.ToLine());
        }

        Assert.Equal(10, kept.Count);

        for (int i = 0; i < kept.Count; i++)
        {
            int number = (i + 1) * 100 - 1;
            Assert.Equal((i + 1) * 100, kept[i].Index);
            Assert.Equal($"line {number}", kept[i].Value.ToAsciiString());
            Assert.Equal($"line {number} // comment", kept[i].Raw);
        }
    }

    [Fact]
    public void Should_Stop_When_Canceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            foreach (var _ in LineEnumerable.FromStream(new MemoryStream("a\nb\n"u8.ToArray()), "test", false, cts.Token))
            {
            }
        });
    }

    // the old reader: StreamReader with ASCII, skipping empty lines, comments and whitespace removed unless raw
    private static List<string> Expected(byte[] bytes, bool keepRaw)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.ASCII, detectEncodingFromByteOrderMarks: false);
        List<string> lines = [];
        string? text;
        int index = 0;

        while ((text = reader.ReadLine()) is not null)
        {
            index++;

            if (text.Length == 0)
                continue;

            string value = keepRaw ? text : (text.Contains("//") ? text[..text.IndexOf("//")] : text).Trim();

            if (value.Length > 0)
                lines.Add($"{index}|{text}|{value}|{value[0]}");
        }

        return lines;
    }

    private static string Describe(int index, ReadOnlySpan<byte> raw, ReadOnlySpan<byte> value, byte firstChar)
        => $"{index}|{raw.ToAsciiString()}|{value.ToAsciiString()}|{(firstChar > 127 ? '?' : (char)firstChar)}";
}
