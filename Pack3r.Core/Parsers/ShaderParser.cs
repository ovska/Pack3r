using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pack3r.Extensions;
using Pack3r.IO;

namespace Pack3r;

public readonly struct ResourcePath : IEquatable<ResourcePath>
{
    public string Path { get; }
    public ZipArchiveEntry? Entry { get; }

    public ResourcePath(string archivePath, ZipArchiveEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(entry);

        Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(archivePath, entry.FullName));
        Entry = entry;
    }

    public ResourcePath(string absolutePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        Path = absolutePath;
    }

    public static implicit operator ResourcePath(string path) => new(path);
    public static implicit operator string(ResourcePath resource) => resource.Path;

    public bool Equals(ResourcePath other) => Path.Equals(other.Path);

    public override bool Equals(object? obj) => obj is ResourcePath resourcePath && Equals(resourcePath);

    public override int GetHashCode() => Path.GetHashCode();
}

public interface IShaderParser
{
    Task<Dictionary<ReadOnlyMemory<char>, Shader>> GetReferencedShaders(
        PackingData data,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Shader> Parse(
        ResourcePath path,
        CancellationToken cancellationToken);
}

public class ShaderParser(
    ILineReader reader,
    IOptions<PackOptions> options,
    ILogger<ShaderParser> logger)
    : IShaderParser
{
    private readonly bool _includeDevFiles = options.Value.DevFiles;

    public async Task<Dictionary<ReadOnlyMemory<char>, Shader>> GetReferencedShaders(
        PackingData data,
        CancellationToken cancellationToken)
    {
        var scriptsDir = data.Map.ETMain
            .GetDirectories("scripts")
            .SingleOrDefault();

        if (scriptsDir is not { Exists: true })
            throw new InvalidOperationException($"Could not find 'scripts'-folder in {data.Map.ETMain.FullName}");

        HashSet<string>? shaderlist = options.Value.ShaderlistOnly
            ? await ReadShaderlist(scriptsDir.FullName, cancellationToken)
            : null;

        ConcurrentDictionary<ReadOnlyMemory<char>, Shader> allShaders = new(ROMCharComparer.Instance);

        int counter = 0;

        await Parallel.ForEachAsync(scriptsDir.GetFiles("*.shader"), cancellationToken, async (file, ct) =>
        {
            if (shaderlist?.Contains(Path.GetFileNameWithoutExtension(file.Name)) == false)
            {
                logger.LogDebug(
                    "Skipping shader parsing from file {fileName} (not in shaderlist)",
                    file.Name);
                return;
            }

            if (file.Name.StartsWith("q3map_") || file.Name.Equals("q3shadersCopyForRadiant.shader"))
            {
                logger.LogDebug(
                    "Skipping shader parsing from compiler generated file {fileName}",
                    file.Name);
                return;
            }

            Interlocked.Increment(ref counter);

            await foreach (var shader in Parse(file.FullName, ct).ConfigureAwait(false))
            {
                if (!allShaders.TryAdd(shader.Name, shader))
                {
                    var existing = allShaders[shader.Name];

                    if (!shader.NeededInPk3 && !existing.NeededInPk3)
                        continue;

                    if (shader.Equals(existing))
                    {
                        logger.LogWarning(
                            "Shader {name} found multiple times in file '{file}'",
                            shader.Name,
                            data.Map.RelativePath(shader.Path.Path));
                    }
                    else
                    {
                        logger.LogWarning(
                            "Shader {name} both in file '{existing}' and '{current}'",
                            shader.Name,
                            data.Map.RelativePath(shader.Path.Path),
                            data.Map.RelativePath(existing.Path.Path));
                    }
                }
            }
        }).ConfigureAwait(false);

        logger.LogInformation("Parsed {shaders} shaders from {files} .shader-files", allShaders.Count, counter);

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

    public async IAsyncEnumerable<Shader> Parse(
        ResourcePath path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        State state = State.None;

        Shader? shader = null;
        ReadOnlyMemory<char> token;
        bool inComment = false;

        await foreach (var line in reader.ReadLines(path, default, cancellationToken).ConfigureAwait(false))
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
                    logger.ExpectedOpeningBrace(path, line.Index, line.Raw);
                    throw new InvalidDataException();
                }

                state = State.Shader;
                continue;
            }

            if (state == State.None)
            {
                if (line.Value.Span.ContainsAny(Tokens.Braces))
                {
                    logger.ExpectedShader(path, line.Index, line.Raw);
                    throw new InvalidDataException();
                }

                shader = new Shader(path, line.Value);
                state = State.AfterShaderName;
                continue;
            }

            Debug.Assert(shader != null);

            if (state == State.Stage)
            {
                if (line.IsOpeningBrace)
                {
                    logger.InvalidToken(path, line.Index, line.Raw);
                    throw new InvalidDataException();
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

                if (!found && _includeDevFiles)
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
                        logger.LogError(
                            "Missing implicit mapping path on line {line} in shader '{shader}' in file '{path}'",
                            line.Index,
                            shader.Name,
                            path);
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
            logger.LogCritical(
                "Shader '{path}' ended in an invalid state: {state}",
                path,
                state);
            throw new InvalidDataException();
        }
    }

    private async Task<HashSet<string>> ReadShaderlist(string scriptsDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var shaderlist = new HashSet<string>();
            var shaderlistPath = Path.Combine(scriptsDirectory, "shaderlist.txt");
            await foreach (var line in reader.ReadLines(shaderlistPath, default, cancellationToken))
            {
                shaderlist.Add(line.Value.ToString());
            }
            return shaderlist;
        }
        catch (Exception e)
        {
            logger.LogCritical(e, "Could not read shaderlist.txt in {scripts}", scriptsDirectory);
            throw;
        }
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
            'q' => !_includeDevFiles && line.StartsWith("qer_"),
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
