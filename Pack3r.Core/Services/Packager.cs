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

        using (var progress = progressManager.Create("Compressing bsp, lightmaps, mapscript etc.", map.RenamableResources.Count))
        {
            int i = 0;
            foreach (var res in map.RenamableResources)
            {
                await CreateRenamable(archive, options, res, cancellationToken);
                includedFiles.Add((res.AbsolutePath, res.ArchivePath.AsMemory()));
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

                // hack
                if (shaderName.EqualsF("noshader"))
                    continue;

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

                foreach (var file in shader.Resources)
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
            var styleShader = new FileInfo(Path.Combine(
                map.GetMapRoot(),
                "scripts",
                $"q3map2_{map.Name}.shader"));

            if (styleShader.Exists)
            {
                logger.CheckAndLogTimestampWarning(
                    $"Stylelight shader ({styleShader.Name})",
                    new FileInfo(Path.ChangeExtension(map.Path, "bsp")),
                    styleShader);
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

        if (map.TryGetAllResources(out var all))
        {
            foreach (var resource in all)
            {
                string title = resource.IsShader ? "shader" : "file";
                string line = resource.Line.HasValue ? $":L{resource.Line}" : "";
                logger.Info($"{resource.Source.NormalizePath()}{line} >> {title} >> {resource.Value}");
            }
        }

        IntegrityChecker.Log(logger);

        // end
        logger.Info($"{includedFiles.Count} files included in pk3");

        bool IsHandledOrExcluded(ReadOnlyMemory<char> relativePath)
        {
            if (handledFiles.Contains(relativePath))
                return true;

            foreach (var source in map.AssetSources)
            {
                if (source.IsPak0 && source.Contains(relativePath))
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
            if (shader.Source is Pk3AssetSource { IsPak0: true })
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

            OnFailedAddFile(false, $"File not found: {relativePath}");
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
        if (!options.DryRun && (required || options.RequireAllAssets))
        {
            logger.Fatal(ref handler);
            throw new ControlledException();
        }
        else
        {
            logger.Error(ref handler);
        }
    }

    private static async ValueTask CreateRenamable(
        ZipArchive archive,
        PackOptions options,
        RenamableResource resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (options.Rename is null || resource.Convert is null)
        {
            archive.CreateEntryFromFile(resource.AbsolutePath, resource.ArchivePath);
            return;
        }

        var entry = archive.CreateEntry(resource.ArchivePath);

        DateTime lastWrite = File.GetLastWriteTime(resource.AbsolutePath);

        // If file to be archived has an invalid last modified time, use the first datetime representable in the Zip timestamp format
        // (midnight on January 1, 1980):
        if (lastWrite.Year < 1980 || lastWrite.Year > 2107)
            lastWrite = new DateTime(1980, 1, 1, 0, 0, 0);

        entry.LastWriteTime = lastWrite;

        using var reader = new StreamReader(resource.AbsolutePath, new FileStreamOptions
        {
            Access = FileAccess.Read,
            Mode = FileMode.Open,
            Share = FileShare.Read,
        });

        await using var writer = new StreamWriter(
            entry.Open(),
            System.Text.Encoding.UTF8,
            leaveOpen: false);

        string? line;

        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            await writer.WriteLineAsync(resource.Convert(line, options).AsMemory(), cancellationToken);
        }
    }
}
