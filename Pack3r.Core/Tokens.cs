using System.Buffers;
using System.Text.RegularExpressions;

namespace Pack3r;

public static partial class Tokens
{
    [GeneratedRegex("^(set|create)$", RegexOptions.IgnoreCase, 1000)]
    public static partial Regex UnsupportedMapscript();

    [GeneratedRegex("""^"([^"]+)" "([^"]+)"$""", RegexOptions.None, 1000)]
    public static partial Regex KeyValuePair();

    public static readonly SearchValues<char> Braces = SearchValues.Create("{}");
}
