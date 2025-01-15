using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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

    public bool SearchModDirectories => false;

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        HashSet<QString> unsupported = [];

        await foreach (var line in reader.ReadLines(path, cancellationToken).ConfigureAwait(false))
        {
            // skip everything except: playsound, remapshader, set, create, changeskin
            if ((line.FirstChar | 0x20) is not ('p' or 'r' or 's' or 'c'))
            {
                continue;
            }

            if (TryRead(in line, out Resource? resource))
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
            var keywords = string.Join(", ", unsupported.Select(l => $"'{l}'"));
            logger.Warn($"Mapscript has keyword(s) ({keywords}) that can include un-discoverable resources such as dynamically loaded models, please manually ensure they are included");
        }
    }

    private static bool TryRead(in Line line, [NotNullWhen(true)] out Resource? resource)
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
                    resource = new Resource(line.Value.Slice(enumerator.Current).TrimQuotes(), isShader: false, in line);
                    return true;
                }

            }
            else if (keyword.EqualsF("remapshader"))
            {
                // second token is the target shader
                if (enumerator.MoveNext() && enumerator.MoveNext())
                {
                    resource = new Resource(line.Value.Slice(enumerator.Current).TrimQuotes(), isShader: true, in line);
                    return true;
                }
            }
            else if (keyword.EqualsF("changemodel") || keyword.EqualsF("changeskin"))
            {
                // first token is the skin/model
                if (enumerator.MoveNext())
                {
                    resource = new Resource(line.Value.Slice(enumerator.Current).TrimQuotes(), isShader: false, in line)
                    {
                        CanReferenceResources = true
                    };
                    return true;
                }
            }
        }

        resource = default;
        return false;
    }

    public string GetRelativePath(string mapName)
    {
        return Path.Combine("maps", $"{mapName}.script");
    }
}
