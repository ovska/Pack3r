﻿using System.Runtime.CompilerServices;
using Pack3r.Core.Parsers;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r;

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
        return Path.Combine(map.ETMain.FullName, "sound", "scripts", $"{rename ?? map.Name}.sounds");
    }
}
