using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Progress;

namespace Pack3r.Services;

public sealed class Packager(
    ILogger<Packager> logger,
    PackOptions options,
    IProgressManager progressManager,
    IShaderParser shaderParser)
{
    public async Task CreateZip(
        Map map,
        Stream destination,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: false);

        var shadersByName = await shaderParser.GetReferencedShaders(map, cancellationToken);

        // contains both actual and alternate files added
        HashSet<ReadOnlyMemory<char>> handledFiles = new(ROMCharComparer.Instance);
        HashSet<ReadOnlyMemory<char>> handledShaders = new(ROMCharComparer.Instance);
        List<(string source, ReadOnlyMemory<char> relative)> includedFiles = [];

        FileInfo bsp = new(Path.ChangeExtension(map.Path, "bsp"));
        AddCompileFile(bsp.FullName);

        if (options.DevFiles)
        {
            AddCompileFile(map.Path);
        }

        using (var progress = progressManager.Create("Compressing bsp, lightmaps, mapscript etc.", map.RenamableResources.Count))
        {
            int i = 0;
            foreach (var (absolutePath, archivePath) in map.RenamableResources)
            {
                archive.CreateEntryFromFile(absolutePath, archivePath);
                progress.Report(++i);
            }
        }

        using (var progress = progressManager.Create("Compressing resources", map.Resources.Count))
        {
            int count = 1;

            foreach (var resource in map.Resources)
            {
                progress.Report(count++);

                if (IsHandledOrExcluded(resource))
                {
                    continue;
                }

                AddFileRelative(resource);
            }
        }

        bool styleLights = map.HasStyleLights;

        using (var progress = progressManager.Create("Compressing files referenced by shaders", map.Shaders.Count))
        {
            int count = 1;

            foreach (var shaderName in map.Shaders)
            {
                progress.Report(count++);

                // TODO
                // if (data.Pak0.Shaders.Contains(shaderName))
                //     continue;

                // already handled
                if (!handledShaders.Add(shaderName))
                    continue;

                if (!shadersByName.TryGetValue(shaderName, out Shader? shader))
                {
                    // might just be a texture without a shader
                    AddFileRelative(shaderName);
                    continue;
                }

                styleLights = styleLights || shader.HasLightStyles;

                if (!shader.NeededInPk3)
                    continue;

                if (!handledFiles.Contains(shader.DestinationPath.AsMemory()))
                    AddShaderFile(shader);

                if (shader.ImplicitMapping is { } implicitMapping)
                {
                    AddFileRelative(implicitMapping);
                }

                foreach (var file in shader.Files)
                {
                    if (IsHandledOrExcluded(file))
                        continue;

                    AddFileRelative(file);
                }

                foreach (var file in shader.Textures)
                {
                    if (IsHandledOrExcluded(file))
                        continue;

                    AddFileRelative(file);
                }
            }
        }

        // most likely no recent light compile if there are no lightmaps
        if (styleLights && map.HasLightmaps)
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
            foreach (var (abs, arch) in includedFiles.OrderBy(x => x.relative, ROMCharComparer.Instance))
            {
                logger.Debug($"File packed: '{arch}' from '{abs}'");
            }
        }

        // end
        logger.Info($"{includedFiles.Count} files included in pk3");

        bool IsHandledOrExcluded(ReadOnlyMemory<char> relativePath)
        {
            if (handledFiles.Contains(relativePath))
                return true;

            foreach (var source in map.AssetSources)
            {
                if (source.IsExcluded && source.Contains(relativePath))
                    return true;
            }

            return false;
        }

        void AddCompileFile(string absolutePath)
        {
            if (!TryAddFileAbsolute(
                archivePath: map.GetArchivePath(absolutePath),
                absolutePath))
            {
                OnFailedAddFile(required: true, $"File '{absolutePath}' not found");
            }
        }

        void AddShaderFile(Shader shader)
        {
            if (shader.Source is Pk3AssetSource { IsExcluded: true })
                return;

            if (TryAddFileFromSource(shader.Source, shader.DestinationPath.AsMemory()))
                return;

            OnFailedAddFile(false, $"Shader file '{shader.GetAbsolutePath()}' not found");
        }

        void AddFileRelative(ReadOnlyMemory<char> relativePath)
        {
            foreach (var source in map.AssetSources)
            {
                if (TryAddFileFromSource(source, relativePath))
                    return;
            }

            OnFailedAddFile(false, $"File '{relativePath}' not found in etmain or pk3dirs");
        }

        bool TryAddFileFromSource(AssetSource source, ReadOnlyMemory<char> relativePath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (!source.TryHandleAsset(archive, relativePath, out var entry))
                    return false;

                handledFiles.Add(relativePath);

                if (entry is not null)
                {
                    includedFiles.Add((source.ToString()!, relativePath));
                }

                return true;
            }
            catch (IOException ex)
            {
                if (options.LogLevel == LogLevel.Trace)
                {
                    logger.Exception(ex, $"Failed to pack file '{relativePath}' from source {source}");
                }
                else
                {
                    logger.Error($"Failed to pack file '{relativePath}' from source {source}");
                }

                return false;
            }
        }

        bool TryAddFileAbsolute(string archivePath, string absolutePath)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(absolutePath))
            {
                return false;
            }

            Exception ex;

            try
            {
                archive.CreateEntryFromFile(absolutePath, archivePath);
                handledFiles.Add(archivePath.AsMemory());
                includedFiles.Add((absolutePath, archivePath.AsMemory()));
                return true;
            }
            catch (IOException ioex) { ex = ioex; }

            if (options.LogLevel == LogLevel.Trace)
            {
                logger.Exception(ex, $"Failed to pack file '{archivePath}' from path: '{absolutePath}'");
            }
            else
            {
                logger.Error($"Failed to pack file '{archivePath}' from path: '{absolutePath}'");
            }

            return false;
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
