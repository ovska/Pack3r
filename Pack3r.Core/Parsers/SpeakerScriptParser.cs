using System.Runtime.CompilerServices;
using Pack3r.IO;

namespace Pack3r.Core.Parsers;

public class SpeakerScriptParser(
    ILineReader reader) : IResourceParser
{
    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, default, cancellationToken).ConfigureAwait(false))
        {
            if (line.MatchPrefix("noise ", out var token))
            {
                yield return new(token.Trim('"').Trim(), false);
            }
        }
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(map.ETMain.FullName, "sound", "maps", $"{rename ?? map.Name}.sps");
    }
}
