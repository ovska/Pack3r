using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pack3r;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Services;

//DI.Setup("Pack3rServices")
//    .Bind<ILogger<TT>>().To((IContext ctx) => )
//    .Root<ServiceRoot>("Root");

//var cmd = new RootCommand();

//await cmd.InvokeAsync(args);

using var cts = CreateConsoleCancellationSource();
using var sp = InitServices();

using var exceptionHandler = new ExceptionHandlerScope(sp.Get<ILogger<ExceptionHandlerScope>>(), cts.Token);

var path = @"C:\Temp\ET\map\ET\etmain\maps\sungilarity.map";

var logger = sp.Get<ILogger<Program>>();
logger.LogInformation("Starting packaging from path '{path}'", path);

var sw = System.Diagnostics.Stopwatch.StartNew();

var assetService = sp.Get<IAssetService>();
PackingData data = await assetService.GetPackingData(path, cts.Token);

await using var destination = new FileStream(@"C:\Temp\test.pk3", FileMode.Create, FileAccess.Write, FileShare.None);

var packager = sp.Get<Packager>();
await packager.CreateZip(data, destination, cts.Token);

sw.Stop();
logger.LogInformation("Packaging finished in {time} ms, press Enter to exit", sw.ElapsedMilliseconds);
Console.ReadLine();

static ServiceProvider InitServices()
{
    var services = new ServiceCollection();

    services.AddLogging(builder => builder.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.IncludeScopes = false;
        o.TimestampFormat = "HH:mm:ss ";
    }));
    services.AddSingleton<IResourceParser, MapscriptParser>();
    services.AddSingleton<IResourceParser, SoundscriptParser>();
    services.AddSingleton<IResourceParser, SpeakerScriptParser>();
    services.AddSingleton<IShaderParser, ShaderParser>();
    services.AddSingleton<IPk3Reader, Pk3Reader>();
    services.AddSingleton<IMapFileParser, MapFileParser>();
    services.AddSingleton<IAssetService, AssetService>();
    services.AddSingleton<Packager>();

    // IO
    services.AddSingleton<ILineReader, FSLineReader>();

    services.AddOptions<PackOptions>();
    services.Configure<PackOptions>(o =>
    {
        o.DevFiles = false;
        o.RequireAllAssets = false;
    });

    return services.BuildServiceProvider();
}

static CancellationTokenSource CreateConsoleCancellationSource()
{
    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, _) => cts.Cancel();
    return cts;
}
