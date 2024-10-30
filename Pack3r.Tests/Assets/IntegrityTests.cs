using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Services;

namespace Pack3r.Tests.Assets;

public sealed class IntegrityTests
{
    private readonly IntegrityChecker _checker = new(
        NullLogger<IntegrityChecker>.Instance,
        new AppLifetime(NullLogger<AppLifetime>.Instance, null!));

    [Fact]
    public void Should_Verify_Progressive_Jpg()
    {
        _checker.CheckIntegrity(new FileAsset(Path.GetFullPath($"../../../TestData/etmain/textures/integrity/empty.jpg")));
        Assert.Empty(_checker.JPGs);

        var asset1 = new FileAsset(Path.GetFullPath($"../../../TestData/etmain/fileprio_jpg.pk3dir/textures/smiley.jpg"));
        _checker.CheckIntegrity(asset1);
        Assert.Empty(_checker.JPGs);

        // image source: imagekit.io
        var asset2 = new FileAsset(Path.GetFullPath($"../../../TestData/etmain/textures/integrity/progressive.jpg"));
        _checker.CheckIntegrity(asset2);
        Assert.Single(_checker.JPGs);
        Assert.Equal(asset2.FullPath.NormalizePath(), _checker.JPGs.Single());
    }

    [Fact]
    public void Should_Verify_Dangerous_TGA()
    {
        _checker.CheckIntegrity(new FileAsset(Path.GetFullPath($"../../../TestData/etmain/textures/integrity/empty.tga")));
        Assert.Empty(_checker.TGAs);

        var asset1 = new FileAsset(Path.GetFullPath($"../../../TestData/etmain/fileprio_tga.pk3dir/textures/smiley.tga"));
        _checker.CheckIntegrity(asset1);
        Assert.Empty(_checker.TGAs);

        // image modified from: https://www.hourences.com/texturespage/
        var asset2 = new FileAsset(Path.GetFullPath($"../../../TestData/etmain/textures/integrity/grass_dark.tga"));
        _checker.CheckIntegrity(asset2);
        Assert.Single(_checker.TGAs);
        Assert.Equal(asset2.FullPath.NormalizePath(), _checker.TGAs.Single());


    }
}
