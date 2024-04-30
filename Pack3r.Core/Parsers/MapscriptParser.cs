﻿using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

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

            if (TryRead(in line, out Resource resource))
            {
                yield return resource;
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

    private static bool TryRead(in Line line, out Resource resource)
    {
        var enumerator = Tokens.WhitespaceSeparatedTokens().EnumerateMatches(line.Value.Span);

        if (enumerator.MoveNext())
        {
            var keyword = line.Value.Slice(enumerator.Current).Span;

            if (keyword.EqualsF("playsound"))
            {
                // first token is the sound file
                if (enumerator.MoveNext())
                {
                    resource = new Resource(line.Value.Slice(enumerator.Current).TrimQuotes(), IsShader: false);
                    return true;
                }

            }
            else if (keyword.EqualsF("remapshader"))
            {
                // second token is the target shader
                if (enumerator.MoveNext() && enumerator.MoveNext())
                {
                    resource = new Resource(line.Value.Slice(enumerator.Current).TrimQuotes(), IsShader: true);
                    return true;
                }
            }
        }

        resource = default;
        return false;
    }

    public string GetPath(Map map, string? rename = null)
    {
        return Path.Combine(
            map.GetMapRoot(),
            "maps",
            $"{rename ?? map.Name}.script");
    }
}
