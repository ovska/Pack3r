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
}
