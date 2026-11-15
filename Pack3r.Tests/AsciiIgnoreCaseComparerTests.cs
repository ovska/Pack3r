using System.Text;
using Pack3r.Extensions;

namespace Pack3r.Tests;

public class AsciiIgnoreCaseComparerTests
{
    private static readonly AsciiIgnoreCaseComparer Comparer = AsciiIgnoreCaseComparer.Instance;

    [Theory]
    [InlineData("common/caulk", "common/caulk")]
    [InlineData("common/caulk", "COMMON/Caulk")]
    [InlineData("", "")]
    [InlineData("a_b-c.1[2]", "A_B-C.1[2]")]
    public void Should_Match_Bytes_To_String(string bytes, string value)
    {
        byte[] alternate = Encoding.ASCII.GetBytes(bytes);

        Assert.True(Comparer.Equals(alternate, new QString(value)));
        Assert.True(Comparer.Equals(new QString(bytes), new QString(value)));
        Assert.Equal(Comparer.GetHashCode(new QString(value)), Comparer.GetHashCode(alternate));
        Assert.Equal(Comparer.GetHashCode(new QString(bytes)), Comparer.GetHashCode(new QString(value)));
    }

    [Theory]
    [InlineData("common/caulk", "common/caul")]
    [InlineData("common/caulk", "common/caulx")]
    [InlineData("[", "{")] // differ only by the 0x20 bit, but aren't letters
    [InlineData("@", "`")]
    public void Should_Not_Match_Different_Strings(string bytes, string value)
    {
        byte[] alternate = Encoding.ASCII.GetBytes(bytes);

        Assert.False(Comparer.Equals(alternate, new QString(value)));
        Assert.False(Comparer.Equals(new QString(bytes), new QString(value)));
    }

    [Fact]
    public void Should_Match_Non_Ascii_Bytes_To_Their_Decoded_String()
    {
        byte[] alternate = [(byte)'T', 0xE4, (byte)'x', 0xFF];
        QString decoded = ((ReadOnlySpan<byte>)alternate).ToQString();

        Assert.Equal("T?x?", decoded.ToString());
        Assert.True(Comparer.Equals(alternate, decoded));
        Assert.True(Comparer.Equals(alternate, new QString("t?X?")));
        Assert.False(Comparer.Equals(alternate, new QString("t?X!")));
        Assert.Equal(Comparer.GetHashCode(decoded), Comparer.GetHashCode(alternate));
    }

    [Fact]
    public void Should_Look_Up_By_Bytes()
    {
        HashSet<QString> set = new(Comparer) { "textures/Common/Caulk"[9..].AsMemory() };
        var lookup = set.GetAlternateLookup<ReadOnlySpan<byte>>();

        Assert.True(lookup.Contains("common/caulk"u8));
        Assert.True(lookup.Contains("COMMON/CAULK"u8));
        Assert.False(lookup.Contains("common/nodraw"u8));

        Assert.True(lookup.Add("common/nodraw"u8));
        Assert.False(lookup.Add("Common/NoDraw"u8));
        Assert.Equal(2, set.Count);
    }
}
