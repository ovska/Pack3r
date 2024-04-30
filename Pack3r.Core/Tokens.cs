﻿using System.Buffers;
using System.Text.RegularExpressions;

namespace Pack3r;

public static partial class Tokens
{
    [GeneratedRegex("^(set|create)$", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex UnsupportedMapscript();

    [GeneratedRegex("""^"([^"]+)" "([^"]+)"$""", RegexOptions.Singleline, 1000)]
    public static partial Regex KeyValuePair();

    [GeneratedRegex("""^scripts[/\\][^\.]+.shader$""", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex ShaderPath();

    [GeneratedRegex("""\.(tga|jpg|mdc|md3|ase|shader|wav|roq|skin)$""", RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex PackableFile();

    [GeneratedRegex("""
        [^\s"]+|"([^"]*)"
        """, RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex WhitespaceSeparatedTokens();

    [GeneratedRegex("""
        [\s]?map[\s]+"
        """, RegexOptions.IgnoreCase | RegexOptions.Singleline, 1000)]
    public static partial Regex ArenaMapName();

    public static readonly SearchValues<char> Braces = SearchValues.Create("{}");
    public static readonly SearchValues<char> SpaceOrTab = SearchValues.Create(" \t");
}
