namespace Pack3r.Tests;

public static class TokensTests
{
    [Theory]
    [InlineData("set", true)]
    [InlineData("create", true)]
    [InlineData("alertentity ent", false)]
    [InlineData("playsound filter.wav", false)]
    public static void Should_Match_Set_And_Create(string input, bool expected)
    {
        if (expected)
        {
            Assert.Matches(Tokens.UnsupportedMapscript(), input);
        }
        else
        {
            Assert.DoesNotMatch(Tokens.UnsupportedMapscript(), input);
        }
    }

    [Theory, MemberData(nameof(Files))]
    public static void Should_Match_Files(string path, bool expected)
    {
        if (expected)
        {
            Assert.Matches(Tokens.PackableFile(), path);
        }
        else
        {
            Assert.DoesNotMatch(Tokens.PackableFile(), path);
        }
    }

    public static TheoryData<string, bool> Files => new()
    {
        { "animations/human/base/akimbo.mdx", false },
        { "animations/scripts/human_base.script", false },
        { "botfiles/chars.h", false },
        { "botfiles/fw_items.c", false },
        { "characters/temperate/allied/cvops.char", false },
        { "fonts/ariblk_0_16.tga", true },
        { "gfx/2d/backtile.jpg", true },
        { "gfx/2d/backtile.jpeg", true },
        { "maps/battery.bsp", false },
        { "maps/battery.objdata", false },
        { "models/ammo/grenade1.mdc", true },
        { "models/mapobjects/blitz_sd/blitzbody.md3", true },
        { "models/mapobjects/blitz_sd/blitzbody.shadow", false },
        { "models/mapobjects/blitz_sd/blitzbody.tag", false },
        { "models/mapobjects/cmarker/allied_cflag.skin", true },
        { "models/players/temperate/allied/cvops/body.mdm", true },
        { "scripts/alpha.shader", true },
        { "scripts/battery.arena", false },
        { "scripts/centraleurope.campaign", false },
        { "scripts/wm_allies_chat.voice", false },
        { "sound/chat/allies/10a.wav", true },
        { "sound/maps/battery.sps", false },
        { "sound/scripts/battery.sounds", false },
        { "ui/credits_activision.menu", false },
        { "weapons/adrenaline.weap", false },
        { "textures/video/flame.roq", true },
        { "somepath/model.obj", true },
        { "somepath/model.fbx", true },
        { "somepath/model.ase", true },
    };
}
