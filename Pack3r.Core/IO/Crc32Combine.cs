namespace Pack3r.IO;

/// <summary>
/// Combines the CRC-32 of two consecutive pieces of data into the CRC-32 of both, without the data.
/// Same algorithm as zlib's <c>crc32_combine</c>: multiplication by x^(8n) modulo the CRC polynomial.
/// </summary>
public static class Crc32Combine
{
    // reflected CRC-32 polynomial (IEEE 802.3), as used by zip
    private const uint Polynomial = 0xEDB88320;

    // X2N[k] = x^(2^k) modulo the polynomial
    private static readonly uint[] X2N = CreateTable();

    /// <summary>
    /// Returns the CRC-32 of A followed by B, given the CRC-32 of A, the CRC-32 of B and the length of B.
    /// </summary>
    public static uint Combine(uint crcA, uint crcB, long lengthB)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lengthB);
        return MultiplyModP(PowerOfX(lengthB, 3), crcA) ^ crcB;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[32];
        uint p = 1u << 30; // x^1

        table[0] = p;

        for (int n = 1; n < table.Length; n++)
        {
            table[n] = p = MultiplyModP(p, p);
        }

        return table;
    }

    // a * b modulo the polynomial, with the bits in reflected order (x^0 is the top bit)
    private static uint MultiplyModP(uint a, uint b)
    {
        uint m = 1u << 31;
        uint p = 0;

        while (true)
        {
            if ((a & m) != 0)
            {
                p ^= b;

                if ((a & (m - 1)) == 0)
                    break;
            }

            m >>= 1;
            b = (b & 1) != 0 ? (b >> 1) ^ Polynomial : b >> 1;
        }

        return p;
    }

    // x^(n * 2^k) modulo the polynomial; with k = 3, x^(8n) for n bytes
    private static uint PowerOfX(long n, int k)
    {
        uint p = 1u << 31; // x^0 == 1

        while (n != 0)
        {
            if ((n & 1) != 0)
                p = MultiplyModP(X2N[k & 31], p);

            n >>= 1;
            k++;
        }

        return p;
    }
}
