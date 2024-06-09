using System.Buffers;
using System.Text.RegularExpressions;

namespace Pack3r;

public static partial class Tokens
{
    /// <summary>
    /// Mapscript keywords that aren't parsed for resources referenced.
    /// </summary>
    [GeneratedRegex("^(set|create)$", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex UnsupportedMapscript();

    /// <summary>
    /// Matches scripts/*something*.shader
    /// </summary>
    [GeneratedRegex("""^scripts[/\\][^\.]+.shader$""", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex ShaderPath();

    /// <summary>
    /// Matches filetypes that should be packaged.
    /// </summary>
    [GeneratedRegex("""\.(tga|jp[e]?g|md3|mdc|ase|obj|shader|wav|roq|skin)$""", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex PackableFile();

    /// <summary>
    /// Matches quoted/notquoted tokens separated by whitespace.
    /// </summary>
    [GeneratedRegex("""
        [^\s"]+|"([^"]*)"
        """, RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex WhitespaceSeparatedTokens();

    public static readonly SearchValues<char> Braces = SearchValues.Create("{}");
    public static readonly SearchValues<char> SpaceOrTab = SearchValues.Create(" \t");
}
