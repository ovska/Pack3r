using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pack3r;
using Pack3r.Core.Parsers;
using Pack3r.IO;

Console.WriteLine((int)'{');
Console.WriteLine((int)'}');
Console.ReadLine();

var services = new ServiceCollection();

services.AddLogging(builder => builder.AddConsole());

services.AddSingleton<IResourceParser, MapscriptParser>();
services.AddSingleton<IResourceParser, SoundscriptParser>();
services.AddSingleton<IResourceParser, SpeakerScriptParser>();
services.AddSingleton<IShaderParser, ShaderParser>();

services.AddSingleton<ITempDirectoryProvider, FSTempDirectoryProvider>();

using var sp = services.BuildServiceProvider();
