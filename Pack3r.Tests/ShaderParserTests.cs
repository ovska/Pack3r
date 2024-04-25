using Microsoft.Extensions.Logging.Abstractions;

namespace Pack3r.Tests;

public class ShaderParserTests
{
    private static async Task<Shader> ParseSingle(string data, bool includeDevFiles = false)
    {
        var parser = GetParser(data, includeDevFiles);
        var results = await parser.Parse("Ö:\\test.map", default).ToList();

        Assert.Single(results);
        return results[0];
    }

    private static ShaderParser GetParser(
        string data,
        bool includeDevFiles = false)
    {
        var reader = new StringLineReader(data);
        return new ShaderParser(
            reader,
            new PackOptions { DevFiles = includeDevFiles },
            NullLogger<ShaderParser>.Instance);
    }

    [Fact]
    public async Task Should_Parse_Multiple_Shaders()
    {
        var parser = GetParser("""
            textures/pgm/bar1
            {
            	surfaceparm metalsteps
            	implicitMap textures/pgm/bar1.tga
            }

            textures/pgm/bar2
            {
            	surfaceparm metalsteps
            	implicitMap textures/pgm/bar2.tga
            }
            """);

        var results = await parser.Parse("Ö:\\test.map", default).ToList();

        Assert.Equal(2, results.Count);

        Assert.Equal(
            ["textures/pgm/bar1", "textures/pgm/bar2"],
            results.Select(r => r.Name.ToString()));

        foreach (var shader in results)
        {
            Assert.Single(shader.Textures);
            Assert.Equal(Path.ChangeExtension(shader.Name.ToString(), "tga"), shader.Textures[0].ToString());

            Assert.Empty(shader.Files);
            Assert.Empty(shader.Shaders);
        }
    }

    [Fact]
    public async Task Should_Parse_Implicit_Shader()
    {
        var shader = await ParseSingle("""
            textures/pgm/light_rec_blu_5000
            {
            	qer_editorimage textures/pgm/abal2.tga
            	surfaceparm metalsteps
            	q3map_surfacelight 5000
            	q3map_shadeangle 46
            	q3map_lightimage textures/pgm/ei/light_blue.tga
            	implicitMap	textures/pgm/abal2.tga
            }
            """);

        Assert.Equal("textures/pgm/light_rec_blu_5000", shader.Name.ToString());
        Assert.Empty(shader.Shaders);
        Assert.Empty(shader.Files);

        Assert.Single(shader.Textures);
        Assert.Equal("textures/pgm/abal2.tga", shader.Textures[0].ToString());
    }

    [Fact]
    public async Task Should_Parse_Shader()
    {
        var shader = await ParseSingle("""
            // this is a comment
            textures/common/clipweap_glass
            {
            	qer_editorImage textures/common/clipweapglass.tga
            	qer_trans 0.3
            	surfaceparm glass
            	surfaceparm nodraw
            	surfaceparm nomarks
            	surfaceparm trans
            }
            """);

        Assert.Equal("textures/common/clipweap_glass", shader.Name.ToString());
        Assert.Empty(shader.Shaders);
        Assert.Empty(shader.Files);
        Assert.Empty(shader.Textures);
    }

    [Fact]
    public async Task Should_Parse_Sky()
    {
        var shader = await ParseSingle("""
            textures/pgm/sky
            {
            	// skpk/qer/env/cs/desert194.tga
            	qer_editorImage textures/pgm/sky/desert194_ft.tga
            	q3map_lightimage textures/pgm/sky/desert194_up.tga
            	surfaceparm noimpact
            	surfaceparm nolightmap
            	surfaceparm sky
            	// q3map_sunExt 1 1 1 85 6 31 2 16
            	// q3map_skylight 70 4
            	q3map_sunExt 1 1 1 100 6 31 2 16
            	q3map_skylight 100 4
            	q3map_lightmapFilterRadius 0 8
            	nopicmip
            	skyparms textures/pgm/sky/desert194 - -
            }
            """);

        Assert.Equal("textures/pgm/sky", shader.Name.ToString());
        Assert.Empty(shader.Shaders);
        Assert.Empty(shader.Files);

        Assert.Equal(
            [
                "textures/pgm/sky/desert194_bk",
                "textures/pgm/sky/desert194_dn",
                "textures/pgm/sky/desert194_ft",
                "textures/pgm/sky/desert194_lf",
                "textures/pgm/sky/desert194_rt",
                "textures/pgm/sky/desert194_up",
            ],
            shader.Textures.AsStrings().Order());
    }

