using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Parsers;
using Pack3r.Progress;

namespace Pack3r.Services;

public readonly record struct PackResult(int Packed, int Missing, long Bytes)
{
    public override string ToString() => Missing == 0
        ? $"{Packed} files"
        : $"{Packed}/{Packed + Missing} files ({Missing} missing)";

    public string Size()
    {
        const long kilobyte = 1024;
        const long megabyte = 1024 * 1024;
        return Bytes > megabyte
            ? $"{(double)Bytes / megabyte:N} MB"
            : $"{(double)Bytes / kilobyte:N} KB";
    }
}

public sealed class Packager(
    ILogger<Packager> logger,
    PackOptions options,
    IProgressManager progressManager,
    IShaderParser shaderParser,
    IIntegrityChecker integrityChecker)
{
    public async Task<PackResult> CreateZip(
        Map map,
        Stream destination,
        CancellationToken cancellationToken)
    {
        int missingFiles = 0;

        var shadersByName = await shaderParser.GetReferencedShaders(map, cancellationToken);

        using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: false);

        // contains both actual and alternate files added
        HashSet<ReadOnlyMemory<char>> handledFiles = new(ROMCharComparer.Instance);
        HashSet<ReadOnlyMemory<char>> handledShaders = new(ROMCharComparer.Instance);
        List<IncludedFile> includedFiles = [];

        RenamableResource[] renamable = [.. map.RenamableResources];
        string renamableMsg = string.Join(", ", renamable.Select(r => r.Name).OfType<string>().Distinct());

        using (var progress = progressManager.Create($"Compressing {renamableMsg}", renamable.Length))
        {
            int i = 0;
            foreach (var res in renamable)
            {
                CreateRenamable(archive, options, res, cancellationToken);
                progress.Report(++i);

                includedFiles.Add(new IncludedFile(res));
            }
        }

        using (var progress = progressManager.Create("Compressing models, model textures, sounds etc.", map.Resources.Count))
        {
            int count = 1;

            foreach (var resource in map.Resources)
            {
                progress.Report(count++);

                if (IsHandledOrExcluded(resource.Value))
                {
                    continue;
                }

                AddFileRelative(resource.Value, resource);
            }
        }

        bool styleLights = map.HasStyleLights;

        using (var progress = progressManager.Create("Compressing files referenced by shaders", map.Shaders.Count))
        {
            int count = 1;

            foreach (var shaderResource in map.Shaders)
            {
                progress.Report(count++);

                var shaderName = shaderResource.Value;

                // hack
                if (shaderName.EqualsF("noshader"))
                    continue;

                // already handled
                if (!handledShaders.Add(shaderName))
                    continue;

                if (!shadersByName.TryGetValue(shaderName, out Shader? shader))
                {
                    // might just be a texture without a shader
                    AddFileRelative(shaderName, shaderResource);
                    continue;
                }

                styleLights = styleLights || shader.HasLightStyles;

                if (!shader.NeededInPk3)
                    continue;

                if (!handledFiles.Contains(shader.DestinationPath.AsMemory()))
                    AddShaderFile(shader, shaderResource);

                if (shader.ImplicitMapping is { } implicitMapping)
                {
                    AddFileRelative(implicitMapping, shaderResource, shader);
                }

                foreach (var file in shader.Resources)
                {
                    if (IsHandledOrExcluded(file))
                        continue;

                    AddFileRelative(file, shaderResource, shader);
                }

                foreach (var file in shader.DevResources)
                {
                    if (IsHandledOrExcluded(file))
                        continue;

                    AddFileRelative(file, shaderResource, shader, devResource: true);
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

        if (options.LogLevel >= LogLevel.Info && options.ReferenceDebug)
        {
            foreach (var file in includedFiles.OrderBy(x => x.ArchivePath, ROMCharComparer.Instance))
            {
                var lineref = file.ReferencedLine != null ? $" line {file.ReferencedLine}" : "";
                var shaderref = file.Shader != null
                    ? $" shader '{file.Shader.DestinationPath}' line {file.Shader.Line} in"
                    : "";
                var fileref = file.ReferencedIn != null
                    ? $" (referenced in{shaderref} file: '{map.GetRelativeToRoot(file.ReferencedIn)}'{lineref})"
                    : "";
                var srcOnly = file.SourceOnly ? " (source only)" : "";
                var src = file.Source != null
                    ? file.Source.Name
                    : $"'{map.GetRelativeToRoot(file.SourcePath.ToString()).NormalizePath()}'";

                logger.Info($"File packed: {file.ArchivePath} from {src}{fileref}{srcOnly}");
            }
        }

        integrityChecker.Log();

        // end
        logger.Info($"{includedFiles.Count} files included in pk3");

        return new PackResult(Packed: includedFiles.Count, Missing: missingFiles, Bytes: destination.Position);

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
                archivePath: map.GetArchivePath(absolutePath).NormalizePath(),
                absolutePath))
            {
                OnFailedAddFile(required: true, $"File '{absolutePath}' not found");
            }
        }

        void AddShaderFile(Shader shader, Resource resource)
        {
            if (shader.Source is Pk3AssetSource { IsExcluded: true })
                return;

            if (TryAddFileFromSource(shader.Source, shader.DestinationPath.AsMemory(), resource, shader))
                return;

            OnFailedAddFile(false, $"Shader file '{shader.GetAbsolutePath()}' not found");
        }

        void AddFileRelative(ReadOnlyMemory<char> relativePath, Resource resource, Shader? shader = null, bool devResource = false)
        {
            foreach (var source in map.AssetSources)
            {
                if (TryAddFileFromSource(source, relativePath, resource, shader))
                    return;
            }

            string sourceOnly = devResource ? " (source file)" : "";
            OnFailedAddFile(false, $"{(resource.IsShader ? "Shader" : "File")} not found: {relativePath}{sourceOnly}");
        }

        bool TryAddFileFromSource(AssetSource source, ReadOnlyMemory<char> relativePath, Resource resource, Shader? shader = null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                ZipArchiveEntry? entry;

                if (shader is not null &&
                    map.ShaderConvert.Count > 0 &&
                    map.ShaderConvert.TryGetValue(shader.Asset, out var convertList))
                {
                    if (!source.Assets.TryGetValue(relativePath, out IAsset? asset))
                        return false;

                    entry = CreateRenamableShader(archive, asset, convertList, cancellationToken);
                }
                else
                {
                    if (!source.TryHandleAsset(archive, relativePath, resource, out entry))
                        return false;
                }

                handledFiles.Add(relativePath);

                if (entry is not null)
                {
                    includedFiles.Add(new IncludedFile(source, relativePath, resource, shader));
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
                includedFiles.Add(new IncludedFile(sourcePath: absolutePath.AsMemory(), archivePath: archivePath.AsMemory()));
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

        void OnFailedAddFile(bool required, ref DefaultInterpolatedStringHandler handler, bool devResource = false)
        {
            missingFiles++;

            if (!devResource && !options.DryRun && (required || options.RequireAllAssets))
            {
                logger.Fatal(ref handler);
                throw new ControlledException();
            }
            else
            {
                logger.Error(ref handler);
            }
        }
    }

    private static ZipArchiveEntry CreateRenamableShader(
        ZipArchive archive,
        IAsset asset,
        List<Func<string, int, string>> convertList,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(asset.Name, CompressionLevel.Optimal);

        using var reader = new StreamReader(asset.OpenRead(), Encoding.UTF8);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);

        string? line;
        int index = 1;

        ReadOnlySpan<Func<string, int, string>> span = CollectionsMarshal.AsSpan(convertList);

        while ((line = reader.ReadLine()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (var func in span)
            {
                line = func(line, index);
            }

            writer.WriteLine(line);
            index++;
        }

        return entry;
    }

    private static void CreateRenamable(
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

        using var reader = new StreamReader(File.OpenRead(resource.AbsolutePath), Encoding.UTF8, leaveOpen: false);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8, leaveOpen: false);

        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteLine(resource.Convert(line, options));
        }
    }
}
