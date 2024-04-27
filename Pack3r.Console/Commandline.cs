using System.CommandLine;
using System.CommandLine.Parsing;
using System.Diagnostics;
using Pack3r.Logging;

namespace Pack3r.Console;

internal static class Commandline
{
    public static Task Run(
        string[] args,
        Func<PackOptions, Task<int>> task)
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

        var etjumpdirOption = new Option<string?>(
            ["--etjumpdir"],
            () => null,
            "Override for etjump directory name")
        {
            Arity = ArgumentArity.ZeroOrOne,
            IsHidden = true,
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
            etjumpdirOption,
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
                ETJumpDir = context.ParseResult.GetValueForOption(etjumpdirOption),
            };

            Debug.Assert(options.Pk3File != null || options.DryRun, "Pk3 is required on non-dry runs");

            context.ExitCode = await task(options);
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

            var extension = Path.GetExtension(file.FullName.AsSpan());

            if (extension.IsEmpty)
            {
                if (Directory.Exists(file.FullName))
                {
                    result.ErrorMessage = $"Map path points to a directory: {file.FullName}";
                    return;
                }

                file = new FileInfo(Path.ChangeExtension(file.FullName, ".map"));
            }
            else if (!extension.Equals(".map", StringComparison.OrdinalIgnoreCase))
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

            var extension = Path.GetExtension(file.FullName.AsSpan());

            if (extension.IsEmpty)
            {
                if (Directory.Exists(file.FullName))
                {
                    result.ErrorMessage = $"Output path points to a directory: {file.FullName}";
                    return;
                }

                file = new FileInfo(Path.ChangeExtension(file.FullName, ".pk3"));
            }
            else if (!extension.Equals(".pk3", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "Output file is not a .pk3 or .zip-file";
                return;
            }

            if (!result.GetValueForOption(overwriteOption) && file.Exists)
            {
                result.ErrorMessage = $"Output file already exists, use the overwrite-option to overwrite it: {file.FullName}";
            }
        }
    }
}
