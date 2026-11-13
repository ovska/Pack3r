using System.Text;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;

namespace Pack3r.Tests;

public sealed class ShaderResolutionTests : IDisposable
{
    private readonly DirectoryInfo _etmain = Directory.CreateTempSubdirectory("pack3r_shaders_").CreateSubdirectory("etmain");

    public void Dispose() => _etmain.Parent!.Delete(recursive: true);

    [Fact]
    public void Should_Resolve_Duplicates_In_File_Order_Regardless_Of_Parse_Time()
    {
        var scripts = _etmain.CreateSubdirectory("scripts");
        _etmain.CreateSubdirectory("maps");

        // the same shader in three files of one source, each pointing to a different texture
        foreach (var name in new[] { "alpha", "bravo", "charlie" })
        {
            File.WriteAllText(
                Path.Combine(scripts.FullName, $"{name}.shader"),
                $"textures/test/dupe\n{{\n\t{{\n\t\tmap textures/test/{name}.tga\n\t}}\n}}\n");
        }

        // shader files are parsed in parallel, but the first file in the order the source lists them must win,
        // as before. pad that file so it is the last one to finish parsing
        var first = new DirectoryAssetSource(_etmain, notPacked: false)
            .EnumerateShaderFiles(_ => false)
            .First();

        var padding = new StringBuilder();

        for (int i = 0; i < 20_000; i++)
        {
            padding.Append($"textures/test/filler_{i}\n{{\n\t{{\n\t\tmap textures/test/filler_{i}.tga\n\t}}\n}}\n");
        }

        File.AppendAllText(first.FullPath, padding.ToString());
        string expected = $"textures/test/{Path.GetFileNameWithoutExtension(first.FullPath)}.tga";

        for (int attempt = 0; attempt < 10; attempt++)
        {
            Assert.Equal(expected, ResolveDupe());
        }
    }

    private string ResolveDupe()
    {
        var options = new PackOptions
        {
            MapFile = new FileInfo(Path.Combine(_etmain.FullName, "maps", "test.map")),
            UnpackedSources = [],
            UnscannedSources = [],
            ModFolders = [],
        };

        using var map = new Map(options)
        {
            Name = "test",
            Path = options.MapFile.FullName,
            ETMain = _etmain,
            Shaders = [Resource.Shader("textures/test/dupe", new Line(options.MapFile.FullName, 1, "", keepRaw: true))],
            Resources = [],
            ReferenceResources = [],
            MiscModels = [],
            HasStyleLights = false,
        };

        var parser = new ShaderParser(
            new FSLineReader(),
            options,
            NullLogger<ShaderParser>.Instance,
            new NoOpProgressManager());

        Dictionary<QPath, Shader> shaders = parser.GetReferencedShaders(map, default);
        return Assert.Single(shaders["textures/test/dupe"].Resources).ToString();
    }
}
