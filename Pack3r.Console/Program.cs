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

static async Task Execute(PackOptions options)
{
    int retval = 0;

    using (var composition = new Composition(options))
    {
        var app = composition.Application;

        bool fileCreated = false;

        const string path = @"C:\Temp\ET\map\ET\etmain\maps\sungilarity.map";
        const string dest = @"C:\Temp\test.pk3";

        try
        {
            var mapName = Path.GetFileName(path);
            var mapsDir = Path.GetDirectoryName(path);

            app.Logger.System($"Packaging from '{mapName}' in '{mapsDir}' to '{dest}'");

            var timer = Stopwatch.StartNew();

            PackingData data = await app.AssetService.GetPackingData(path, app.CancellationToken);

            await using (var destination = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
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
            if (fileCreated)
                File.Delete(dest);

            app.Lifetime.HandleException(e);
            retval = 1;
        }
    }

    Console.ReadLine();
    Environment.Exit(retval);
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

