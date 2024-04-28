using Pack3r.Parsers;

namespace Pack3r.Tests;

public static class SpeakerScriptTests
{
    [Fact]
    public static async Task Should_Parse_SpeakerScript()
    {
        var reader = new StringLineReader("""
            speakerScript
            {
            	speakerDef {
            		noise "sound/world/war.wav"
            		origin -229.57 1087.27 502.41
            		looped "on"
            		broadcast "nopvs"
            		volume 127
            		range 1250
            	}

            	speakerDef {
            		noise "sound/world/machine_01.wav"
            		origin 1599.28 1962.78 404.12
            		looped "on"
            		broadcast "nopvs"
            		volume 256
            		range 800
            	}
            }
            """);

        var parser = new SpeakerScriptParser(reader);

        var results = await parser.Parse("a.sps", default).ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("sound/world/war.wav", results[0].Value.ToString());
        Assert.Equal("sound/world/machine_01.wav", results[1].Value.ToString());
    }
}
