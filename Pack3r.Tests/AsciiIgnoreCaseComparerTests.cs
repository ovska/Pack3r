using System.Text;

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
    public void Should_Fold_Only_Letters_At_Every_Position()
    {
        // every ASCII character, at the start and end of values long enough to cover whole and partial words
        for (int length = 1; length <= 17; length++)
        {
            for (int c = 0; c < 128; c++)
            {
                foreach (int position in (int[])[0, length - 1])
                {
                    char[] chars = new string('x', length).ToCharArray();
                    chars[position] = (char)c;
                    string value = new(chars);
                    byte[] bytes = Encoding.ASCII.GetBytes(value);

                    Assert.Equal(Comparer.GetHashCode(new QString(value.ToLowerInvariant())), Comparer.GetHashCode(bytes));
                    Assert.Equal(Comparer.GetHashCode(new QString(value.ToUpperInvariant())), Comparer.GetHashCode(bytes));
                    Assert.True(Comparer.Equals(bytes, new QString(value.ToUpperInvariant())));

                    // flipping the case bit of anything but a letter gives a different value
                    chars[position] = (char)(c ^ 0x20);

                    if (!char.IsAsciiLetter((char)c) && chars[position] < 128)
                        Assert.False(Comparer.Equals(bytes, new QString(new string(chars))));
                }
            }
        }
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
