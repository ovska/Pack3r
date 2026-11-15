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
        List<Resource> resources = [];

        foreach (var line in reader.ReadLines(path, cancellationToken))
        {
            if (line.MatchKeyword("sound", out var token))
            {
                resources.Add(new Resource(token.ToQString(), isShader: false, line.ToLine()));
            }
        }

        return resources;
    }

    public string GetRelativePath(string mapName)
    {
        return Path.Combine("sound", "scripts", $"{mapName}.sounds");
    }
}
