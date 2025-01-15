using System.CommandLine;
using System.Diagnostics.CodeAnalysis;
using DotMake.CommandLine;
using Pack3r.Logging;
using Pack3r.Extensions;

namespace Pack3r.Console;

[CliCommand(
    Description = "Pack3r, tool to create release-ready pk3s from NetRadiant maps",
    NameCasingConvention = CliNameCasingConvention.LowerCase,
    ShortFormAutoGenerate = false)]
public class RootCommand
{
    [CliArgument(
        HelpName = "map path",
        Description = "Path to the source .map file",
        Arity = CliArgumentArity.ExactlyOne,
        ValidationRules = CliValidationRules.ExistingFile)]
    public FileInfo Map { get; set; } = null!;

    [CliOption(
        Description = "Path to destination pk3/zip (or directory where it will be written), defaults to etmain/mapname.pk3",
        Required = false,
        ValidationRules = CliValidationRules.LegalPath,
        Aliases = ["-o"])]
    public FileSystemInfo? Output { get; set; }

    [CliOption(
        Description = "Discover packed files and estimate file size without creating a pk3",
        Aliases = ["-d"])]
    public bool DryRun { get; set; }

    [CliOption(
        Description = "Name of the map after packing (renames bsp, lightmaps, mapscript etc.)",
        Required = false,
        ValidationRules = CliValidationRules.LegalFileName,
        Aliases = ["-r"])]
    public string? Rename { get; set; }

    [CliOption(
        Description = "Log severity threshold",
        Arity = CliArgumentArity.ZeroOrOne,
        Aliases = ["-v"])]
    public LogLevel Verbosity { get; set; } = LogLevel.Info;

    [CliOption(
        Description = "Complete packing even if some files are missing",
        Aliases = ["-l"])]
    public bool Loose { get; set; }

    [CliOption(
        Description = "Pack only source files (.map, editorimages, misc_models) without packing BSP & lightmaps",
        Aliases = ["-s"])]
    public bool Source { get; set; }

    [CliOption(
        Description = "Print shader resolution details (Debug verbosity needed)",
        Aliases = ["-sd"])]
    public bool ShaderDebug { get; set; }

    [CliOption(
        Description = "Print asset resolution details (Info verbosity needed)",
        Aliases = ["-rd"])]
    public bool ReferenceDebug { get; set; }

    [CliOption(
        Description = "Overwrite existing files in the output path with impunity",
        Aliases = ["-f", "--overwrite"])]
    public bool Force { get; set; }

    [CliOption(
        Description = "Include pk3 files and pk3dirs in etmain when indexing files",
        Aliases = ["-p", "--pk3"])]
    public bool IncludePk3 { get; set; }

    [CliOption(
        Description = "Don't scan pk3/pk3dirs for assets",
        Arity = CliArgumentArity.ZeroOrMore,
        ValidationRules = CliValidationRules.LegalPath,
        AllowMultipleArgumentsPerToken = true,
        Aliases = ["-ns"])]
    public List<string> NoScan { get; set; } = ["pak1.pk3", "pak2.pk3", "mp_bin.pk3"];

    [CliOption(
        Description = "Scan some pk3s/pk3dirs but don't pack their contants",
        Arity = CliArgumentArity.ZeroOrMore,
        ValidationRules = CliValidationRules.LegalPath,
        AllowMultipleArgumentsPerToken = true,
        Aliases = ["-np"])]
    public List<string> NoPack { get; set; } =
    [
        "pak0.pk3",
        "pak0.pk3dir",
        "lights.pk3",
        "sd-mapobjects.pk3",
        "common.pk3",
        "astro-skies.pk3",
    ];

    [CliOption(
        Description = "Adds all pk3s in mod directories to exclude-list",
        Arity = CliArgumentArity.ZeroOrMore,
        ValidationRules = CliValidationRules.LegalPath,
        AllowMultipleArgumentsPerToken = true,
        Aliases = ["-m"])]
    public List<string> Mods { get; set; } = [];

    public Task<int> RunAsync()
    {
        return Program.Execute(new PackOptions
        {
            MapFile = ResolveMap(),
            Pk3File = ResolvePk3(),
            Overwrite = Force,
            UnpackedSources = NoPack,
            UnscannedSources = NoScan,
            ModFolders = Mods,
            DryRun = DryRun,
            OnlySource = Source,
            LoadPk3s = IncludePk3,
            LogLevel = Verbosity,
            Rename = Rename,
            RequireAllAssets = !Loose,
            ShaderDebug = ShaderDebug,
            ReferenceDebug = ReferenceDebug,
        });
    }

    private FileInfo ResolveMap()
    {
        var ext = Map.FullName.GetExtension();

        if (!ext.EqualsF(".map") && !ext.EqualsF(".reg"))
        {
            Error("Invalid map file extension, expected .map or .reg");
        }

        var bspPath = Path.ChangeExtension(Map.FullName, "bsp");

        if (!File.Exists(bspPath))
        {
            Error($"BSP for the map file not found: '{bspPath}'");
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
                    Error("Could not find output pk3 location");
                    return null!; // shut up compiler
                }

                pk3Location = etmain;
            }

            string outName = Path.ChangeExtension(pk3Name, Source ? ".zip" : ".pk3");
            pk3 = new FileInfo(Path.Combine(pk3Location.FullName, outName));
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
