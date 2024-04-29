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
    public List<string> Ignore { get; set; } = [];

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

#if false
internal static class Commandline
{
    public static Task Run(
        string[] args,
        Func<PackOptions, CancellationToken, Task<int>> task)
    {
        var mapArgument = new Argument<FileInfo?>(
           name: "map",
           getDefaultValue: () => null,
           description: ".map file to create the pk3 from (NetRadiant format)");

        var pk3Option = new Option<FileInfo?>(
           ["--pk3"],
           getDefaultValue: () => null,
           description: "Destination to write the pk3 to, defaults to etmain (ignored on dry runs)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var dryrunOption = new Option<bool>(
            ["-d", "--dry-run"],
            () => false,
            "Print files that would be packed without creating the pk3")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var looseOption = new Option<bool>(
            ["--loose", "--allowpartial"],
            () => false,
            "Pack the pk3 even if some assets are missing (ignored on dry runs)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var includeSourceOption = new Option<bool>(
            ["--src", "--includesource"],
            () => false,
            "Include source (.map, editorimages etc.) in pk3")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var shaderlistOption = new Option<bool>(
            ["--sl", "--shaderlist"],
            () => false,
            "Only consider shaders included in shaderlist.txt")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var overwriteOption = new Option<bool>(
            ["--force", "--overwrite"],
            () => false,
            "Overwrites an existing output pk3 file if one exists")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        var loglevelOption = new Option<LogLevel?>(
            ["-v", "--verbosity"],
            () => LogLevel.Info,
            "Output log level, use without parameter to view all output")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        mapArgument.AddValidator(ValidateMapPath);
        pk3Option.AddValidator(ValidatePk3Path);

        var rootCommand = new RootCommand("Pack3r, tool to create release-ready pk3s from NetRadiant maps")
        {
            mapArgument,
            pk3Option,
            dryrunOption,
            looseOption,
            includeSourceOption,
            shaderlistOption,
            overwriteOption,
            loglevelOption,
        };

        rootCommand.SetHandler(async context =>
        {
            var options = new PackOptions
            {
                MapFile = context.ParseResult.GetValueForArgument(mapArgument)!,
                Pk3File = context.ParseResult.GetValueForOption(pk3Option),
                DryRun = context.ParseResult.GetValueForOption(dryrunOption),
                RequireAllAssets = !context.ParseResult.GetValueForOption(looseOption),
                DevFiles = context.ParseResult.GetValueForOption(includeSourceOption),
                ShaderlistOnly = context.ParseResult.GetValueForOption(shaderlistOption),
                Overwrite = context.ParseResult.GetValueForOption(overwriteOption),
                LogLevel = context.ParseResult.GetValueForOption(loglevelOption) ?? LogLevel.Debug,
            };

            Debug.Assert(options.Pk3File != null || options.DryRun, "Pk3 is required on non-dry runs");

            context.ExitCode = await task(options, context.GetCancellationToken());
        });

        return rootCommand.InvokeAsync(args);

        void ValidateMapPath(ArgumentResult result)
        {
            var file = result.GetValueForArgument(mapArgument);

            if (file is null)
            {
                result.ErrorMessage = "No .map file argument provided";
                return;
            }

            ReadOnlySpan<char> extension = file.FullName.GetExtension();

            if (extension.IsEmpty)
            {
                if (Directory.Exists(file.FullName))
                {
                    result.ErrorMessage = $"Map path points to a directory: {file.FullName}";
                    return;
                }

                file = new FileInfo(Path.ChangeExtension(file.FullName, ".map"));
            }
            else if (!extension.EqualsF(".map"))
            {
                result.ErrorMessage = "File is not a .map-file";
                return;
            }

            if (!file.Exists)
            {
                result.ErrorMessage = $"File does not exist: {file.FullName}";
                return;
            }

            var bspPath = Path.ChangeExtension(file.FullName, "bsp");

            if (!File.Exists(bspPath))
            {
                result.ErrorMessage = $"Compiled bsp-file with the .map name not found: {bspPath}";
            }
        }

        void ValidatePk3Path(OptionResult result)
        {
            // ignored
            if (result.GetValueForOption(dryrunOption))
                return;

            var file = result.GetValueForOption(pk3Option);

            if (file is null)
            {
                if (result.GetValueForArgument(mapArgument) is not
                    { Directory.Parent: { Exists: true } etmain } mapInfo)
                {
                    result.ErrorMessage = "Could not determine output pk3 location";
                    return;
                }

                var pk3Name = Path.GetFileNameWithoutExtension(mapInfo.FullName);
                file = new FileInfo(Path.Combine(etmain.FullName, Path.ChangeExtension(pk3Name, ".pk3")));
            }

            var extension = file.FullName.GetExtension();

            if (extension.IsEmpty)
            {
                if (Directory.Exists(file.FullName))
                {
                    result.ErrorMessage = $"Output path points to a directory: {file.FullName}";
                    return;
                }

                file = new FileInfo(Path.ChangeExtension(file.FullName, ".pk3"));
            }
            else if (!extension.EqualsF(".pk3") && !extension.EqualsF(".zip"))
            {
                result.ErrorMessage = "Output file is not a .pk3 or .zip-file";
                return;
            }

            if (!result.GetValueForOption(overwriteOption) && file.Exists)
            {
                result.ErrorMessage = $"Output file already exists, use the overwrite-option to overwrite it: {file.FullName}";
            }

            // How to set pk3 here?
            //result.
        }
    }
}
#endif