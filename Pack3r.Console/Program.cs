using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using DotMake.CommandLine;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Progress;
using Pack3r.Services;
using Pure.DI;

namespace Pack3r.Console;

public class Program
{
    internal static IConfiguration SetupDI()
    {
        return DI.Setup("Composition")
            .DefaultLifetime(Lifetime.Scoped)
            .Bind<IResourceParser>(1).To<MapscriptParser>()
            .Bind<IResourceParser>(2).To<SoundscriptParser>()
            .Bind<IResourceParser>(3).To<SpeakerScriptParser>()
            .Bind<IShaderParser>().To<ShaderParser>()
            .Bind<IMapFileParser>().To<MapFileParser>()
            .Bind<IReferenceParser>(1).To<AseParser>()
            .Bind<IReferenceParser>(2).To<Md3Parser>()
            .Bind<IReferenceParser>(3).To<SkinParser>()
            .Bind<IResourceRefParser>().To<ResourceRefParser>()
            .Bind<IAssetService>().To<AssetService>()
            .Bind<LoggerBase>().To<LoggerBase>()
            .Bind<ILogger<TT>>().To<Logger<TT>>()
            .Bind<ILineReader>().To<FSLineReader>()
            .Bind<IProgressManager>().To<ConsoleProgressManager>()
            .Bind<IIntegrityChecker>().To<IntegrityChecker>()
            .Bind<AppLifetime>().To<AppLifetime>()
            .Bind().To(ctx => { ctx.Inject<PackOptionsWrapper>(out var wrapper); return wrapper.Value; })
            .Root<App>("Application")
            .Arg<PackOptions>("options", "options");
    }

    public static Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            if (AskArgs() is not { } valid)
            {
                return Task.FromResult(-1);
            }

            args = valid;
        }

        return Cli.RunAsync<RootCommand>(args);
    }

    public static async Task<int> Execute(PackOptions options)
    {
        using var composition = new Composition(options);

        App app = composition.Application;
        CancellationToken cancellationToken = app.Lifetime.CancellationToken;
        bool fileCreated = false;

        if (!options.DryRun &&
            !options.Overwrite &&
            options.Pk3File is { Exists: true } pk3File)
        {
            if (options.LogLevel == LogLevel.None)
                return -1;

            if (!PromptOverwrite(pk3File))
                return 0;
        }

        try
        {
            PrintDetails(options, app.Logger);

            var startTime = Stopwatch.GetTimestamp();

            PackResult result;

            // map keeps read-locks for pk3s it encounters, so keep it alive for as little time as possible
            using (Map map = await app.AssetService.GetPackingData(cancellationToken))
            {
                Stream destination;

                if (!options.DryRun)
                {
                    destination = new FileStream(options.Pk3File.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
                    fileCreated = true;
                }
                else
                {
                    destination = new CountingStream();
                }

                using (destination)
                {
                    result = await app.Packager.CreateZip(map, destination, cancellationToken);
                }
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTime);

            app.Logger.Drain();

            string output = !options.DryRun ? $" to '{options.Pk3File.FullName}'" : "";
            app.Logger.System($"Finished in {elapsed.TotalSeconds:F1} seconds, {result}, pk3 size: {result.Size()}{output}");

            return 0;
        }
        catch (Exception e)
        {
            if (fileCreated)
            {
                try
                {
                    File.Delete(options.Pk3File!.FullName);
                }
                catch (Exception e2)
                {
                    e = new AggregateException("Failed to delete partial pk3", e, e2);
                }
            }

            app.Lifetime.HandleException(e);
            return -1;
        }
    }

    private static void PrintDetails(PackOptions options, ILogger<Program> appLogger)
    {
        StringBuilder sb = new();

        sb.Append(options.DryRun ? "Running a dry run for map " : "Packing map ");
        sb.Append(Path.GetFileNameWithoutExtension(options.MapFile.Name.AsSpan()));

        if (options.OnlySource)
        {
            sb.Append(" source");
        }

        if (!options.DryRun)
        {
            if (options.Rename is { Length: > 0 })
            {
                sb.Append(" as ");
                sb.Append(options.Rename);
            }

            sb.Append(" to ");
            sb.Append(options.Pk3File?.FullName ?? "<n/a>");

            if (options is { Overwrite: true, Pk3File.Exists: true })
            {
                sb.Append(" (overwriting existing file)");
            }
        }
        else
        {
            sb.Append(" without creating a pk3");
        }

        sb.Append('.');

        sb.Append(
            options.RequireAllAssets
                ? " All assets are required."
                : " Missing assets are ignored.");

        sb.Append(options.LoadPk3s ? " Pk3s in etmain are scanned." : " Pk3s in etmain are not scanned.");

        sb.Append(" Logging verbosity is '");
        sb.Append(options.LogLevel);
        sb.Append("'.");

        if (options.ModFolders is { Count : > 0 })
        {
            sb.Append(" Mod folders are scanned: ");
            sb.AppendJoin(", ", options.ModFolders);
        }

        appLogger.System($"{sb.ToString()}");
    }

    private static bool PromptOverwrite(FileInfo pk3)
    {
        System.Console.ForegroundColor = ConsoleColor.Red;
        System.Console.Write($"Output file already exists: ");
        System.Console.ResetColor();
        System.Console.WriteLine(pk3.FullName);
        System.Console.Write("Overwrite? ");
        System.Console.ForegroundColor = ConsoleColor.Cyan;
        System.Console.WriteLine("Y/N");
        System.Console.ResetColor();
        return System.Console.ReadLine().AsSpan().Trim().EqualsF("y");
    }

    private static string[]? AskArgs()
    {
        if (OperatingSystem.IsWindows())
        {
            if (OpenFileDialog.OpenFile(out string file) && !string.IsNullOrEmpty(file))
            {
                return [file];
            }

            return null;
        }

        System.Console.WriteLine($"Pack3r {typeof(PackOptions).Assembly.GetName().Version?.ToString(3)}");
        System.Console.WriteLine("For more options, run Pack3r through the command line");
        System.Console.WriteLine("Enter path to .map file:");
        string? res = System.Console.ReadLine()?.Trim();

        if (res is not null)
        {
            return [res];
        }

        return [];
    }
}

internal sealed record App(
    ILogger<Program> Logger,
    IAssetService AssetService,
    Packager Packager,
    AppLifetime Lifetime,
    [Tag("options")] PackOptionsWrapper Options) // <- required for DI to generate it
{
}

// pureDI hack
internal sealed class PackOptionsWrapper([Tag("options")] PackOptions value)
{
    public PackOptions Value => value;
}
