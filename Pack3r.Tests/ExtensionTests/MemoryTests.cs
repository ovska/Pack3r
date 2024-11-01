using System.Collections.Generic;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Tests.ExtensionTests;

public static class MemoryTests
{
    [Theory]
    [InlineData("skyparms -", "skyparms", "-")]
    [InlineData("skyparms    -", "skyparms", "-")]
    [InlineData("skyparms\t-", "skyparms", "-")]
    [InlineData("skyparms\t\t-", "skyparms", "-")]
    [InlineData("map textures/test", "map", "textures/test")]
    [InlineData("skyparms -", "map", null)]
    [InlineData("nopicmip", "nopicmip", null)]
    [InlineData("nopicmip ", "nopicmip", null)]
    [InlineData("nopicmip\t\t", "nopicmip", null)]
    [InlineData("animmap a.tga b.tga c.tga", "animMap", "a.tga b.tga c.tga")]
    public static void Should_Match_Keyword(string input, string keyword, string? remainder)
    {
        var line = new Line("", 1, input, keepRaw: false);

        if (remainder is not null)
        {
            Assert.True(line.MatchKeyword(keyword, out var rem));
            Assert.Equal(remainder, rem.ToString());
        }
        else
        {
            Assert.False(line.MatchKeyword(keyword, out _));
        }
    }

    [Theory]
    [InlineData("test arg", "arg")]
    [InlineData(" arg", "arg")]
    public static void Should_Read_Past_Whitespace(string input, string expected)
    {
        Assert.True(input.AsMemory().TryReadPastWhitespace(out var actual));
        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("test arg", "test")]
    [InlineData("test ", "test")]
    public static void Should_Read_Up_To_Whitespace(string input, string expected)
    {
        Assert.True(input.AsMemory().TryReadUpToWhitespace(out var actual));
        Assert.Equal(expected, actual.ToString());
    }

    [Theory]
    [InlineData("test", (string[])["test"])]
    [InlineData("test arg", (string[])["test", "arg"])]
    [InlineData("test arg arg2", (string[])["test", "arg", "arg2"])]
    [InlineData("test arg ", (string[])["test", "arg"])]
    [InlineData("test \"arg\" ", (string[])["test", "arg"])]
    [InlineData("test\t\t\"arg\" ", (string[])["test", "arg"])]
    [InlineData("\t\"test\" \"arg\" ", (string[])["test", "arg"])]
    public static void Should_Split_By_Whitespace(string input, string[] expected)
    {
        List<string> list = [];

        foreach (var frame in Tokens.WhitespaceSeparatedTokens().EnumerateMatches(input))
        {
            ReadOnlyMemory<char> value = input.AsMemory(frame.Index, frame.Length).TrimQuotes().Trim();

            if (!value.IsEmpty)
            {
                list.Add(value.ToString());
            }
        }

        Assert.Equal(expected, list);
    }

    [Theory]
    [InlineData("\"classname\" \"info_player_deathmatch\"", "classname", "info_player_deathmatch")]
    public static void Should_Parse_Key_And_Value(string input, string key, string value)
    {
        var (k, v) = new Line(input, 1, input, true).ReadKeyValue();
        Assert.Equal(key, k.ToString());
        Assert.Equal(value, v.ToString());
    }

    [Theory]
    [InlineData("test.tga", true)]
    [InlineData("test.jpg", true)]
    [InlineData("test.mdc", true)]
    [InlineData("test.md3", true)]
    [InlineData("test.ase", true)]
    [InlineData("test.shader", true)]
    [InlineData("test.wav", true)]
    [InlineData("test.roq", true)]
    [InlineData("test.skin", true)]
    [InlineData("test.dat", false)]
    [InlineData("test.cfg", false)]
    [InlineData("test.menu", false)]
    [InlineData("test.weap", false)]
    public static void Should_Return_Packable_Files(string input, bool expected)
    {
        Assert.Equal(expected, Tokens.PackableFile().IsMatch(input));
    }

    [Theory, MemberData(nameof(MapscriptTokens))]
    public static void Should_Parse_Mapscript_Tokens(string input, string[] expected)
    {
        //var tokens = Tokens.MapscriptTokens().Matches(input).Select(m => m.Groups[0].Value.Trim('"')).ToArray();
        var tokens = new List<string>();

        foreach (var range in Tokens.WhitespaceSeparatedTokens().EnumerateMatches(input))
            tokens.Add(input.Substring(range.Index, range.Length).Trim('"'));

        Assert.Equal(expected, tokens);
    }

    public static IEnumerable<object[]> MapscriptTokens => (((string, string[])[])[
        ("remapshader testi testi", ["remapshader", "testi", "testi"]),
        ("remapshader \"testi\" testi", ["remapshader", "testi", "testi"]),
        ("remapshader \"testi\" \"testi\"", ["remapshader", "testi", "testi"]),
        ("\"origin\" \"-1 -5 -99\"", ["origin", "-1 -5 -99"]),
        ("\"origin\" \"-24 -72 -96\"", ["origin", "-24 -72 -96"]),
        ("\"-24 -72 -96\" \"origin\"", [ "-24 -72 -96", "origin"]),
        ("\"message\" \"hello, world\"", ["message", "hello, world"]),
    ])
    .SelectMany(x => new[] { x, (x.Item1.Replace("\" \"", "\"\t\""), x.Item2) })
    .Select(x => new object[] { x.Item1, x.Item2 });

}
