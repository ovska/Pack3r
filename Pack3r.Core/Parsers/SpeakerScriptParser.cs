using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SpeakerScriptParser(
    ILineReader reader,
    ILogger<SpeakerScriptParser> logger) : IResourceParser
{
    public string Description => "speakerscript";

    public bool SearchModDirectories => true;

    public IEnumerable<Resource> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        List<Resource> resources = [];

        foreach (var line in reader.ReadLines(path, cancellationToken))
        {
            if (line.MatchKeyword("noise"u8, out var token))
            {
                QString noise = token.Trim((byte)'"').TrimWhitespace().ToQString();

                if (noise.Span.GetExtension().IsEmpty)
                {
                    logger.Warn(
                        $"Speakerscript has a missing file extension on line {line.Index}: '{noise}' (will not work on 2.60b)");
                }
                else
                {
                    resources.Add(new Resource(noise, isShader: false, line.ToLine()));
                }
            }
        }

        return resources;
    }

    public string GetRelativePath(string mapName)
    {
        return Path.Combine("sound", "maps", $"{mapName}.sps");
    }
}
