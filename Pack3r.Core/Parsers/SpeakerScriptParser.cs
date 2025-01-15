using System.Runtime.CompilerServices;
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

    public async IAsyncEnumerable<Resource> Parse(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var line in reader.ReadLines(path, cancellationToken).ConfigureAwait(false))
        {
            if (line.MatchKeyword("noise", out var token))
            {
                var noise = token.Trim('"').Trim();

                if (noise.GetExtension().IsEmpty)
                {
                    logger.Warn(
                        $"Speakerscript has a missing file extension on line {line.Index}: '{noise}' (will not work on 2.60b)");
                }
                else
                {
                    yield return new(noise, isShader: false, in line);
                }
            }
        }
    }

    public string GetRelativePath(string mapName)
    {
        return Path.Combine("sound", "maps", $"{mapName}.sps");
    }
}
