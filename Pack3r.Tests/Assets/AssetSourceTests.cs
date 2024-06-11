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
        ReadOnlyMemory<char> none = "textures/smiley".AsMemory();
        ReadOnlyMemory<char> jpg = "textures/smiley.jpg".AsMemory();
        ReadOnlyMemory<char> tga = "textures/smiley.tga".AsMemory();

        var jpgSrc = GetSource("fileprio_jpg");
        Assert.Equal(2, jpgSrc.Assets.Count);
        AssertHas(jpgSrc, jpg, jpg);
        AssertHas(jpgSrc, none, jpg);
        Assert.DoesNotContain(tga, jpgSrc.Assets); // can't downcast jpg in 2.60b

        var tgaSrc = GetSource("fileprio_tga");
        Assert.Equal(3, tgaSrc.Assets.Count);
        AssertHas(tgaSrc, tga, tga);
        AssertHas(tgaSrc, jpg, tga);
        AssertHas(tgaSrc, none, tga);

        var bothSrc = GetSource("fileprio_both");
        Assert.Equal(3, bothSrc.Assets.Count);
        AssertHas(bothSrc, tga, tga);
        AssertHas(bothSrc, jpg, jpg);
        AssertHas(bothSrc, none, tga); // tga takes prio

        static void AssertHas(
            AssetSource source,
            ReadOnlyMemory<char> key,
            ReadOnlyMemory<char> expected)
        {
            Assert.Contains(key, source.Assets);
            Assert.Equal(expected.ToString(), source.Assets[key].Name);
        }
    }
}
