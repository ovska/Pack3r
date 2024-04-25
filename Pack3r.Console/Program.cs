using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pack3r;
using Pack3r.Core.Parsers;
using Pack3r.IO;
using Pack3r.Services;

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, _) => cts.Cancel();

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddConsole());
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

using var sp = services.BuildServiceProvider();

using var exceptionHandler = new ExceptionHandlerScope(
    sp.GetRequiredService<ILogger<ExceptionHandlerScope>>(),
    cts.Token);

var sw = System.Diagnostics.Stopwatch.StartNew();

var path = @"C:\Temp\ET\map\ET\etmain\maps\sungilarity.map";
var assetService = sp.GetRequiredService<IAssetService>();
var packager = sp.GetRequiredService<Packager>();

// parse .map, associated resource files, pak0 
var data = await assetService.GetPackingData(path, cts.Token);

var shaderParser = sp.GetRequiredService<IShaderParser>();

var relative = data.Map.RelativePath(path);

await using var destination = File.OpenWrite(@"C:\Temp\test.pk3");

await packager.CreateZip(data, destination, cts.Token);

sw.Stop();
int a = 1;
//using var dir = tempDirProvider.Create();
