using Pack3r.Logging;
using Pack3r.Parsers;

namespace Pack3r.Tests;

public static class MapscriptTests
{
    [Fact]
    public static async Task Should_Parse_Mapscript()
    {
        var reader = new StringLineReader("""
            game_manager
            {
                spawn
                {
                    wait 100
                    playsound testi.wav
                    playsound "path/to/sound.wav" looping volume 255
                    remapshader "shader/a" shader/test_01
                    remapshader shader/b "shader/c"
                }
            }
            """);

        var parser = new MapscriptParser(reader, NullLogger<MapscriptParser>.Instance);

        var results = await parser.Parse("a.script", default).ToList();

        Assert.Equal(4, results.Count);

        Assert.Equal(
            ["testi.wav", "path/to/sound.wav"],
            results.Where(x => !x.IsShader).Select(x => x.Value.ToString()));

        Assert.Equal(
            ["shader/test_01", "shader/c"],
            results.Where(x => x.IsShader).Select(x => x.Value.ToString()));
    }
}
