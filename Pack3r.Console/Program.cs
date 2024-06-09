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
            .Bind<IReferenceResourceParser>().To<ReferenceResourceParser>()
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
        return Cli.RunAsync<RootCommand>(args);
    }

    public static async Task<int> Execute(PackOptions options)
    {
        using var composition = new Composition(options);

        var app = composition.Application;
        CancellationToken cancellationToken = app.Lifetime.CancellationToken;
        bool fileCreated = false;

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
                app.Logger.System($"Running dry run for '{mapName}' without creating a pk3");
            }

            Stream destination;
            var timer = Stopwatch.StartNew();

            long written;
            int missingCount;

            using (Map map = await app.AssetService.GetPackingData(cancellationToken))
            {
                if (!options.DryRun)
                {
                    destination = new FileStream(options.Pk3File.FullName, FileMode.Create, FileAccess.Write, FileShare.None);
                    fileCreated = true;
                }
                else
                {
                    options.Pk3File = null!;
                    destination = new CountingStream();
                }

                using (destination)
                {
                    missingCount = await app.Packager.CreateZip(map, destination, cancellationToken);
                    written = destination.Position;
                }
            }

            timer.Stop();

            app.Logger.Drain();

            if (!options.DryRun)
            {
                app.Logger.System($"Packaging finished in {timer.ElapsedMilliseconds} ms, pk3 size: {Size(written)}{Missing(missingCount)}");
            }
            else
            {
                app.Logger.System($"Dry run finished in {timer.ElapsedMilliseconds} ms, estimated pk3 size: {Size(written)}{Missing(missingCount)}");
            }

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

        static string Size(long written)
        {
            const long kilobyte = 1024;
            const long megabyte = 1024 * 1024;
            return written > megabyte
                ? $"{(double)written / megabyte:N} MB"
                : $"{(double)written / kilobyte:N} KB";
        }

        static string Missing(int missingCount)
        {
            return missingCount > 0
                ? $", with {missingCount} file{(missingCount != 1 ? "s" : "")} missing"
                : "";
        }
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
