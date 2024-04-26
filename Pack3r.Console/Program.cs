using Pack3r;
using Pack3r.Core.Parsers;
using Pack3r.IO;
using Pack3r.Services;
using Pure.DI;

DI.Setup("Composition")
    .DefaultLifetime(Lifetime.Singleton)
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
    .Bind<PackOptions>().To(static _ => new PackOptions { RequireAllAssets = false })
    .Root<ServiceRoot>("Application");

int retval = 0;

using (var composition = new Composition())
{
    var app = composition.Application;

    try
    {
        const string path = @"C:\Temp\ET\map\ET\etmain\maps\sungilarity.map";
        const string dest = @"C:\Temp\test.pk3";

        var mapName = Path.GetFileName(path);
        var mapsDir = Path.GetDirectoryName(path);

        app.Logger.System($"Packaging from '{mapName}' in '{mapsDir}' to '{dest}'");

        var sw = System.Diagnostics.Stopwatch.StartNew();

        PackingData data = await app.AssetService.GetPackingData(path, app.CancellationToken);

        await using var destination = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);

        await app.Packager.CreateZip(data, destination, app.CancellationToken);

        sw.Stop();

        app.Logger.Drain();
        app.Logger.System($"Packaging finished in {sw.ElapsedMilliseconds} ms, press Enter to exit");
    }
    catch (Exception e)
    {
        app.Lifetime.HandleException(e);
        retval = 1;
    }
}

Console.ReadLine();
Environment.Exit(retval);

#pragma warning disable RCS1110 // Declare type inside namespace
sealed record ServiceRoot(
    ILogger<Program> Logger,
    IAssetService AssetService,
    Packager Packager,
    AppLifetime Lifetime)
{
    public CancellationToken CancellationToken => Lifetime.CancellationToken;
}
#pragma warning restore RCS1110 // Declare type inside namespace
