using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
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
        Map map = data.Map;

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: false);

        var shadersByName = await shaderParser.GetReferencedShaders(data, cancellationToken);

        // contains both actual and alternate files added
        HashSet<ReadOnlyMemory<char>> addedFiles = new(ROMCharComparer.Instance);
        HashSet<ReadOnlyMemory<char>> handledShaders = new(ROMCharComparer.Instance);
        List<(string absolute, string archive)> includedFiles = [];

        FileInfo bsp = new(Path.ChangeExtension(map.Path, "bsp"));
        AddCompileFile(bsp.FullName);

        if (options.DevFiles)
        {
            AddCompileFile(map.Path);
        }

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
                AddCompileFile(file.FullName);
                progress.Report(i + 1);
                includedLightmaps = true;
            }
        }
        else
        {
            logger.Debug($"Lightmaps skipped, files not found in '{lightmapDir.FullName}'");
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

                AddFileRelative(resource.ToString());
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

                if (!shader.NeededInPk3)
                    continue;

                if (!addedFiles.Contains(shader.ArchivePath.AsMemory()))
                    AddShaderFile(shader);

                if (shader.ImplicitMapping is { } implicitMapping)
                {
                    AddTexture(implicitMapping.ToString());
                }

                foreach (var file in shader.Files)
                {
                    if (data.Pak0.Resources.Contains(file) || addedFiles.Contains(file))
                        continue;

                    AddFileRelative(file.ToString());
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
            var styleShader = new FileInfo(Path.Combine(map.AssetDirectories[0].FullName, "scripts", $"q3map2_{map.Name}.shader"));

            if (styleShader.Exists)
            {
                logger.CheckAndLogTimestampWarning("Stylelight shader", bsp, styleShader);
                AddCompileFile(styleShader.FullName);
            }
            else
            {
                logger.Warn($"Map has style lights, but shader file '{styleShader.FullName}' was not found");
            }
        }

        if (options.LogLevel == LogLevel.Debug)
        {
            foreach (var (abs, arch) in includedFiles.Order())
            {
                logger.Debug($"File packed: '{arch}' (from: {abs})");
            }
        }

        // end
        logger.Info($"{includedFiles.Count} files included in pk3");

        void AddCompileFile(string absolutePath)
        {
            if (!TryAddFileCore(
                archivePath: map.GetArchivePath(absolutePath),
                absolutePath))
            {
                OnFailedAddFile(required: true, $"File '{absolutePath}' not found");
            }
        }

        void AddShaderFile(Shader shader)
        {
            if (!TryAddFileCore(
                archivePath: shader.ArchivePath,
                absolutePath: shader.Path.Path))
            {
                OnFailedAddFile(false, $"Shader file '{shader.Path}' not found");
            }
        }

        void AddFileRelative(string relative)
        {
            foreach (var dir in map.AssetDirectories)
            {
                if (TryAddFileCore(relative, Path.Combine(dir.FullName, relative)))
                    return;
            }

            OnFailedAddFile(false, $"File '{relative}' not found in etmain or pk3dirs");
        }

        bool TryAddFileCore(
            string archivePath,
            string absolutePath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(absolutePath))
            {
                Exception ex;

                try
                {
                    archive!.CreateEntryFromFile(sourceFileName: absolutePath, entryName: archivePath);
                    addedFiles.Add(archivePath.AsMemory());
                    includedFiles.Add((absolutePath, archivePath));
                    return true;
                }
                catch (IOException ioex) { ex = ioex; }

                if (options.LogLevel == LogLevel.Trace)
                {
                    logger.Exception(ex, "Failed to copy an existing file");
                }
                else
                {
                    // should be rare
                    logger.Error($"Failed to pack file '{absolutePath}':{Environment.NewLine}{ex.Message}");
                }
            }
            else
            {
                logger.Trace($"File not found: '{absolutePath}' (pk3 path: '{archivePath}')");
            }

            return false;
        }

        bool TryAddRelative(string relative)
        {
            foreach (var dir in map.AssetDirectories)
            {
                if (TryAddFileCore(
                    archivePath: relative,
                    absolutePath: Path.Combine(dir.FullName, relative)))
                {
                    return true;
                }
            }

            return false;
        }

        void AddTexture(string name)
        {
            bool tgaAttempted = false;

            TextureExtension extension = name.GetTextureExtension();

            if (extension is TextureExtension.Empty or TextureExtension.Tga)
            {
                goto TryAddTga;
            }
            else if (extension is TextureExtension.Jpg)
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
            OnFailedAddFile(false, $"Missing texture reference{detail}: {name}");
        }
    }

    private void OnFailedAddFile(bool required, ref DefaultInterpolatedStringHandler handler)
    {
        if (required || options.RequireAllAssets)
        {
            logger.Fatal(ref handler);
            throw new ControlledException();
        }
        else
        {
            logger.Error(ref handler);
        }
    }

    private readonly struct PackingPath
    {
        public static PackingPath CreateAbsolute(string value) => new(value, null);
        public static PackingPath CreateRelative(string value) => new(null, value);

        public string? Absolute { get; }
        public string? Relative { get; }

        private PackingPath(string? absolute, string? relative)
        {
            Absolute = absolute;
            Relative = relative;
        }

        [MemberNotNullWhen(true, nameof(Relative))]
        [MemberNotNullWhen(false, nameof(Absolute))]
        public bool IsRelative => Relative is not null;
    }
}
