using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using DotMake.CommandLine;
using Pack3r.Logging;
using Pack3r.Extensions;

namespace Pack3r.Console;

[CliCommand(
    Description = "Pack3r, tool to create release-ready pk3s from NetRadiant maps",
    NameCasingConvention = CliNameCasingConvention.LowerCase)]
public class RootCommand
{
    [CliArgument(
        Description = "Path to the .map file",
        Arity = CliArgumentArity.ExactlyOne,
        ValidationRules = CliValidationRules.ExistingFile)]
    public FileInfo Map { get; set; } = null!;

    [CliOption(
        Description = "Path of destination directory or pk3 name, defaults to etmain",
        Required = false,
        ValidationRules = CliValidationRules.LegalPath)]
    public FileSystemInfo? Output { get; set; }

    [CliOption(Description = "Print files that would be packed, without creating a pk3")]
    public bool DryRun { get; set; }

    [CliOption(
        Description = "Name of the map release, changes name of bsp, mapscript, etc.",
        Required = false,
        ValidationRules = CliValidationRules.LegalFileName)]
    public string? Rename { get; set; }

    [CliOption(
        Description = "Log severity threshold",
        Arity = CliArgumentArity.ZeroOrOne)]
    public LogLevel Verbosity { get; set; } = LogLevel.Info;

    [CliOption(Description = "Complete packing even if some files are missing")]
    public bool Loose { get; set; }

    [CliOption(Description = "Pack source files such as .map, editorimages, misc_models")]
    public bool Source { get; set; }

    [CliOption(
        Description = "Only read shaders present in shaderlist.txt",
        Aliases = ["-sl"])]
    public bool Shaderlist { get; set; }

    [CliOption(Description = "Overwrite existing files in the output path with impunity")]
    public bool Force { get; set; }

    [CliOption(Description = "Include pk3 files and pk3dirs in etmain when indexing files")]
    public bool IncludePk3 { get; set; }

    [CliOption(
        Description = "Ignore some pk3 files or pk3dir directories",
        Arity = CliArgumentArity.ZeroOrMore,
        ValidationRules = CliValidationRules.LegalPath)]
    public List<string> Ignore { get; set; } = ["pak1.pk3", "pak2.pk3", "mp_bin.pk3"];

    [CliOption(
        Description = "Include pk3 files and pk3dirs in etmain in index, but never pack their contents",
        Arity = CliArgumentArity.ZeroOrMore,
        ValidationRules = CliValidationRules.LegalPath,
        Hidden = true)]
    public List<string> Exclude { get; set; } = ["pak0.pk3"];

    public Task<int> RunAsync()
    {
        return Program.Execute(new PackOptions
        {
            MapFile = ResolveMap(),
            Pk3File = ResolvePk3(),
            Overwrite = Force,
            ExcludeSources = Exclude,
            IgnoreSources = Ignore,
            DryRun = DryRun,
            IncludeSource = Source,
            LoadPk3s = IncludePk3,
            LogLevel = Verbosity,
            Rename = Rename,
            RequireAllAssets = !Loose,
            UseShaderlist = Shaderlist,
        });
    }

    private FileInfo ResolveMap()
    {
        var bspPath = Path.ChangeExtension(Map.FullName, "bsp");

        if (!File.Exists(bspPath))
        {
            Error($"Compiled bsp of the .map not found in '{bspPath}'");
        }

        return Map;
    }

    private FileInfo? ResolvePk3()
    {
        if (DryRun)
            return null;

        FileInfo pk3;

        if (Output is not FileInfo fi)
        {
            var pk3Name = Rename ?? Path.GetFileNameWithoutExtension(Map.FullName);
            DirectoryInfo pk3Location;

            if (Output is DirectoryInfo di)
            {
                pk3Location = di;
            }
            else
            {
                if (Map.Directory?.Parent is not { Exists: true } etmain)
                {
                    Error("Could not determine output pk3 location");
                    return null!; // shut up compiler
                }

                pk3Location = etmain;
            }

            pk3 = new FileInfo(Path.Combine(pk3Location.FullName, Path.ChangeExtension(pk3Name, ".pk3")));
        }
        else
        {
            pk3 = fi;
        }

        var extension = pk3.FullName.GetExtension();

        if (!extension.EqualsF(".pk3") && !extension.EqualsF(".zip"))
        {
            Error($"Invalid output path (not pk3 or zip): '{pk3.FullName}'");
        }

        if (!Force && pk3.Exists)
        {
            Error($"Output file already exists, use the force-option to overwrite it: '{pk3.FullName}'");
        }

        return pk3;
    }

    [DoesNotReturn]
    private static void Error(string message)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.WriteLine(message);
        Environment.Exit(1);
    }
}
