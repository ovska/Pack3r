using Pack3r.Models;

namespace Pack3r.Tests;

public static class ObjectTests
{
    public static IEnumerable<object[]> RelativePathArgs =>
          from trailing in (bool[])[true, false]
          select new object[]
          {
              @"C:\ET\etmain\".TrimEnd(trailing ? Path.DirectorySeparatorChar : '\0'),
              @"C:\ET\etmain\maps\test.map".Replace('\\', Path.DirectorySeparatorChar),
              @"maps\test.map",
          };

    [Theory, MemberData(nameof(RelativePathArgs))]
    public static void Map_Should_Return_Relative_Path(string etmain, string full, string expected)
    {
        var map = new Map
        {
            ETMain = new DirectoryInfo(etmain),
            HasStyleLights = default,
            Name = default!,
            Path = default!,
            Resources = default!,
            Shaders = default!,
        };

        Assert.Equal(
            expected,
            map.RelativePath(full));
    }
}
