﻿using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using Pack3r.Progress;

namespace Pack3r.Parsers;

public interface IShaderParser
{
    Task<Dictionary<QPath, Shader>> GetReferencedShaders(
        Map map,
        CancellationToken cancellationToken);

    IAsyncEnumerable<Shader> Parse(
        IAsset asset,
        CancellationToken cancellationToken);
}

public class ShaderParser(
    ILineReader reader,
    PackOptions options,
    ILogger<ShaderParser> logger,
    IProgressManager progressManager)
    : IShaderParser
{
    public async Task<Dictionary<QPath, Shader>> GetReferencedShaders(
        Map map,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<QPath, Shader> allShaders = [];
        ConcurrentDictionary<QPath, List<Shader>> duplicateShaders = [];

        int shaderFileCount = 0;

        using (var progress = progressManager.Create("Parsing shader files", max: null))
        {
            await Parallel.ForEachAsync(map.AssetSources, Global.ParallelOptions(cancellationToken), async (source, ct) =>
            {
                bool skipPredicate(string name)
                {
                    progress.Report(Interlocked.Increment(ref shaderFileCount));

                    string fileName = Path.GetFileNameWithoutExtension(name);

                    if (fileName.EqualsF("q3shadersCopyForRadiant"))
                    {
                        if (options.ShaderDebug)
                            logger.Debug($"Skipped parsing Radiant specific shaderfile {getName()}");
                        return true;
                    }

                    if (fileName.StartsWithF("q3map_") || fileName.StartsWithF("q3map2_"))
                    {
                        if (options.ShaderDebug)
                            logger.Debug($"Skipped shaderfile parsing from compiler generated file '{getName()}'");
                        return true;
                    }

                    return false;

                    string getName() => Path.Combine(source.RootPath, "scripts", name).NormalizePath();
                }

                await foreach (var shader in source.EnumerateShaders(this, skipPredicate, ct))
                {
                    allShaders.AddOrUpdate(
                        key: shader.Name,
                        addValueFactory: static (_, tuple) => tuple.shader,
                        updateValueFactory: static (_, a, tuple) =>
                        {
                            var (b, logger, map, duplicate, options) = tuple;

                            int cmp = map.AssetSources.IndexOf(a.Source).CompareTo(map.AssetSources.IndexOf(b.Source));

                            if (cmp != 0)
                            {
                                var toReturn = cmp > 0 ? a : b;
                                var other = cmp > 0 ? b : a;

                                if (options.ShaderDebug)
                                {
                                    logger.Debug($"Shader {a.Name} resolved from '{toReturn.Source}' instead of '{other.Source}'");
                                }

                                return toReturn;
                            }

                            // cases such as common shaders, if they are compile only we don't care
                            if (!options.OnlySource && (!a.NeededInPk3 || !b.NeededInPk3))
                            {
                                return a.NeededInPk3 ? b : a;
                            }

                            if (ReferenceEquals(a.Asset, b.Asset))
                            {
                                // not packed, don't care
                                if (a.Source.NotPacked) return a;

                                // ET uses the first shader in the file in case of duplicates
                                var first = a.Line < b.Line ? a : b;

                                if (options.ShaderDebug)
                                {
                                    logger.Debug(
                                        $"Shader {first} found multiple times in {first.Asset.FullPath}, " +
                                        $"using the first from line {first.Line}");
                                }

                                return first;
                            }

                            if (!a.Source.NotPacked || !b.Source.NotPacked)
                            {
                                duplicate.AddOrUpdate(
                                    key: a.Name,
                                    addValueFactory: static (_, arg) => [arg.a, arg.b],
                                    updateValueFactory: static (_, existing, arg) => [arg.a, arg.b, .. existing],
                                    (a, b));
                            }
                            return a;
                        },
                        factoryArgument: (shader, logger, map, duplicateShaders, options));
                }
            }).ConfigureAwait(false);
        }

        logger.Debug($"Parsed total of {allShaders.Count} shaders");

        Dictionary<QPath, Shader> included = [];

        AddShaders(
            map,
            map.Shaders.Select(r => (QPath)r.Value),
            allShaders,
            duplicateShaders,
            included,
            0,
            cancellationToken);

        foreach (var (name, duplicates) in duplicateShaders)
        {
            if (duplicates.DistinctBy(s => s.Asset).Count() == 1)
            {
                logger.Warn($"Shader '{name}' multiple times in file: {duplicates[0].GetAbsolutePath()}");
            }
            else
            {
                var display = string.Join("\n\t", duplicates.Select(s => $"'{s.GetAbsolutePath()}'"));
                logger.Warn($"Shader '{name}' found in multiple files:\n\t{display}");
            }
        }

        return included;
    }

    private void AddShaders(
        Map map,
        IEnumerable<QPath> shaders,
        ConcurrentDictionary<QPath, Shader> allShaders,
        ConcurrentDictionary<QPath, List<Shader>> duplicateShaders,
        Dictionary<QPath, Shader> included,
        int depth,
        CancellationToken cancellationToken)
    {
        if (depth == 0)
        {
            HandleCC("automap");
            HandleCC("trans");
        }

        foreach (var name in shaders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (included.ContainsKey(name))
                continue;

            if (!allShaders.TryGetValue(name, out Shader? shader))
            {
                included.Remove(name);
                continue;
            }

            included.Add(name, shader);

            if (duplicateShaders.TryRemove(name, out List<Shader>? duplicates))
            {
                var display = string.Join(", ", duplicates.Select(s => $"'{s.GetAbsolutePath()}'"));
                logger.Warn($"Shader '{name}' found in multiple sources: {display}");
            }

            if (shader.Shaders.Count != 0)
            {
                AddShaders(
                    map,
                    shader.Shaders,
                    allShaders,
                    duplicateShaders,
                    included,
                    depth + 1,
                    cancellationToken);
            }
        }

        void HandleCC(string type)
        {
            var name = $"levelshots/{map.Name}_cc_{type}".AsMemory();

            if (allShaders.TryGetValue(name, out Shader? shader))
            {
                included.Add(name, shader);
                map.Shaders.Add(new Resource(name, isShader: true, new Line(map.Path, -1, "", true), sourceOnly: false));

                if (options.Rename is not null)
                {
                    logger.Info($"Packed shader '{name}' on line {shader.Line} in '{shader.Asset.Name}' will be modified to account for --rename");

                    if (!map.ShaderConvert.TryGetValue(shader.Asset, out var list))
                    {
                        map.ShaderConvert[shader.Asset] = list = [];
                    }

                    list.Add((string line, int index) =>
                    {
                        if (index == shader.Line)
                        {
                            return $"levelshots/{options.Rename}_cc_{type} {Global.Disclaimer}, was: {line}";
                        }

                        return line;
                    });
                }
            }
        }
    }

    public async IAsyncEnumerable<Shader> Parse(
        IAsset asset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        State state = State.None;

        Shader? shader = null;
        ReadOnlyMemory<char> token;
        bool inComment = false;

        await foreach (var _line in reader.ReadLines(asset, cancellationToken).ConfigureAwait(false))
        {
            Line line = _line;

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
                        $"Expected {{ on line {line.Index} in file '{asset.FullPath}', but line was: {line.Raw}");
                }

                state = State.Shader;

                if (!IsContinuationBrace(ref line))
                {
                    continue;
                }
            }

            if (state == State.None)
            {
                ReadOnlyMemory<char> shaderName = line.Value;

                State next = State.AfterShaderName;

                // handle opening brace left on the previous line, e.g:
                // textures/mymap/ice {
                if (shaderName.Span[^1] == '{')
                {
                    shaderName = shaderName[..^1].TrimEnd();
                    next = State.Shader;
                }

                if (shaderName.Span.ContainsAny(Tokens.Braces))
                {
                    throw new InvalidDataException(
                        $"Expected shader name on line {line.Index} in file '{asset.FullPath}', got: '{line.Raw}'");
                }

                shader = new Shader(shaderName, asset, line.Index);
                state = next;
                continue;
            }

            Debug.Assert(shader != null);

            if (state == State.Shader)
            {
                if (line.IsOpeningBrace)
                {
                    state = State.Stage;

                    if (IsContinuationBrace(ref line))
                        goto ReadStage;

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
                    if (line.MatchKeyword(prefix, out token))
                    {
                        if (!token.Span.StartsWith("$", StringComparison.Ordinal))
                            shader.Shaders.Add(token);
                        found = true;
                        break;
                    }
                }

                if (!found && options.OnlySource)
                {
                    foreach (var prefix in _devTexturePrefixes)
                    {
                        if (line.MatchKeyword(prefix, out token))
                        {
                            if (!token.Span.StartsWith("$", StringComparison.Ordinal))
                            {
                                shader.DevResources.Add(token.Trim('"')); // netradiant allows using doublequotes here
                            }

                            found = true;
                            break;
                        }
                    }
                }

                if (found)
                    continue;

                if (line.Value.StartsWithF("implicit"))
                {
                    if (line.Value.TryReadPastWhitespace(out token))
                    {
                        if (token.Length == 1 && token.Span.Equals("-", StringComparison.Ordinal))
                        {
                            shader.ImplicitMapping = shader.Name;
                        }
                        else
                        {
                            shader.ImplicitMapping = token;
                        }
                    }
                    else
                    {
                        logger.Warn($"Missing implicit mapping path on line {line.Index} in shader '{shader.Name}' in file '{asset.FullPath}'");
                    }
                }
                else if (line.MatchKeyword("skyparms", out token))
                {
                    if (!token.TryReadUpToWhitespace(out token))
                    {
                        logger.UnparsableKeyword(asset.FullPath, line.Index, "skyparms", line.Raw);
                        continue;
                    }

                    if (token.Span.Length == 1 && token.Span.Equals("-", StringComparison.Ordinal))
                    {
                        token = shader.Name;
                    }

                    foreach (var suffix in _skySuffixes)
                    {
                        shader.Resources.Add($"{token}{suffix}".AsMemory());
                    }
                }
                else if (line.MatchKeyword("sunshader", out token))
                {
                    shader.Shaders.Add(token);
                }
                else if (line.MatchKeyword("q3map_surfaceModel", out token))
                {
                    if (token.TryReadUpToWhitespace(out token))
                    {
                        shader.Resources.Add(token);
                    }
                    else
                    {
                        logger.UnparsableKeyword(asset.FullPath, line.Index, "q3map_surfaceModel", line.Raw);
                    }
                }
                else if (!shader.HasLightStyles && line.MatchKeyword("q3map_lightstyle", out _))
                {
                    shader.HasLightStyles = true;
                }
            }

            ReadStage:
            if (state == State.Stage)
            {
                if (line.IsOpeningBrace)
                {
                    throw new InvalidDataException(
                        $"Invalid token '{line.Raw}' on line {line.Index} in file {asset.FullPath}");
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

                if (line.MatchKeyword("map", out token) ||
                    line.MatchKeyword("clampMap", out token))
                {
                    // $lightmap, $whiteimage etc
                    if (token.Span[0] != '$')
                    {
                        shader.Resources.Add(token);
                    }
                }
                else if (line.MatchKeyword("animMap", out token))
                {
                    // read past the frames-agument
                    if (token.TryReadPastWhitespace(out token))
                    {
                        AddFrames(shader.Resources, token);

                        static void AddFrames(List<QPath> list, ReadOnlyMemory<char> source)
                        {
                            foreach (var match in Tokens.WhitespaceSeparatedTokens().EnumerateMatches(source.Span))
                            {
                                ReadOnlyMemory<char> value = source.Slice(match).TrimQuotes().Trim();

                                if (!value.IsEmpty)
                                {
                                    list.Add(value);
                                }
                            }
                        }
                    }
                    else
                    {
                        logger.UnparsableKeyword(asset.FullPath, line.Index, "animMap", line.Raw);
                    }
                }
                else if (line.MatchKeyword("videomap", out token))
                {
                    shader.Resources.Add(token);
                }

                continue;
            }
        }

        if (state != State.None)
        {
            throw new InvalidDataException($"Shader '{asset.FullPath}' ended in an invalid state: {state}");
        }
    }

    /// <summary>
    /// Returns whether the line is not empty (just a single opening brace)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsContinuationBrace(ref Line line)
    {
        if (line.Value.Length > 1)
        {
            line = new Line(line.Path, line.Index, line.Value[1..].ToString(), false);
            return true;
        }

        return false;
    }

    private static readonly string[] _skySuffixes = ["_bk", "_dn", "_ft", "_up", "_rt", "_lf"];

    private static readonly string[] _simpleShaderRefPrefixes =
    [
        "q3map_backShader",
        "q3map_baseShader",
        "q3map_cloneShader",
        "q3map_remapShader",
    ];

    private static readonly string[] _devTexturePrefixes =
    [
        "q3map_lightImage",
        "qer_editorImage",
        "q3map_normalImage",
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanSkipShaderDirective(ReadOnlySpan<char> line)
    {
        return (line[0] | 0x20) switch
        {
            'q' => !options.OnlySource && line.StartsWithF("qer_"),
            's' => line.StartsWithF("surfaceparm") || line.StartsWithF("sort"),
            'c' => line.StartsWithF("cull"),
            'n' => line.StartsWithF("nopicmip") || line.StartsWithF("nomipmaps"),
            't' => line.StartsWithF("tesssize"),
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
