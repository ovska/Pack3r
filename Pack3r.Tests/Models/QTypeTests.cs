namespace Pack3r.Tests.Models;

public static class QTypeTests
{
    [Theory]
    [InlineData("test", "test")]
    [InlineData("path/to/texture.jpg", "path/to/texture.jpg")]
    [InlineData(@"path\to\texture.jpg", "path/to/texture.jpg")]
    public static void QPath_Should_Normalize_Separators(string input, string expected)
    {
        Assert.Equal(expected, new QPath(input).ToString());
        Assert.Equal(expected, new QPath(input.AsMemory()).ToString());
        Assert.Equal(expected, new QPath(input.ToCharArray()).ToString());
        Assert.Equal(expected, new QPath(input)[Range.All].ToString());
    }

    [Fact]
    public static void QPath_Should_Validate_Length()
    {
        _ = new QPath(new string('a', 64));
        _ = new QPath(new char[64].AsMemory());
        Assert.Throws<InvalidDataException>(() => new QPath(new string('a', 65)));
        Assert.Throws<InvalidDataException>(() => new QPath(new char[65].AsMemory()));
    }

    [Fact]
    public static void QTypes_Should_Be_Case_Insensitive()
    {
        Assert.Equal((QPath)"aaa", (QPath)"aaa");
        Assert.Equal((QString)"aaa", (QString)"aaa");

        Assert.Equal((QPath)"aAa", (QPath)"Aaa");
        Assert.Equal((QString)"aAa", (QString)"Aaa");
    }

    [Fact]
    public static void QTypes_Should_Be_Comparable()
    {
        Assert.Equal(0, new QPath("xXx").CompareTo("xXx"));
        Assert.Equal(0, new QPath("xXx").CompareTo("xXx"));
        Assert.Equal(1, new QPath("def").CompareTo("abc"));
        Assert.Equal(1, new QString("def").CompareTo("abc"));
        Assert.Equal(-1, new QPath("asd").CompareTo("ASD"));
        Assert.Equal(-1, new QString("asd").CompareTo("ASD"));
    }
}
