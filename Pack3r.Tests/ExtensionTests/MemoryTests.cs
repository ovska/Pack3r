using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r.Tests.ExtensionTests;

public static class MemoryTests
{
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
    public static void Should_Split_By_Whitespace(string input, string[] expected)
    {
        Assert.Equal(expected, input.AsMemory().Split(' ').Select(r => input.AsMemory(r).ToString()));
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
    [InlineData("scripts/common.shader", true)]
    [InlineData("scripts/medieval_soc.shader", true)]
    [InlineData("scripts/shaderlist.txt", false)]
    [InlineData("wrongfolder/common.shader", false)]
    [InlineData("scripts\\common.shader", true)]
    [InlineData("scripts\\medieval_soc.shader", true)]
    [InlineData("scripts\\shaderlist.txt", false)]
    [InlineData("wrongfolder\\common.shader", false)]
    public static void Should_Check_For_Shader_File(string input, bool expected)
    {
        Assert.Equal(expected, Tokens.ShaderPath().IsMatch(input));
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
}
