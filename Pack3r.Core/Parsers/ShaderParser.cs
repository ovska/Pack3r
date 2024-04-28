using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Progress;
using Pack3r.Services;

namespace Pack3r.Parsers;

public interface IShaderParser
{
    Task<Dictionary<ReadOnlyMemory<char>, Shader>> GetReferencedShaders(
        PackingData data,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Shader> Parse(
        string path,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Shader> Parse(
        string archivePath,
        ZipArchiveEntry archiveEntry,
        CancellationToken cancellationToken);
}

public class ShaderParser(
    ILineReader reader,
    PackOptions options,
    ILogger<ShaderParser> logger,
    IProgressManager progressManager)
    : IShaderParser
{
    public async Task<Dictionary<ReadOnlyMemory<char>, Shader>> GetReferencedShaders(
        PackingData data,
        CancellationToken cancellationToken)
    {
        var scriptsDirs = data.Map.AssetDirectories
            .Select(a => a.EnumerateDirectories("scripts", SearchOption.TopDirectoryOnly).SingleOrDefault())
            .OfType<DirectoryInfo>()
            .ToList();

        if (scriptsDirs.Count == 0)
        {
            throw new EnvironmentException(
                $"Could not find 'scripts'-folder(s) in: {string.Join(", ", data.Map.AssetDirectories.Select(d => d.FullName))}");
        }

        ConcurrentDictionary<ReadOnlyMemory<char>, Shader> allShaders = new(ROMCharComparer.Instance);

        var whitelist = await ParseShaderLists(scriptsDirs, cancellationToken);
        int shaderFileCount = 0;

        using (var progress = progressManager.Create("Processing shader files", max: null))
        {
            var allShaderFiles = scriptsDirs.SelectMany(dir => dir.EnumerateFiles("*.shader", SearchOption.AllDirectories));

            await Parallel.ForEachAsync(allShaderFiles, cancellationToken, async (file, ct) =>
            {
                int currentCount = Interlocked.Increment(ref shaderFileCount);
                progress.Report(currentCount);

                if (whitelist?.Contains(file.FullName) == false)
                {
                    logger.Debug($"Skipped shader parsing from file {file.Name} (not in shaderlist)");
                    return;
                }

                if (file.Name.Equals("q3shadersCopyForRadiant.shader"))
                {
                    logger.Debug($"Skipped parsing Radiant specific file {file.Name}");
                    return;
                }

                if (file.Name.StartsWith("q3map_"))
                {
                    logger.Debug($"Skipped shader parsing from compiler generated file {file.Name}");
                    return;
                }

                await foreach (var shader in Parse(file.FullName, ct).ConfigureAwait(false))
                {
                    allShaders.AddOrUpdate(
                        shader.Name,
                        static (_, tuple) => tuple.shader,
                        static (_, existing, tuple) =>
                        {
                            var (shader, logger, map) = tuple;

                            // compile time shader, ignore
                            if (!shader.NeededInPk3 && !existing.NeededInPk3)
                                return existing;

                            if (shader.AbsolutePath.EqualsF(existing.AbsolutePath))
                            {
                                logger.Warn($"Shader {shader.Name} found multiple times in file '{shader.AbsolutePath}'");
                                return existing;
                            }

                            var defaultDir = map.AssetDirectories[0].Name;
                            var matchA = shader.AssetDirectory.EqualsF(defaultDir);
                            var matchB = existing.AssetDirectory.EqualsF(defaultDir);

                            if (matchA == matchB)
                            {
                                logger.Warn(
                                    $"Shader {shader.Name} both in file '{shader.AbsolutePath}'" +
                                    $" and '{existing.AbsolutePath}'");
                            }

                            return matchA ? shader : existing;
                        },
                        (shader, logger, data.Map));
                }
            }).ConfigureAwait(false);
        }

        var included = new Dictionary<ReadOnlyMemory<char>, Shader>(ROMCharComparer.Instance);

        AddShaders(
            data,
            data.Map.Shaders.GetEnumerator(),
            allShaders,
            included,
            cancellationToken);

        return included;
    }

    private void AddShaders<TEnumerator>(
        PackingData data,
        TEnumerator enumerator,
        ConcurrentDictionary<ReadOnlyMemory<char>, Shader> allShaders,
        Dictionary<ReadOnlyMemory<char>, Shader> included,
        CancellationToken cancellationToken)
        where TEnumerator : IEnumerator<ReadOnlyMemory<char>>
    {
        cancellationToken.ThrowIfCancellationRequested();

        while (enumerator.MoveNext())
        {
            ReadOnlyMemory<char> name = enumerator.Current;

            if (data.Pak0.Shaders.Contains(name))
                continue;

            if (included.ContainsKey(name))
                continue;

            if (!allShaders.TryGetValue(name, out Shader? shader))
            {
                included.Remove(name);
                continue;
            }

            included.Add(name, shader);

            if (shader.Shaders.Count != 0)
            {
                AddShaders(
                    data,
                    shader.Shaders.GetEnumerator(),
                    allShaders,
                    included,
                    cancellationToken);
            }
        }
    }

    public IAsyncEnumerable<Shader> Parse(string path, CancellationToken cancellationToken)
    {
        return ParseCore(
            path,
            null,
            reader.ReadLines(path, default, cancellationToken));
    }

    public IAsyncEnumerable<Shader> Parse(string archivePath, ZipArchiveEntry archiveEntry, CancellationToken cancellationToken)
    {
        return ParseCore(
            Path.Combine(archivePath, archiveEntry.FullName),
            new ArchiveData(archivePath, archiveEntry.FullName),
            reader.ReadLines(archivePath, archiveEntry, default, cancellationToken));
    }

    private async IAsyncEnumerable<Shader> ParseCore(
        string path,
        ArchiveData? archiveData,
        IAsyncEnumerable<Line> lines)
    {
        State state = State.None;

        Shader? shader = null;
        ReadOnlyMemory<char> token;
        bool inComment = false;

        await foreach (var line in lines.ConfigureAwait(false))
        {
            if (inComment)
            {
                if (line.FirstChar == '*' && line.Value.Length == 2 && line.Value.Span[1] == '/')
                {
                    inComment = false;
                }
                continue;
            }

            if (line.FirstChar == '/' && line.Value.Length == 2 && line.Value.Span[1] == '*')
            {
                inComment = true;
                continue;
            }

            if (state == State.AfterShaderName)
            {
                if (!line.IsOpeningBrace)
                {
                    throw new InvalidDataException(
                        $"Expected {{ on line {line.Index} in file '{path}', but line was: {line.Raw}");
                }

                state = State.Shader;
                continue;
            }

            if (state == State.None)
            {
                ReadOnlyMemory<char> shaderName = line.Value;
                State next = State.AfterShaderName;

                if (shaderName.Span.ContainsAny(Tokens.Braces))
                {
                    // handle opening brace left on the wrong line
                    if (shaderName.Span[^1] == '{')
                    {
                        shaderName = shaderName[..^1];
                        next = State.Shader;
                    }

                    if (shaderName.Span.ContainsAny(Tokens.Braces))
                    {
                        throw new InvalidDataException(
                            $"Expected shader name on line {line.Index} in file '{path}', got: '{line.Raw}'");
                    }
                }

                shader = new Shader(shaderName, path, archiveData);
                state = next;
                continue;
            }

            Debug.Assert(shader != null);

            if (state == State.Stage)
            {
                if (line.IsOpeningBrace)
                {
                    throw new InvalidDataException(
                        $"Invalid token '{line.Raw}' on line {line.Index} in file {path}");
                }

                if (line.IsClosingBrace)
                {
                    state = State.Shader;
                    continue;
                }

                // only map, animmap, clampmap and videomap are valid
                if ((line.FirstChar | 0x20) is not ('m' or 'a' or 'c' or 'v'))
                {
                    continue;
                }

                if (line.MatchPrefix("map ", out token) ||
                    line.MatchPrefix("clampMap ", out token))
                {
                    // $lightmap, $whiteimage etc
                    if (token.Span[0] != '$')
                    {
                        shader.Textures.Add(token);
                    }
                }
                else if (line.MatchPrefix("animMap ", out token))
                {
                    // read past the frames-agument
                    if (token.TryReadPastWhitespace(out token))
                    {
                        foreach (var range in token.Split(' '))
                            shader.Textures.Add(token[range]);
                    }
                    else
                    {
                        logger.UnparsableKeyword(path, line.Index, "animMap", line.Raw);
                    }
                }
                else if (line.MatchPrefix("videomap ", out token))
                {
                    shader.Files.Add(token);
                }

                continue;
            }

            if (state == State.Shader)
            {
                if (line.IsOpeningBrace)
                {
                    state = State.Stage;
                    continue;
                }

                if (line.IsClosingBrace)
                {
                    Debug.Assert(shader != null);
                    yield return shader;
                    state = State.None;
                    continue;
                }

                // early exit for known irrelevant keywords
                if (CanSkipShaderDirective(line.Value.Span))
                    continue;

                bool found = false;

                foreach (var prefix in _simpleShaderRefPrefixes)
                {
                    if (line.MatchPrefix(prefix, out token))
                    {
                        if (!token.Span.StartsWith("$", StringComparison.Ordinal))
                            shader.Shaders.Add(token);
                        found = true;
                        break;
                    }
                }

                if (!found && options.DevFiles)
                {
                    foreach (var prefix in _devTexturePrefixes)
                    {
                        if (line.MatchPrefix(prefix, out token))
                        {
                            if (!token.Span.StartsWith("$", StringComparison.Ordinal))
                                shader.Textures.Add(token);
                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                    continue;

                if (line.MatchPrefix("implicit", out token))
                {
                    if (!token.TryReadPastWhitespace(out token))
                    {
                        logger.Warn($"Missing implicit mapping path on line {line.Index} in shader '{shader.Name}' in file '{path}'");
                    }
                    else
                    {
                        if (token.Span.Equals("-", StringComparison.Ordinal))
                        {
                            shader.ImplicitMapping = shader.Name;
                        }
                        else
                        {
                            shader.ImplicitMapping = token;
                        }
                    }
                }
                else if (line.MatchPrefix("skyparms ", out token))
                {
                    if (!token.TryReadUpToWhitespace(out token))
                    {
                        logger.UnparsableKeyword(path, line.Index, "skyparms", line.Raw);
                        continue;
                    }

                    if (token.Span.Equals("-", StringComparison.Ordinal))
                    {
                        token = shader.Name;
                    }

                    foreach (var suffix in _skySuffixes)
                    {
                        shader.Textures.Add($"{token}{suffix}".AsMemory());
                    }
                }
                else if (line.MatchPrefix("sunshader ", out token))
                {
                    shader.Shaders.Add(token);
                }
                else if (line.MatchPrefix("q3map_surfaceModel ", out token))
                {
                    if (token.TryReadUpToWhitespace(out token))
                    {
                        shader.Files.Add(token);
                    }
                    else
                    {
                        logger.UnparsableKeyword(path, line.Index, "q3map_surfaceModel", line.Raw);
                    }
                }
                else if (!shader.HasLightStyles && line.MatchPrefix("q3map_lightstyle ", out _))
                {
                    shader.HasLightStyles = true;
                }
            }
        }

        if (state != State.None)
        {
            logger.Fatal($"Shader '{path}' ended in an invalid state: {state}");
            throw new ControlledException();
        }
    }

    private async Task<HashSet<string>?> ParseShaderLists(
        IEnumerable<DirectoryInfo> scriptsDirs,
        CancellationToken cancellationToken)
    {
        if (!options.ShaderlistOnly)
            return null;

        var allowedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in scriptsDirs)
        {
            try
            {
                var shaderlistPath = Path.Combine(dir.FullName, "shaderlist.txt");

                if (!File.Exists(shaderlistPath))
                {
                    logger.Debug($"Shaderlist skipped, does not exist in {dir.FullName}");
                    continue;
                }

                await foreach (var line in reader.ReadLines(shaderlistPath, default, cancellationToken))
                {
                    allowedFiles.Add(Path.Combine(dir.FullName, $"{line}.shader"));
                }

                return allowedFiles;
            }
            catch (Exception e)
            {
                logger.Exception(e, $"Could not read shaderlist.txt in {dir.FullName}");
                throw new ControlledException();
            }
        }

        return allowedFiles;
    }

    private static readonly ImmutableArray<string> _skySuffixes =
    [
        "_bk", "_dn", "_ft", "_up", "_rt", "_lf"
    ];

    private static readonly ImmutableArray<string> _simpleShaderRefPrefixes =
    [
        "q3map_backShader",
        "q3map_baseShader",
        "q3map_cloneShader",
        "q3map_remapShader",
    ];

    private static readonly ImmutableArray<string> _devTexturePrefixes =
    [
        "q3map_lightImage",
        "qer_editorImage",
        "q3map_normalImage",
    ];

    private bool CanSkipShaderDirective(ReadOnlySpan<char> line)
    {
        // shouldn't happen
        if (line.IsEmpty)
            return true;

        return (line[0] | 0x20) switch
        {
            'q' => !options.DevFiles && line.StartsWith("qer_"),
            's' => line.StartsWith("surfaceparm "),
            'c' => line.StartsWith("cull "),
            'n' => line.StartsWith("nopicmip") || line.StartsWith("nomipmaps"),
            't' => line.StartsWith("tesssize"),
            _ => false,
        };
    }

    private enum State : byte
    {
        /// <summary>Top-level</summary>
        None = 0,

        /// <summary>Shader name read but first brace is not</summary>
        AfterShaderName = 1,

        /// <summary>In shader, e.g. qer_editorimage</summary>
        Shader = 2,

        /// <summary>In a stage e.g. map $lightmap</summary>
        Stage = 3,
    }
}
