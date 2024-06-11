using Pack3r.IO;

namespace Pack3r.Tests.Assets;

public static class AssetSourceTests
{
    private static DirectoryAssetSource GetSource(string name)
    {
        var source = new DirectoryAssetSource(new DirectoryInfo(Path.GetFullPath($"../../../TestData/etmain/{name}.pk3dir")), false);
        Assert.Equal($"{name}.pk3dir", source.Name);
        return source;
    }

    [Fact]
    public static void Should_Resolve_Textures()
    {
        QPath none = "textures/smiley";
        QPath jpg = "textures/smiley.jpg";
        QPath tga = "textures/smiley.tga";

        var jpgSrc = GetSource("fileprio_jpg");
        Assert.Equal(3, jpgSrc.Assets.Count);
        AssertHas(jpgSrc, jpg, jpg);
        AssertHas(jpgSrc, tga, jpg);
        AssertHas(jpgSrc, none, jpg);

        var tgaSrc = GetSource("fileprio_tga");
        Assert.Equal(2, tgaSrc.Assets.Count);
        AssertHas(tgaSrc, tga, tga);
        AssertHas(tgaSrc, none, tga);
        Assert.DoesNotContain(jpg, tgaSrc.Assets);

        var bothSrc = GetSource("fileprio_both");
        Assert.Equal(3, bothSrc.Assets.Count);
        AssertHas(bothSrc, tga, tga);
        AssertHas(bothSrc, jpg, jpg);
        AssertHas(bothSrc, none, tga); // tga takes prio

        static void AssertHas(
            AssetSource source,
            QPath key,
            QPath expected)
        {
            Assert.Contains(key, source.Assets);
            Assert.Equal(expected, source.Assets[key].Name);
        }
    }
}
