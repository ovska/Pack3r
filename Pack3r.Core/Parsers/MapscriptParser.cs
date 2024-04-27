using System.Runtime.CompilerServices;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;

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
        HashSet<ReadOnlyMemory<char>> unsupported = new(ROMCharComparer.Instance);

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
                unsupported.Add(line.Value);
            }
        }

        if (unsupported.Count > 0)
        {
            var keywords = string.Join(", ", unsupported.Select(l => l));
            logger.Warn($"Mapscript has keyword(s) ({keywords}) that can include undiscoverable resources, please manually ensure they are included");
        }
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(map.ETMain.FullName, "maps", $"{rename ?? map.Name}.script");
    }
}
