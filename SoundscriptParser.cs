using System.Runtime.CompilerServices;
using CommunityToolkit.Diagnostics;
using Microsoft.Extensions.Logging;
using Pack3r.IO;

namespace Pack3r;

public class SoundscriptParser(
    ILineReader reader) : IResourceParser
{
    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Guard.IsEqualTo(Path.GetExtension(path), ".sounds", "path");

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
        return Path.Combine(map.ETMain.FullName, "sound", "scripts", $"{rename ?? map.Name}.sounds");
    }
}
