using System;
using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;

namespace Pack3r;

public sealed class Packager(
    ILogger<Packager> logger,
    PackOptions options,
    IProgressManager progressManager,
    IShaderParser shaderParser)
{
    public async Task CreateZip(
        PackingData data,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: false);

        var shadersByName = await shaderParser.GetReferencedShaders(data, cancellationToken);

        // contains both actual and alternate files added
        HashSet<ReadOnlyMemory<char>> addedFiles = new(ROMCharComparer.Instance);
        HashSet<ReadOnlyMemory<char>> handledShaders = new(ROMCharComparer.Instance);
        List<string> includedFiles = [];

        Map map = data.Map;

        if (options.DevFiles)
        {
            AddFileAbsolute(map.Path, required: true);
        }

        var bsp = new FileInfo(Path.ChangeExtension(map.Path, "bsp"));
        AddFileAbsolute(bsp.FullName, required: true);

        var lightmapDir = new DirectoryInfo(Path.ChangeExtension(map.Path, null));

        if (lightmapDir.Exists)
        {
            bool timestampWarned = false;

            var files = lightmapDir.GetFiles("lm_????.tga");
            using var progress = progressManager.Create("Packing lightmaps", files.Length);

            for (int i = 0; i < files.Length; i++)
            {
                FileInfo? file = files[i];
                timestampWarned = timestampWarned || logger.CheckAndLogTimestampWarning("Lightmap", bsp, file);
                AddFileAbsolute(file.FullName, required: true);
                progress.Report(i + 1);
            }
        }

        using (var progress = progressManager.Create("Packing resources", data.Map.Resources.Count))
        {
            int count = 1;

            foreach (var resource in data.Map.Resources)
            {
                progress.Report(count++);

                if (data.Pak0.Resources.Contains(resource) || addedFiles.Contains(resource))
                {
                    continue;
                }

                AddFileRelative(resource);
            }
        }

        bool styleLights = map.HasStyleLights;

        using (var progress = progressManager.Create("Packing files referenced by shaders", data.Map.Shaders.Count))
        {
            int count = 1;

            foreach (var shaderName in data.Map.Shaders)
            {
                progress.Report(count++);

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
                {
                    throw new UnreachableException($"Can't include file from pk3: {shader.Path.Entry}");
                }

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
        }

        if (styleLights)
        {
            var styleShader = Path.Combine("scripts", $"q3map_{map.Name}.shader");
            var file = new FileInfo(styleShader);

            if (file.Exists)
            {
                logger.CheckAndLogTimestampWarning("Stylelight shader", bsp, file);
                AddFileRelative(styleShader.AsMemory());
            }
            else
            {
                logger.Warn($"Map has style lights, but shader file {styleShader} was not found");
            }
        }

        if (includedFiles.Count > 0)
        {
            includedFiles.Sort();

            foreach (var file in includedFiles)
            {
                logger.Debug($"File included: {file.Replace('\\', '/')}");
            }
        }

        // end

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
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                archive.CreateEntryFromFile(sourceFileName: absolute, entryName: relative);
                addedFiles.Add(absolute.AsMemory());

                if (options.LogLevel == LogLevel.Debug)
                    includedFiles.Add(relative);
                return;
            }
            catch (FileNotFoundException)
            {
            }

            if (!required && options.RequireAllAssets)
            {
                logger.Fatal($"File {relative} not found");
                throw new ControlledException();
            }
            else
            {
                logger.Error($"File {relative} not found");
                // not added
            }
        }

        bool TryAddRelative(string relative)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var absolute = Path.Combine(map.ETMain.FullName, relative);
                archive.CreateEntryFromFile(sourceFileName: absolute, entryName: relative);
                addedFiles.Add(absolute.AsMemory());

                if (options.LogLevel == LogLevel.Debug)
                    includedFiles.Add(relative);

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
            string type = extension.IsEmpty ? "shader or texture" : "texture";
            logger.Log(
                options.RequireAllAssets ? LogLevel.Fatal : LogLevel.Error,
                $"Missing {type}: {name}");

            if (options.RequireAllAssets)
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
            logger.Debug($"No destination file supplied, defaulting to: {destination}");
        }

        // is a directory?
        if (Path.GetExtension(destination.AsSpan()).IsEmpty)
        {
            destination = Path.ChangeExtension(Path.Combine(destination, map.Name), "pk3");
            logger.Debug($"Destination path is a directory, using path: {destination}");
        }

        // relative path?
        if (!Path.IsPathRooted(destination))
        {
            destination = Path.GetFullPath(new Uri(destination).LocalPath);
            logger.Debug($"Destination resolved to full path: {destination}");
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
