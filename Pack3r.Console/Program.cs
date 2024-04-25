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
services.AddSingleton<IExceptionHandlerScope>(
    sp => new ExceptionHandlerScope(sp.GetRequiredService<ILogger<ExceptionHandlerScope>>(), cts.Token));
services.AddSingleton<IResourceParser, MapscriptParser>();
services.AddSingleton<IResourceParser, SoundscriptParser>();
services.AddSingleton<IResourceParser, SpeakerScriptParser>();
services.AddSingleton<IShaderParser, ShaderParser>();
services.AddSingleton<IPk3Reader, Pk3Reader>();
services.AddSingleton<IMapFileParser, MapFileParser>();
services.AddSingleton<IAssetService, AssetService>();

// IO
services.AddSingleton<ILineReader, FSLineReader>();
services.AddSingleton<ITempDirectoryProvider, FSTempDirectoryProvider>();

services.AddOptions<PackOptions>();

using var sp = services.BuildServiceProvider();

using var exceptionHandler = sp.GetRequiredService<IExceptionHandlerScope>();

var sw = System.Diagnostics.Stopwatch.StartNew();

var path = @"C:\Temp\ET\map\ET\etmain\maps\sunjump.map";
var assetService = sp.GetRequiredService<IAssetService>();

var data = await assetService.GetPackingData(path, cts.Token);

sw.Stop();
int a = 1;
//using var dir = tempDirProvider.Create();
