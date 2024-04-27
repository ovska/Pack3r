using System.Diagnostics;
using System.Text.Json.Serialization.Metadata;
using Pack3r;
using Pack3r.Console;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Progress;
using Pack3r.Services;
using Pure.DI;

DI.Setup("Composition")
    .DefaultLifetime(Lifetime.Scoped)
    .Bind<IResourceParser>(1).To<MapscriptParser>()
    .Bind<IResourceParser>(2).To<SoundscriptParser>()
    .Bind<IResourceParser>(3).To<SpeakerScriptParser>()
    .Bind<IShaderParser>().To<ShaderParser>()
    .Bind<IPk3Reader>().To<Pk3Reader>()
    .Bind<IMapFileParser>().To<MapFileParser>()
    .Bind<IAssetService>().To<AssetService>()
    .Bind<LoggerBase>().To<LoggerBase>()
    .Bind<ILogger<TT>>().To<Logger<TT>>()
    .Bind<ILineReader>().To<FSLineReader>()
    .Bind<IProgressManager>().To<ConsoleProgressManager>()
    .Bind<AppLifetime>().To<AppLifetime>()
    .Bind().To(ctx => { ctx.Inject<PackOptionsWrapper>(out var wrapper); return wrapper.Value; })
    .Root<App>("Application")
    .Arg<PackOptions>("options", "options")
    ;

await Commandline.Run(args, Execute);

static async Task<int> Execute(PackOptions options, CancellationToken systemToken)
{
    int retval = 0;

    using (var composition = new Composition(options))
    {
        var app = composition.Application;

        using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(systemToken, app.CancellationToken);
        CancellationToken cancellationToken = linkedTokenSource.Token;

        try
        {
            var mapName = options.MapFile.Name;
            var mapsDir = Path.GetDirectoryName(options.MapFile.FullName);

            if (!options.DryRun)
            {
                app.Logger.System($"Packaging from '{mapName}' in '{mapsDir}' to '{options.Pk3File.FullName}'");
            }
            else
            {
                app.Logger.System($"Running asset discovery for '{mapName}' in '{mapsDir}' without creating a pk3");
            }

            var timer = Stopwatch.StartNew();

            PackingData data = await app.AssetService.GetPackingData(cancellationToken);

            Stream destination;

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
                await app.Packager.CreateZip(data, destination, cancellationToken);
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

    Console.ReadLine();
    return retval;
}

#pragma warning disable RCS1110 // Declare type inside namespace

sealed record App(
    ILogger<Program> Logger,
    IAssetService AssetService,
    Packager Packager,
    AppLifetime Lifetime,
    [Tag("options")] PackOptionsWrapper Options) // <- required for DI to generate it
{
    public CancellationToken CancellationToken => Lifetime.CancellationToken;
}

// pureDI hack
sealed class PackOptionsWrapper([Tag("options")] PackOptions value)
{
    public PackOptions Value => value;
}
