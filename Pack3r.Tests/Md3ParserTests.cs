using Pack3r.Logging;
using Pack3r.Parsers;

namespace Pack3r.Tests;

public static class Md3ParserTests
{
    [Fact]
    public static async Task Should_Parse_Md3()
    {
        var path = Path.GetFullPath("../../../TestData/etmain/models/spire_active_red.md3");
        var parser = new Md3Parser(NullLogger<Md3Parser>.Instance);

        Assert.True(parser.CanParse(path.AsMemory()));

        var result = await parser.Parse(new FileAsset(path), default);

        Assert.NotNull(result);
        Assert.All(result, s => Assert.True(s.IsShader));
        Assert.Equal(
        [
            "textures/pgm/spire_bar3",
            "textures/pgm/spire_bar4",
            "textures/pgm/fx_bar4_red",
            "textures/pgm/zap",
            "textures/pgm/spire_yelbase1",
            "textures/pgm/spire_wallbarsvari1",
            "textures/pgm/spire_glass",
            "textures/pgm/spire_abal6_small",
            "textures/pgm/crystal_red",
        ], result!.Select(s => s.Value.ToString()));
    }

    [Fact]
    public static async Task Should_Parse_Mdm()
    {
        var path = Path.GetFullPath("../../../TestData/etmain/models/sidechair3.mdc");
        var parser = new Md3Parser(NullLogger<Md3Parser>.Instance);

        Assert.True(parser.CanParse(path.AsMemory()));

        var result = await parser.Parse(new FileAsset(path), default);

        Assert.NotNull(result);
        Assert.All(result, s => Assert.False(s.IsShader));
        Assert.Equal(
        [
            "models/mapobjects/furniture/chairmetal.tga",
            "models/mapobjects/furniture/sherman_s.tga",
        ], result!.Select(s => s.Value.ToString()));
    }
}
