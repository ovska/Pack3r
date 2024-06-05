using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Pack3r.Parsers;

namespace Pack3r.Tests;

public static class SkinParserTests
{
    [Fact]
    public static async Task Should_Parse_Skin()
    {
        var reader = new StringLineReader("""
            head, "models/players/hud/allied_field"
            teeth,"models/players/hud/teeth01"
            eye1, "models/players/hud/eye02"
            eye2, "models/players/hud/eye02"
            """);

        var parser = new SkinParser(reader);

        Assert.True(parser.CanParse("test.skin".AsMemory()));
        Assert.False(parser.CanParse("test.md3".AsMemory()));

        var result = await parser.Parse(new MockAsset(), default);

        Assert.NotNull(result);
        Assert.Equal(
            [
                "models/players/hud/allied_field",
                "models/players/hud/teeth01",
                "models/players/hud/eye02",
            ],
            result.Select(s => s.Value.ToString()));
    }
}
