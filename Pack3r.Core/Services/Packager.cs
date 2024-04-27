using System;
using System.Diagnostics;
using System.IO.Compression;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Progress;
using Pack3r.Services;

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
        var includedLightmaps = false;

        if (lightmapDir.Exists && lightmapDir.GetFiles("lm_????.tga") is { Length: > 0 } lmFiles)
        {
            bool timestampWarned = false;
            using var progress = progressManager.Create("Packing lightmaps", lmFiles.Length);

            for (int i = 0; i < lmFiles.Length; i++)
            {
                FileInfo? file = lmFiles[i];
                timestampWarned = timestampWarned || logger.CheckAndLogTimestampWarning("Lightmap", bsp, file);
                AddFileAbsolute(file.FullName, required: true);
                progress.Report(i + 1);
                includedLightmaps = true;
            }
        }
        else
        {
            logger.Debug($"Lightmaps skipped, files not found in '{map.RelativePath(lightmapDir.FullName)}'");
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

        // most likely no recent light compile if there are no lightmaps
        if (styleLights && includedLightmaps)
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

        if (options.LogLevel == LogLevel.Debug)
        {
            includedFiles.Sort();

            foreach (var file in includedFiles)
            {
                logger.Debug($"File included: {file.Replace('\\', '/')}");
            }
        }

        // end
        logger.Info($"{includedFiles.Count} files included in pk3");

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
                includedFiles.Add(relative);
                return;
            }
            catch (DirectoryNotFoundException) { }
            catch (FileNotFoundException) { }

            if (required || options.RequireAllAssets)
            {
                logger.Fatal($"File '{relative}' not found in path: {absolute}");
                throw new ControlledException();
            }
            else
            {
                logger.Error($"File {relative} not found");
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
                includedFiles.Add(relative);
                return true;
            }
            catch (DirectoryNotFoundException) { return false; }
            catch (FileNotFoundException) { return false; }
        }

        void AddTexture(string name)
        {
            bool tgaAttempted = false;
            ReadOnlySpan<char> extension = Path.GetExtension(name.AsSpan());

            if (extension.IsEmpty ||
                extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            {
                goto TryAddTga;
            }
            else if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            {
                goto TryAddJpeg;
            }

            goto Fail;

            TryAddTga:
            string tga = Path.ChangeExtension(name, ".tga");
            tgaAttempted = true;

            if (data.Pak0.Resources.Contains(tga.AsMemory()) || addedFiles.Contains(tga.AsMemory()))
            {
                return;
            }

            if (TryAddRelative(tga))
            {
                // consider the original texture added as well
                addedFiles.Add(name.AsMemory());
                return;
            }

            TryAddJpeg:
            string jpg = Path.ChangeExtension(name, ".jpg");

            if (data.Pak0.Resources.Contains(jpg.AsMemory()) || addedFiles.Contains(jpg.AsMemory()))
            {
                return;
            }

            if (TryAddRelative(jpg))
            {
                // consider the original texture added as well
                addedFiles.Add(name.AsMemory());
                return;
            }

            Fail:
            string detail = tgaAttempted ? " (no .tga or .jpg found)" : "";
            logger.Log(
                options.MissingAssetLoglevel,
                $"Missing rexture reference{detail}: {name}");

            if (options.RequireAllAssets)
            {
                throw new ControlledException();
            }
        }
    }
}
