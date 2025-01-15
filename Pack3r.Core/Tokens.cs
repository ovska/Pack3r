using System.Buffers;
using System.Text.RegularExpressions;

namespace Pack3r;

public static partial class Tokens
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;
    private const int Timeout = 1_000;

    /// <summary>
    /// Mapscript keywords that aren't parsed for resources referenced.
    /// </summary>
    [GeneratedRegex("^(set|create)$", Options, Timeout)]
    public static partial Regex UnsupportedMapscript();

    /// <summary>
    /// Matches filetypes that should be packaged.
    /// </summary>
    [GeneratedRegex("""\.(tga|jp[e]?g|md3|mdc|mdm|ase|obj|fbx|shader|wav|roq|skin)$""", Options, Timeout)]
    public static partial Regex PackableFile();

    public static bool IncludeAsset(ReadOnlySpan<char> fullPath)
        => !fullPath.Contains("_pack3rignore_", StringComparison.OrdinalIgnoreCase) &&
            PackableFile().IsMatch(fullPath);

    /// <summary>
    /// Matches quoted/notquoted tokens separated by whitespace.
    /// </summary>
    [GeneratedRegex("""
        [^\s"]+|"([^"]*)"
        """, Options, Timeout)]
    public static partial Regex WhitespaceSeparatedTokens();

    public static readonly SearchValues<char> Braces = SearchValues.Create("{}");
    public static readonly SearchValues<char> SpaceOrTab = SearchValues.Create(" \t");
}
