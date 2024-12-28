using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SoundscriptParser(
    ILineReader reader) : IResourceParser
{
    public string Description => "soundscript";

    public bool SearchModDirectories => false;

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, cancellationToken).ConfigureAwait(false))
        {
            if (line.MatchKeyword("sound", out var token))
            {
                yield return new(token, false, in line);
            }
        }
    }

    public string GetRelativePath(string mapName)
    {
        return Path.Combine("sound", "scripts", $"{mapName}.sounds");
    }
}