    [Fact]
    public async Task Should_Parse_AnimMap()
    {
        var shader = await ParseSingle("""
            textures/sfx/wilsflame1
            {
            	qer_editorimage textures/sfx/flame1.tga
            	q3map_surfacelight 1482
            	cull none
            	nofog
            	surfaceparm nomarks
            	surfaceparm nonsolid
            	surfaceparm pointlight
            	surfaceparm trans
            	{
            		animMap 10 textures/sfx/flame1.tga textures/sfx/flame2.tga textures/sfx/flame3.tga textures/sfx/flame4.tga textures/sfx/flame5.tga textures/sfx/flame6.tga textures/sfx/flame7.tga textures/sfx/flame8.tga
            		blendFunc GL_ONE GL_ONE
            		rgbGen wave inverseSawtooth 0 1 0 10
            	}
            	{
            		animMap 10 textures/sfx/flame2.tga textures/sfx/flame3.tga textures/sfx/flame4.tga textures/sfx/flame5.tga textures/sfx/flame6.tga textures/sfx/flame7.tga textures/sfx/flame8.tga textures/sfx/flame1.tga
            		blendFunc GL_ONE GL_ONE
            		rgbGen wave sawtooth 0 1 0 10
            	}
            	{
            		map textures/sfx/flameball.tga
            		blendFunc GL_ONE GL_ONE
            		rgbGen wave sin .6 .2 0 .6
            	}
            }
            """);

        Assert.Equal(
            [
                "textures/sfx/flame1.tga",
                "textures/sfx/flame2.tga",
                "textures/sfx/flame3.tga",
                "textures/sfx/flame4.tga",
                "textures/sfx/flame5.tga",
                "textures/sfx/flame6.tga",
                "textures/sfx/flame7.tga",
                "textures/sfx/flame8.tga",
                "textures/sfx/flameball.tga",
            ],
            shader.Textures.AsStrings().Order().Distinct());
    }

    [Fact]
    public async Task Should_Parse_Complex_Shader()
    {
        var shader = await ParseSingle("""
            textures/pgm/ice_floor1
            {
            	qer_editorimage textures/pgm/ice_floor1.jpg
            	q3map_lightImage textures/pgm/ice_floor1.jpg	
            	q3map_surfaceModel models/pgm/ice_haze.md3 64 0.8 0.2 0.4 0 359 1
            	surfaceparm slick
            	surfaceparm glass
            	q3map_surfacelight 250
            	q3map_backSplash 5 64
            	tessSize 256

            	{
            		map $lightmap
            		rgbGen identity
            	}
            	{
                    map textures/pgm/ice_floor1.jpg	
            		blendFunc GL_DST_COLOR GL_ZERO
            	    rgbGen identity
            	}
            	{
            		map textures/pgm/iceoverlay.tga
            		blendfunc GL_DST_COLOR GL_SRC_ALPHA
            		tcMod scale 2 2 
            		alphagen const 0.7
            	}
            	{
            		map textures/pgm/ice_effect.tga
            		tcgen environment
            		blendfunc add
            	}
            }
            """);

        Assert.Equal("textures/pgm/ice_floor1", shader.Name.ToString());
        Assert.Empty(shader.Shaders);

        Assert.Equal(
            [
                "textures/pgm/ice_floor1.jpg",
                "textures/pgm/iceoverlay.tga",
                "textures/pgm/ice_effect.tga",
            ],
            shader.Textures.AsStrings());

        Assert.Equal(
            [
                "models/pgm/ice_haze.md3",
            ],
            shader.Files.AsStrings());
    }
}