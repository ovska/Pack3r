using Pack3r.IO;

namespace Pack3r;

public sealed class Packager(ITempDirectoryProvider directoryProvider)
{
    public async Task CreateZip(
        Map map,
        ICollection<Shader> shaders,
        CancellationToken cancellationToken)
    {
        using var tempDir = directoryProvider.Create();
        var dir = tempDir.Value;

        await Parallel.ForEachAsync(map.Resources, cancellationToken, (r, _) =>
        {
            HandleResource(map.ETMain, dir, r);
            return default;
        });

        foreach (var resource in map.Resources)
        {
            var resourceStr = resource.ToString();

            string? source = null;
            string? destination = null;
        }
    }

    private void HandleResource(DirectoryInfo etmain, DirectoryInfo dir, ReadOnlyMemory<char> resource)
    {
        var resourceStr = resource.ToString();

        var source = Path.GetFullPath(Path.Combine(etmain.FullName, resourceStr));
        var destination = Path.GetFullPath(Path.Combine(dir.FullName, resourceStr));

        try
        {
            File.Copy(source, destination, overwrite: true);
            return;
        }
        catch (FileNotFoundException) { }


    }
}