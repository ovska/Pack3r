using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SoundscriptParser(
    ILineReader reader) : IResourceParser
{
    public string Description => "soundscript";

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, default, cancellationToken).ConfigureAwait(false))
        {
            if (line.MatchPrefix("sound ", out var token))
            {
                yield return new(token, false);
            }
        }
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(
            map.GetMapRoot(),
            "sound",
            "scripts",
            $"{rename ?? map.Name}.sounds");
    }
}
