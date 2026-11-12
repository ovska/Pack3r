using System.IO.Compression;
using System.Runtime.CompilerServices;
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

        // entries are compressed on worker threads while the assets are being resolved here
        await using var archive = new Pk3Writer(
            destination,
            comment: $"Created with Pack3r {Global.Version}",
            compressionLevel: options.SmallestSize ? CompressionLevel.SmallestSize : CompressionLevel.Optimal,
            cancellationToken: cancellationToken);

        // contains both actual and alternate files added
        HashSet<QPath> handledFiles = [];
        HashSet<QPath> handledShaders = [];
        List<IncludedFile> includedFiles = [];

        RenamableResource[] renamable = [.. map.RenamableResources];
        string renamableMsg = string.Join(", ", renamable.Select(r => r.Name).OfType<string>().Distinct());

        using (var progress = progressManager.Create($"Compressing {renamableMsg}", renamable.Length))
        {
            int i = 0;
            foreach (var res in renamable)
            {
                CreateRenamable(archive, options, res, cancellationToken);
                handledFiles.Add(res.ArchivePath);
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

        using (var progress = progressManager.Create("Compressing shaders and textures", map.Shaders.Count))
        {
            int count = 1;

            foreach (var shaderResource in map.Shaders)
            {
                progress.Report(count++);

                QPath shaderName = shaderResource.Value;

                // hack
                if (shaderName.Equals("noshader"))
                    continue;

                HandleShaderRecursive(shaderName);

                void HandleShaderRecursive(QPath shaderName)
                {
                    // already handled
                    if (!handledShaders.Add(shaderName))
                        return;

                    if (!shadersByName.TryGetValue(shaderName, out Shader? shader))
                    {
                        // might just be a texture without a shader
                        AddFileRelative(shaderName, shaderResource);
                        return;
                    }

                    styleLights = styleLights || shader.HasLightStyles;

                    if (!shader.NeededInPk3)
                        return;

                    if (!handledFiles.Contains(shader.DestinationPath.AsMemory()))
                        AddShaderFile(shader, shaderResource);

                    if (shader.ImplicitMapping is { } implicitMapping && !IsHandledOrExcluded(implicitMapping))
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

                    foreach (var inner in shader.Shaders)
                    {
                        HandleShaderRecursive(inner);
                    }
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

                if (string.IsNullOrEmpty(options.Rename))
                {
                    AddCompileFile(styleShader.FullName);
                }
                else
                {
                    AddRenamedStylelightShader(archive, styleShader, map, options.Rename, cancellationToken);
                }
            }
            else
            {
                logger.Warn($"Map has style lights, but shader file '{styleShader.FullName}' was not found");
            }
        }

        long archiveSize;

        using (var progress = progressManager.Create("Finishing compression", archive.EntriesAdded))
        {
            Task<long> completion = archive.CompleteAsync();

            while (!completion.IsCompleted)
            {
                progress.Report(archive.EntriesWritten);
                await Task.WhenAny(completion, Task.Delay(100, CancellationToken.None));
            }

            archiveSize = await completion;
            progress.Report(archive.EntriesAdded);
        }

        if (options.LogLevel <= LogLevel.Info && options.ReferenceDebug)
        {
            foreach (var file in includedFiles.OrderBy(x => x.ArchivePath))
            {
                var shaderref = file.Shader != null
                    ? $" shader '{file.Shader.DestinationPath}' line {file.Shader.Line} in"
                    : "";
                var fileref = file.Reference is not null
                    ? $" (referenced in{shaderref} file: {file.Reference.Format(map)})"
                    : "";
                var srcOnly = file.SourceOnly ? " (source only)" : "";
                var src = file.Source != null
                    ? file.Source.Name
                    : $"'{map.GetRelativeToRoot(file.SourcePath.ToString()).NormalizePath()}'";

                logger.Info($"File packed: {file.ArchivePath} from {src}{fileref}{srcOnly}");
            }
        }

        integrityChecker.Log();

        return new PackResult(
            Packed: includedFiles.Count,
            Missing: missingFiles,
            Bytes: archiveSize);

        bool IsHandledOrExcluded(QPath relativePath)
        {
            if (handledFiles.Contains(relativePath))
                return true;

            foreach (var source in map.AssetSources)
            {
                if (source.NotPacked && source.Assets.ContainsKey(relativePath))
                    return true;
            }

            return false;
        }

        void AddCompileFile(string absolutePath)
        {
            if (!TryAddFileAbsolute(
                archivePath: Path.GetRelativePath(map.ETMain.FullName, absolutePath).NormalizePath(),
                absolutePath))
            {
                OnFailedAddFile(required: true, $"File '{absolutePath}' not found");
            }
        }

        void AddShaderFile(Shader shader, Resource resource)
        {
            if (shader.Source.NotPacked)
            {
                handledFiles.Add(shader.DestinationPath);
                return;
            }

            foreach (var source in map.AssetSources)
            {
                if (source.NotPacked &&
                    source.Assets.ContainsKey(shader.DestinationPath))
                {
                    if (options.ShaderDebug)
                    {
                        logger.Info($"Shader not packed, also present in {source.Name}: '{shader.GetAbsolutePath()}'");
                    }

                    handledFiles.Add(shader.DestinationPath);
                    return;
                }
            }

            if (TryAddFileFromSource(shader.Source, shader.DestinationPath.AsMemory(), resource, shader))
                return;

            OnFailedAddFile(false, $"Shader file '{shader.GetAbsolutePath()}' not found");
        }

        void AddFileRelative(QPath relativePath, Resource resource, Shader? shader = null, bool devResource = false)
        {
            foreach (var source in map.AssetSources)
            {
                if (TryAddFileFromSource(source, relativePath, resource, shader, devResource))
                    return;
            }

            if (map.IsRegionCompile && resource.IsShader && resource.Value.Equals("textures/NULL"))
            {
                logger.Warn($"Region compile shader 'textures/NULL' not found, but was included in {resource.Source.Format(map)}");
                return;
            }

            if (options.OnlySource && resource.SourceOnly)
                devResource = true;

            string suffix = "";

            if (shader is not null && relativePath.Span.GetTextureExtension() is TextureExtension.Jpg)
            {
                foreach (var src in map.AssetSources)
                {
                    if (src.Assets.ContainsKey(Path.ChangeExtension(relativePath.ToString(), ".tga")))
                    {
                        suffix = $" TGA was found, do you need to fix the shader? (in: {src.Name})";
                        break;
                    }
                }
            }

            string sourceOnly = devResource ? " (source file)" : "";
            string referencedIn = $"(referenced in {resource.Source.Format(map)})";
            OnFailedAddFile(
                false,
                $"Asset not found: {relativePath}{sourceOnly} {referencedIn}{suffix}",
                devResource);
        }

        bool TryAddFileFromSource(
            AssetSource source,
            QPath relativePath,
            Resource resource,
            Shader? shader = null,
            bool devResource = false)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!source.Assets.TryGetValue(relativePath, out IAsset? asset))
                return false;

            bool added = handledFiles.Add(asset.Name) | handledFiles.Add(relativePath);

            // already added via tga/jpg downcasting?
            if (!added)
                return false;

            try
            {
                string? entry;

                if (shader is not null &&
                    map.ShaderConvert.Count > 0 &&
                    map.ShaderConvert.TryGetValue(shader.Asset, out var convertList))
                {
                    if (!shader.Asset.Name.EqualsF(relativePath))
                        convertList = [];

                    // ugly hack to rename levelshots
                    string archivePath = shader.Asset.Name.EqualsF($"scripts/levelshots_{map.Name}.shader")
                        ? $"scripts/levelshots_{options.Rename ?? map.Name}.shader"
                        : shader.Asset.Name;

                    CreateRenamableShader(archive, asset, archivePath, convertList, cancellationToken);
                    entry = archivePath;
                }
                else
                {
                    if (source.NotPacked)
                    {
                        // file found from an excluded source
                        entry = null;
                    }
                    else
                    {
                        if (!resource.SourceOnly && !devResource)
                            integrityChecker.CheckIntegrity(asset);

                        asset.AddTo(archive);
                        entry = asset.Name;
                    }
                }

                if (entry is not null)
                {
                    includedFiles.Add(new IncludedFile(
                        source,
                        entry,
                        resource,
                        shader,
                        devResource));
                }

                return true;
            }
            catch (IOException ex)
            {
                handledFiles.Remove(relativePath);
                handledFiles.Remove(asset.Name);

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
                archive.Add(archivePath, File.GetLastWriteTime(absolutePath), OpenFile(absolutePath));
                handledFiles.Add(archivePath.AsMemory());
                includedFiles.Add(new IncludedFile(sourcePath: absolutePath, archivePath: archivePath));
                return true;
            }
            catch (IOException ioex) { ex = ioex; }

            if (options.LogLevel == LogLevel.Trace)
            {
                logger.Exception(ex, $"Failed to pack file '{archivePath}' from path: '{absolutePath}'");
            }
            else
            {
                logger.Error($"Failed to pack file '{archivePath}' from path: '{absolutePath}' (use Trace verbosity for details)");
            }

            return false;
        }

        void OnFailedAddFile(bool required, ref DefaultInterpolatedStringHandler handler, bool devResource = false)
        {
            missingFiles++;

            if (!devResource && !options.DryRun && (required || options.RequireAllAssets))
            {
                if (!required && options.RequireAllAssets)
                {
                    handler = $"{handler.ToStringAndClear()}\r\n        Use --loose to ignore missing files)";
                }

                logger.Fatal(ref handler);
                throw new ControlledException();
            }

            logger.Log(devResource || !options.RequireAllAssets ? LogLevel.Warn : LogLevel.Error, ref handler);
        }
    }

    private static void CreateRenamableShader(
        Pk3Writer archive,
        IAsset asset,
        string archivePath,
        List<Func<string, int, string>> convertList,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (convertList.Count == 0)
        {
            asset.AddTo(archive, archivePath);
            return;
        }

        // the conversion runs on a compression thread
        Func<string, int, string>[] converters = [.. convertList];

        asset.AddTo(archive, archivePath, (input, output) =>
        {
            using var reader = new StreamReader(input, Encoding.UTF8);
            using var writer = new StreamWriter(output, Encoding.UTF8);

            string? line;
            int index = 1;

            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var func in converters)
                {
                    line = func(line, index);
                }

                writer.WriteLine(line);
                index++;
            }
        });
    }

    private static void CreateRenamable(
        Pk3Writer archive,
        PackOptions options,
        RenamableResource resource,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        DateTime lastWrite = File.GetLastWriteTime(resource.AbsolutePath);

        if (options.Rename is null || resource.Convert is null)
        {
            archive.Add(resource.ArchivePath, lastWrite, OpenFile(resource.AbsolutePath));
            return;
        }

        var convert = resource.Convert;

        // the conversion runs on a compression thread
        archive.Add(resource.ArchivePath, lastWrite, OpenFile(resource.AbsolutePath), (input, output) =>
        {
            using var reader = new StreamReader(
                input,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: false);

            using var writer = new StreamWriter(output, Encoding.UTF8, leaveOpen: false);

            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteLine(convert(line, options));
            }
        });
    }

    private static FileStream OpenFile(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.SequentialScan);

    private static void AddRenamedStylelightShader(
        Pk3Writer archive,
        FileInfo stylelightShaderFile,
        Map map,
        string rename,
        CancellationToken cancellationToken)
    {
        DirectoryAssetSource rootSource = map
            .AssetSources
            .OfType<DirectoryAssetSource>()
            .Single(src => src.Directory.FullName == map.GetMapRoot());

        string needle = $"\t\tmap maps/{map.Name}/lm_";
        string replacement = $"\t\tmap maps/{rename}/lm_";
        CreateRenamableShader(
            archive,
            new FileAsset(rootSource, stylelightShaderFile),
            archivePath: $"scripts/q3map2_{rename}.shader",
            [
                (string line, int _) =>
                {
                    if (line.StartsWith(needle))
                    {
                        return line.Replace(needle, replacement) + " " + Global.Disclaimer;
                    }

                    return line;
                }
            ],
            cancellationToken);
    }
}
