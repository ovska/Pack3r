﻿using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SpeakerScriptParser(
    ILineReader reader) : IResourceParser
{
    public string Description => "speakerscript";

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, default, cancellationToken).ConfigureAwait(false))
        {
            if (line.MatchKeyword("noise", out var token))
            {
                yield return new(token.Trim('"').Trim(), isShader: false, in line);
            }
        }
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(
            map.GetMapRoot(),
            "sound",
            "maps",
            $"{rename ?? map.Name}.sps");
    }
}
