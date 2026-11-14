using System.IO.Hashing;
using Pack3r.IO;

namespace Pack3r.Tests;

public class Crc32CombineTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 10)]
    [InlineData(10, 0)]
    [InlineData(1, 1)]
    [InlineData(1000, 1)]
    [InlineData(2 * 1024 * 1024, 2 * 1024 * 1024)]
    [InlineData(2 * 1024 * 1024, 12345)]
    [InlineData(7, 3 * 1024 * 1024 + 17)]
    public void Should_Match_Crc_Of_Concatenated_Data(int lengthA, int lengthB)
    {
        var random = new Random(lengthA * 31 + lengthB);
        var data = new byte[lengthA + lengthB];
        random.NextBytes(data);

        uint crcA = Crc32.HashToUInt32(data.AsSpan(0, lengthA));
        uint crcB = Crc32.HashToUInt32(data.AsSpan(lengthA));

        Assert.Equal(Crc32.HashToUInt32(data), Crc32Combine.Combine(crcA, crcB, lengthB));
    }

    [Fact]
    public void Should_Combine_Many_Chunks_In_Order()
    {
        var data = new byte[10 * 1024 * 1024 + 999];
        new Random(5).NextBytes(data);

        const int chunk = 1024 * 1024;
        uint crc = 0;

        for (int offset = 0; offset < data.Length; offset += chunk)
        {
            var piece = data.AsSpan(offset, Math.Min(chunk, data.Length - offset));
            crc = offset == 0 ? Crc32.HashToUInt32(piece) : Crc32Combine.Combine(crc, Crc32.HashToUInt32(piece), piece.Length);
        }

        Assert.Equal(Crc32.HashToUInt32(data), crc);
    }
}
