using System.IO.Compression;
using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pack3r.Extensions;

namespace Pack3r;

public sealed class Packager(
    ILogger<Packager> logger,
    IOptions<PackOptions> options,
    IShaderParser shaderParser)
{
    public async Task CreateZip(
        PackingData data,
        Stream destination,
        CancellationToken cancellationToken)
    {
        // start parsing shaders in the background
        var shaderParseTask = shaderParser.GetReferencedShaders(data, cancellationToken);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: false);

        // contains both actual and alternate files added
        HashSet<ReadOnlyMemory<char>> addedFiles = new(ROMCharComparer.Instance);
        HashSet<ReadOnlyMemory<char>> handledShaders = new(ROMCharComparer.Instance);

        Map map = data.Map;

        if (options.Value.DevFiles)
            AddFileAbsolute(map.Path, required: true);

        var bsp = new FileInfo(Path.ChangeExtension(map.Path, "bsp"));
        AddFileAbsolute(bsp.FullName, required: true);

        var lightmapDir = new DirectoryInfo(Path.ChangeExtension(map.Path, null));

        if (lightmapDir.Exists)
        {
            // TODO: check bsp timestamps
            foreach (var file in lightmapDir.GetFiles("lm_????.tga"))
            {
                AddFileAbsolute(file.FullName, required: true);
            }
        }

        foreach (var resource in data.Map.Resources)
        {
            if (data.Pak0.Resources.Contains(resource))
                continue;

            if (addedFiles.Contains(resource))
                continue;

            AddFileRelative(resource);
        }

        bool styleLights = map.HasStyleLights;
        var shadersByName = await shaderParseTask;

        char[] buffer = new char[1024];

        foreach (var shaderName in data.Map.Shaders)
        {
            if (data.Pak0.Shaders.Contains(shaderName))
                continue;

            // already handled
            if (!handledShaders.Add(shaderName))
                continue;

            if (!shadersByName.TryGetValue(shaderName, out Shader? shader))
            {
                // might just be a texture without a shader
                AddTexture(shaderName.ToString());
                continue;
            }

            styleLights = styleLights || shader.HasLightStyles;

            if (shader.Path.Entry is not null)
                ThrowHelper.ThrowNotSupportedException($"Can't include file from pk3: {shader.Path.Entry}");

            if (!addedFiles.Contains(shader.Path.Path.AsMemory()))
                AddFileAbsolute(shader.Path.Path);

            if (shader.ImplicitMapping is { } implicitMapping)
            {
                AddTexture(implicitMapping.ToString());
            }

            foreach (var file in shader.Files)
            {
                if (data.Pak0.Resources.Contains(file) || addedFiles.Contains(file))
                    continue;

                AddFileRelative(file);
            }

            foreach (var file in shader.Textures)
            {
                if (data.Pak0.Resources.Contains(file) || addedFiles.Contains(file))
                    continue;

                AddTexture(file.ToString());
            }
        }

        void AddFileRelative(ReadOnlyMemory<char> relativePath, bool required = false)
        {
            var asString = relativePath.ToString();
            AddFile(absolute: Path.Combine(map.ETMain.FullName, asString), relative: asString, required: required);
        }

        void AddFileAbsolute(string absolutePath, bool required = false)
        {
            AddFile(absolutePath, map.RelativePath(absolutePath), required);
        }

        void AddFile(
            string absolute,
            string relative,
            bool required = false)
        {
            try
            {
                archive.CreateEntryFromFile(sourceFileName: absolute, entryName: relative);
                addedFiles.Add(absolute.AsMemory());
                return;
            }
            catch (FileNotFoundException)
            {
            }

            if (!required && options.Value.RequireAllAssets)
            {
                logger.LogCritical("File {file} not found", relative);
                throw new ControlledException();
            }
            else
            {
                logger.LogError("File {file} not found", relative);
                // not added
            }
        }

        bool TryAddRelative(string relative)
        {
            try
            {
                var absolute = Path.Combine(map.ETMain.FullName, relative);
                archive.CreateEntryFromFile(sourceFileName: absolute, entryName: relative);
                addedFiles.Add(absolute.AsMemory());
                return true;
            }
            catch (DirectoryNotFoundException) { return false; }
            catch (FileNotFoundException) { return false; }
        }

        void AddTexture(string name)
        {
            ReadOnlySpan<char> extension = Path.GetExtension(name.AsSpan());

            if (extension.IsEmpty)
            {
                goto TryAddTga;
            }

            if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                goto TryAddTga;
            }

            if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                goto TryAddJpeg;
            }

            goto Fail;

            TryAddTga:
            var tga = Path.ChangeExtension(name, ".tga");

            if (data.Pak0.Resources.Contains(tga.AsMemory()) || addedFiles.Contains(tga.AsMemory()))
            {
                return;
            }

            if (TryAddRelative(tga))
            {
                // consider the original texture added
                addedFiles.Add(name.AsMemory());
                return;
            }

            TryAddJpeg:
            var jpg = Path.ChangeExtension(name, ".jpg");

            if (data.Pak0.Resources.Contains(jpg.AsMemory()) || addedFiles.Contains(jpg.AsMemory()))
            {
                return;
            }

            if (TryAddRelative(jpg))
            {
                // consider the texture added
                addedFiles.Add(name.AsMemory());
                return;
            }

            Fail:
            logger.Log(
                options.Value.RequireAllAssets ? LogLevel.Critical : LogLevel.Error,
                "Shader/texture {name} not found",
                name);

            if (options.Value.RequireAllAssets)
            {
                throw new ControlledException();
            }
        }
    }

    public string ResolveDestinationPath(Map map, string? destination, string? rename)
    {
        if (destination is null)
        {
            destination = Path.ChangeExtension(map.Path, "pk3");
            logger.LogDebug("No destination file supplied, defaulting to {path}", destination);
        }

        // is a directory?
        if (Path.GetExtension(destination.AsSpan()).IsEmpty)
        {
            destination = Path.ChangeExtension(Path.Combine(destination, map.Name), "pk3");
            logger.LogDebug("Destination path is a directory, using path: {path}", destination);
        }

        // relative path?
        if (!Path.IsPathRooted(destination))
        {
            destination = Path.GetFullPath(new Uri(destination).LocalPath);
            logger.LogDebug("Destination resolved to full path {path}", destination);
        }

        if (!string.IsNullOrEmpty(rename))
        {
            // TODO
            throw new NotSupportedException();
        }

        return destination;
    }

    private enum AddResult : byte { NotAdded, Exact, Alternate }
}
