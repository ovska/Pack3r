using System.CommandLine;

namespace Pack3r.Console;

internal static class Commandline
{
    public static Task Run(
        string[] args,
        Func<PackOptions, Task> task)
    {
        var allowpartial = new Option<bool>(
            ["--loose", "--allowpartial"],
            () => false,
            "Pack the pk3 even if some assets are missing")
        {
            Arity = new ArgumentArity(0, 1),
        };

        var includeSource = new Option<bool>(
            ["--src", "--includesource"],
            () => false,
            "Include source (.map, editorimages etc.) in pk3")
        {
            Arity = new ArgumentArity(0, 1),
        };

        var shaderlistOnly = new Option<bool>(
            ["--sl", "--shaderlist"],
            () => false,
            "Only consider shaders included in shaderlist.txt")
        {
            Arity = new ArgumentArity(0, 1),
        };

        var overwrite = new Option<bool>(
            ["--force", "--overwrite"],
            () => false,
            "Overwrites an existing output pk3 file if one exists")
        {
            Arity = new ArgumentArity(0, 1),
        };

        var loglevel = new Option<LogLevel>(
            ["--log", "--loglevel"],
            () => LogLevel.Debug,
            "Output log level")
        {
            Arity = new ArgumentArity(1, 1),
        };

        var rootCommand = new RootCommand("Pack3r, tool to create release-ready pk3s from NetRadiant maps");
        rootCommand.AddOption(allowpartial);
        rootCommand.AddOption(includeSource);
        rootCommand.AddOption(shaderlistOnly);
        rootCommand.AddOption(overwrite);
        rootCommand.AddOption(loglevel);

        rootCommand.SetHandler(
            (
                allowpartial,
                includeSource,
                shaderlistOnly,
                overwrite,
                loglevel) =>
            {
                var options = new PackOptions
                {
                    RequireAllAssets = !allowpartial,
                    DevFiles = includeSource,
                    ShaderlistOnly = shaderlistOnly,
                    Overwrite = overwrite,
                    LogLevel = loglevel,
                };

                return task(options);
            },
            allowpartial,
            includeSource,
            shaderlistOnly,
            overwrite,
            loglevel);

        return rootCommand.InvokeAsync(args);
    }
}
