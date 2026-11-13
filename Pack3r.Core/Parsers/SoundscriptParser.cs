using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class SoundscriptParser(
    ILineReader reader) : IResourceParser
{
    public string Description => "soundscript";

    public bool SearchModDirectories => false;

    public IEnumerable<Resource> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var line in reader.ReadLines(path, cancellationToken))
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
