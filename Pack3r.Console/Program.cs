using System.Diagnostics;
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

#pragma warning disable RCS1102 // Make class static

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
            .Bind<IAssetService>().To<AssetService>()
            .Bind<LoggerBase>().To<LoggerBase>()
            .Bind<ILogger<TT>>().To<Logger<TT>>()
            .Bind<ILineReader>().To<FSLineReader>()
            .Bind<IProgressManager>().To<ConsoleProgressManager>()
            .Bind<AppLifetime>().To<AppLifetime>()
            .Bind().To(ctx => { ctx.Inject<PackOptionsWrapper>(out var wrapper); return wrapper.Value; })
            .Root<App>("Application")
            .Arg<PackOptions>("options", "options");
    }

    public static async Task Main(string[] args)
    {
        await Cli.RunAsync<RootCommand>(args);
    }

    public static async Task<int> Execute(PackOptions options)
    {
        int retval = 0;

        using (var composition = new Composition(options))
        {
            var app = composition.Application;
            CancellationToken cancellationToken = app.CancellationToken;

            try
            {
                string mapName = Path.GetFileNameWithoutExtension(options.MapFile.FullName);

                if (!options.DryRun)
                {
                    if (options.Rename != null)
                        mapName += "' as '" + options.Rename;

                    app.Logger.System($"Packaging '{mapName}' to '{options.Pk3File.FullName}'");
                }
                else
                {
                    app.Logger.System($"Running asset discovery for '{mapName}' without creating a pk3");
                }

                Stream destination;
                var timer = Stopwatch.StartNew();

                using (Map map = await app.AssetService.GetPackingData(cancellationToken))
                {
                    if (!options.DryRun)
                    {
                        destination = new FileStream(options.Pk3File.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
                    }
                    else
                    {
                        options.Pk3File = null!;
                        destination = new CountingStream();
                    }

                    await using (destination)
                    {
                        await app.Packager.CreateZip(map, destination, cancellationToken);
                    }
                }

                timer.Stop();

                app.Logger.Drain();

                if (options.DryRun)
                {
                    long written = ((CountingStream)destination).Position;
                    app.Logger.System($"Pk3 size: {(double)written / 1024:N} KB ({written} bytes)");
                    app.Logger.System($"Dry run finished in {timer.ElapsedMilliseconds} ms, press Enter to exit");
                }
                else
                {
                    app.Logger.System($"Packaging finished in {timer.ElapsedMilliseconds} ms, press Enter to exit");
                }
            }
            catch (Exception e)
            {
                if (!options.DryRun)
                    File.Delete(options.Pk3File.FullName);

                app.Lifetime.HandleException(e);
                retval = 1;
            }
        }

        System.Console.ReadLine();
        return retval;
    }
}

internal sealed record App(
    ILogger<Program> Logger,
    IAssetService AssetService,
    Packager Packager,
    AppLifetime Lifetime,
    [Tag("options")] PackOptionsWrapper Options) // <- required for DI to generate it
{
    public CancellationToken CancellationToken => Lifetime.CancellationToken;
}

// pureDI hack
internal sealed class PackOptionsWrapper([Tag("options")] PackOptions value)
{
    public PackOptions Value => value;
}
