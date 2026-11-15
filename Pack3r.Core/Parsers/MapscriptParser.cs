using System.Diagnostics.CodeAnalysis;
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

    public IEnumerable<Resource> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        List<Resource> resources = [];
        HashSet<QString> unsupported = [];

        foreach (var line in reader.ReadLines(path, cancellationToken))
        {
            // skip everything except: playsound, remapshader, set, create, changeskin
            if ((line.FirstChar | 0x20) is not ('p' or 'r' or 's' or 'c'))
            {
                continue;
            }

            if (TryRead(in line, out Resource? resource))
            {
                resources.Add(resource);
            }
            else if (line.Value.Equals("set", StringComparison.OrdinalIgnoreCase) || line.Value.Equals("create", StringComparison.OrdinalIgnoreCase))
            {
                // keywords that aren't parsed for resources
                unsupported.Add(line.Value.ToQString());
            }
        }

        if (unsupported.Count > 0)
        {
            var keywords = string.Join(", ", unsupported.Select(l => $"'{l}'"));
            logger.Warn($"Mapscript has keyword(s) ({keywords}) that can include un-discoverable resources such as dynamically loaded models, please manually ensure they are included");
        }

        return resources;
    }

    private static bool TryRead(in LineRef line, [NotNullWhen(true)] out Resource? resource)
    {
        var tokens = line.Value.EnumerateTokens();

        if (tokens.MoveNext())
        {
            ReadOnlySpan<char> keyword = tokens.Current;

            if (keyword.Equals("playsound", StringComparison.OrdinalIgnoreCase))
            {
                // first token is the sound file
                if (tokens.MoveNext())
                {
                    resource = new Resource(tokens.Current.TrimQuotes().ToQString(), isShader: false, line.ToLine());
                    return true;
                }
            }
            else if (keyword.Equals("remapshader", StringComparison.OrdinalIgnoreCase))
            {
                // second token is the target shader
                if (tokens.MoveNext() && tokens.MoveNext())
                {
                    resource = new Resource(tokens.Current.TrimQuotes().ToQString(), isShader: true, line.ToLine());
                    return true;
                }
            }
            else if (keyword.Equals("changemodel", StringComparison.OrdinalIgnoreCase) || keyword.Equals("changeskin", StringComparison.OrdinalIgnoreCase))
            {
                // first token is the skin/model
                if (tokens.MoveNext())
                {
                    resource = new Resource(tokens.Current.TrimQuotes().ToQString(), isShader: false, line.ToLine())
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
