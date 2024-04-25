using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r;

public class MapscriptParser(
    ILineReader reader,
    ILogger<MapscriptParser> logger)
    : IResourceParser
{
    public string Description => "mapscript";

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, default, cancellationToken).ConfigureAwait(false))
        {
            // skip everything except: playsound, remapshader, set, create
            if ((line.FirstChar | 0x20) is not ('p' or 'r' or 's' or 'c'))
            {
                continue;
            }

            if (line.MatchPrefix("playsound ", out var token))
            {
                // read first arg, playsound can have multiple, e.g.:
                //    playsound sound/menu/filter.wav volume 256 looping
                if (token.TryReadUpToWhitespace(out var firstArg))
                {
                    token = firstArg;
                }

                yield return new Resource(
                    token.Trim('"').Trim(),
                    IsShader: false);
            }
            else if (line.MatchPrefix("remapshader ", out token))
            {
                foreach (var range in token.Split(' '))
                    yield return new Resource(token[range], IsShader: true);
            }
            else if (Tokens.UnsupportedMapscript().IsMatch(line.Value.Span))
            {
                logger.LogWarning(
                    "Unsupported mapscript keyword that potentially affects required files " +
                    "in line {line} in file '{path}': '{value}'",
                    line.Index,
                    line.Path,
                    line.Value);
            }
        }
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(map.ETMain.FullName, "maps", $"{rename ?? map.Name}.script");
    }
}
