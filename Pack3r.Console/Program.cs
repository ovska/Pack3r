using System.Diagnostics;
using Pack3r;
using Pack3r.Console;
using Pack3r.Core.Parsers;
using Pack3r.IO;
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
    .Bind().To(ctx =>
    {
        ctx.Inject<PackOptionsWrapper>(out var q);
        return q.Value;
    })
    .Root<App>("Application")
    .Arg<PackOptions>("options", "options")
    ;

await Commandline.Run(args, Execute);

static async Task<int> Execute(PackOptions options)
{
    int retval = 0;

    using (var composition = new Composition(options))
    {
        var app = composition.Application;

        bool fileCreated = false;

        try
        {
            var mapName = options.MapFile.Name;
            var mapsDir = Path.GetDirectoryName(options.MapFile.FullName);

            app.Logger.System($"Packaging from '{mapName}' in '{mapsDir}' to '{options.Pk3File.FullName}'");

            var timer = Stopwatch.StartNew();

            PackingData data = await app.AssetService.GetPackingData(app.CancellationToken);

            await using (var destination = new FileStream(options.Pk3File.FullName, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fileCreated = true;
                await app.Packager.CreateZip(data, destination, app.CancellationToken);
            }

            timer.Stop();

            app.Logger.Drain();
            app.Logger.System($"Packaging finished in {timer.ElapsedMilliseconds} ms, press Enter to exit");
        }
        catch (Exception e)
        {
            if (fileCreated && options.Overwrite)
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
    [Tag("options")] PackOptionsWrapper Options)
{
    public CancellationToken CancellationToken => Lifetime.CancellationToken;
}

sealed class PackOptionsWrapper([Tag("options")] PackOptions value)
{
    public PackOptions Value => value;
}
