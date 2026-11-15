using System.Collections.Concurrent;
using System.Collections.Frozen;
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
    Dictionary<QPath, Shader> GetReferencedShaders(
        Map map,
        CancellationToken cancellationToken);

    IEnumerable<Shader> Parse(
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
    public Dictionary<QPath, Shader> GetReferencedShaders(
        Map map,
        CancellationToken cancellationToken)
    {
        ConcurrentDictionary<QPath, Shader> allShaders = [];
        ConcurrentDictionary<QPath, List<Shader>> duplicateShaders = [];

        // list the files first, in the order they are searched in each source. a group is parsed on one thread:
        // each file on disk is its own group, but a pk3's files are read one at a time through its archive
        List<IAsset[]> groups = [];

        foreach (var source in map.AssetSources)
        {
            IAsset[] files = [.. source.EnumerateShaderFiles(name => SkipShaderFile(source, name))];

            if (source.CanReadFilesConcurrently)
            {
                foreach (var file in files)
                    groups.Add([file]);
            }
            else if (files.Length > 0)
            {
                groups.Add(files);
            }
        }

        var parsed = new List<Shader>[groups.Count];
        int parsedCount = 0;

        using (var progress = progressManager.Create("Parsing shader files", groups.Sum(g => g.Length)))
        {
            Global.ParallelFor(groups.Count, cancellationToken, i =>
            {
                List<Shader> shaders = [];

                foreach (var file in groups[i])
                {
                    shaders.AddRange(Parse(file, cancellationToken));
                    progress.Report(Interlocked.Increment(ref parsedCount));
                }

                parsed[i] = shaders;
            });
        }

        // merged in the order the files were listed, not the order they finished parsing, so shaders defined
        // in more than one file resolve the same way every time
        foreach (var shaders in parsed)
        {
            foreach (var shader in shaders)
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
                                logger.Info($"Shader {a.Name} resolved from '{toReturn.Source}' instead of '{other.Source}'");
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
                                logger.Info(
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

    private bool SkipShaderFile(AssetSource source, string name)
    {
        string fileName = Path.GetFileNameWithoutExtension(name);

        if (fileName.EqualsF("q3shadersCopyForRadiant"))
        {
            if (options.ShaderDebug)
                logger.Info($"Skipped parsing Radiant specific shaderfile {getName()}");
            return true;
        }

        if (fileName.StartsWithF("q3map_") || fileName.StartsWithF("q3map2_"))
        {
            if (options.ShaderDebug)
                logger.Info($"Skipped shaderfile parsing from compiler generated file '{getName()}'");
            return true;
        }

        return false;

        string getName() => Path.Combine(source.RootPath, "scripts", name).NormalizePath();
    }

    public IEnumerable<Shader> Parse(
        IAsset asset,
        CancellationToken cancellationToken)
    {
        List<Shader> shaders = [];
        State state = State.None;

        Shader? shader = null;
        ReadOnlySpan<byte> token;
        bool inComment = false;

        foreach (var _line in reader.ReadLines(asset, cancellationToken))
        {
            LineRef line = _line;

            if (inComment)
            {
                if (line.FirstChar == '*' && line.Value.Length == 2 && line.Value[1] == '/')
                {
                    inComment = false;
                }
                continue;
            }

            if (line.FirstChar == '/' && line.Value.Length == 2 && line.Value[1] == '*')
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
                ReadOnlySpan<byte> shaderName = line.Value;

                State next = State.AfterShaderName;

                // handle opening brace left on the previous line, e.g:
                // textures/mymap/ice {
                if (shaderName[^1] == '{')
                {
                    shaderName = shaderName[..^1].TrimWhitespace();
                    next = State.Shader;
                }

                if (shaderName.IndexOfAny((byte)'{', (byte)'}') >= 0)
                {
                    throw new InvalidDataException(
                        $"Expected shader name on line {line.Index} in file '{asset.FullPath}', got: '{line.Raw}'");
                }

                // every shader is kept in the table of all shaders, so its name is always a string
                shader = new Shader(shaderName.ToQPath(), asset, line.Index);
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
                    shaders.Add(shader);
                    state = State.None;
                    continue;
                }

                // early exit for known irrelevant keywords
                if (CanSkipShaderDirective(line.Value))
                    continue;

                // look up the first word, instead of trying each keyword in turn
                ReadOnlySpan<byte> keyword = SplitKeyword(line.Value, out token, out bool hasArguments);

                // implicitMap, implicitMask, implicitBlend
                if (keyword.StartsWithIgnoreCase("implicit"u8))
                {
                    if (line.Value.TryReadPastWhitespace(out token))
                    {
                        if (token.Length == 1 && token[0] == '-')
                        {
                            shader.ImplicitMapping = shader.Name;
                        }
                        else
                        {
                            shader.ImplicitMapping = token.ToQPath();
                        }
                    }
                    else
                    {
                        logger.Warn($"Missing implicit mapping path on line {line.Index} in shader '{shader.Name}' in file '{asset.FullPath}'");
                    }

                    continue;
                }

                if (!TryGetKeyword(keyword, out ShaderKeyword kind))
                    continue;

                if (kind == ShaderKeyword.ForceInclude)
                {
                    // fog, portals etc. need the shader in the pk3 even without any textures
                    shader.ForcePk3Include = true;
                    continue;
                }

                // the rest need a value after the keyword
                if (!hasArguments)
                    continue;

                switch (kind)
                {
                    case ShaderKeyword.ShaderReference:
                        if (token[0] != '$')
                            shader.Shaders.Add(token.ToQPath());
                        break;

                    case ShaderKeyword.DevTexture when options.OnlySource:
                        if (token[0] != '$')
                            shader.DevResources.Add(token.Trim((byte)'"').ToQPath()); // netradiant allows using doublequotes here
                        break;

                    case ShaderKeyword.SkyParms:
                        if (!token.TryReadUpToWhitespace(out token))
                        {
                            logger.UnparsableKeyword(asset.FullPath, line.Index, "skyparms", line.Raw);
                            break;
                        }

                        // '-' farbox means no envbox is loaded, see ParseSkyParms in tr_shader.c
                        if (!(token.Length == 1 && token[0] == '-'))
                        {
                            string farbox = token.ToAsciiString();

                            foreach (var suffix in _skySuffixes)
                            {
                                shader.Resources.Add(farbox + suffix);
                            }
                        }
                        break;

                    case ShaderKeyword.SunShader:
                        shader.Shaders.Add(token.ToQPath());
                        break;

                    case ShaderKeyword.SurfaceModel:
                        if (token.TryReadUpToWhitespace(out token))
                        {
                            shader.Resources.Add(token.ToQPath());
                        }
                        else
                        {
                            logger.UnparsableKeyword(asset.FullPath, line.Index, "q3map_surfaceModel", line.Raw);
                        }
                        break;

                    case ShaderKeyword.LightStyle:
                        shader.HasLightStyles = true;
                        break;
                }

                continue;
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

                ReadOnlySpan<byte> stageKeyword = SplitKeyword(line.Value, out token, out bool stageHasArguments);

                if (!stageHasArguments)
                    continue;

                if (stageKeyword.EqualsIgnoreCase("map"u8) || stageKeyword.EqualsIgnoreCase("clampMap"u8))
                {
                    // $lightmap, $whiteimage etc
                    if (token[0] != '$')
                    {
                        shader.Resources.Add(token.ToQPath());
                    }
                    else
                    {
                        shader.ForcePk3Include = true;
                    }
                }
                else if (stageKeyword.EqualsIgnoreCase("animMap"u8))
                {
                    // read past the frames-agument
                    if (token.TryReadPastWhitespace(out token))
                    {
                        foreach (var frame in token.EnumerateTokens())
                        {
                            ReadOnlySpan<byte> value = frame.TrimQuotes().TrimWhitespace();

                            if (!value.IsEmpty)
                            {
                                shader.Resources.Add(value.ToQPath());
                            }
                        }
                    }
                    else
                    {
                        logger.UnparsableKeyword(asset.FullPath, line.Index, "animMap", line.Raw);
                    }
                }
                else if (stageKeyword.EqualsIgnoreCase("videomap"u8))
                {
                    shader.Resources.Add(token.ToQPath());
                }

                continue;
            }
        }

        if (state != State.None)
        {
            throw new InvalidDataException($"Shader '{asset.FullPath}' ended in an invalid state: {state}");
        }

        return shaders;
    }

    /// <summary>
    /// Returns whether the line is not empty (just a single opening brace)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsContinuationBrace(ref LineRef line)
    {
        if (line.Value.Length > 1)
        {
            line = line.WithValue(line.Value[1..].TrimWhitespace());
            return true;
        }

        return false;
    }

    private static readonly string[] _skySuffixes = ["_bk", "_dn", "_ft", "_up", "_rt", "_lf"];

    /// <summary>
    /// Splits a trimmed line into its first word and the rest, trimmed. Same as
    /// <see cref="AsciiExtensions.MatchKeyword"/>: the keyword must be followed by a space or a tab.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ReadOnlySpan<byte> SplitKeyword(
        ReadOnlySpan<byte> value,
        out ReadOnlySpan<byte> arguments,
        out bool hasArguments)
    {
        int separator = value.IndexOfAny((byte)' ', (byte)'\t');

        if (separator < 0)
        {
            arguments = default;
            hasArguments = false;
            return value;
        }

        arguments = value[(separator + 1)..].TrimWhitespace();
        hasArguments = true;
        return value[..separator];
    }

    /// <summary>
    /// Looks up a keyword without creating a string: it's decoded on the stack, and anything longer than the
    /// longest keyword isn't one.
    /// </summary>
    private static bool TryGetKeyword(ReadOnlySpan<byte> keyword, out ShaderKeyword kind)
    {
        const int MaxKeywordLength = 32;

        if (keyword.Length > MaxKeywordLength)
        {
            kind = default;
            return false;
        }

        Span<char> chars = stackalloc char[MaxKeywordLength];
        return _shaderKeywords.TryGetValue(keyword.ToChars(chars), out kind);
    }

    private enum ShaderKeyword : byte
    {
        ShaderReference,
        DevTexture,
        SkyParms,
        SunShader,
        SurfaceModel,
        LightStyle,
        ForceInclude,
    }

    private static readonly FrozenDictionary<string, ShaderKeyword>.AlternateLookup<ReadOnlySpan<char>> _shaderKeywords =
        new Dictionary<string, ShaderKeyword>
        {
            ["q3map_backShader"] = ShaderKeyword.ShaderReference,
            ["q3map_baseShader"] = ShaderKeyword.ShaderReference,
            ["q3map_cloneShader"] = ShaderKeyword.ShaderReference,
            ["q3map_remapShader"] = ShaderKeyword.ShaderReference,
            ["q3map_lightImage"] = ShaderKeyword.DevTexture,
            ["qer_editorImage"] = ShaderKeyword.DevTexture,
            ["q3map_normalImage"] = ShaderKeyword.DevTexture,
            ["skyparms"] = ShaderKeyword.SkyParms,
            ["sunshader"] = ShaderKeyword.SunShader,
            ["q3map_surfaceModel"] = ShaderKeyword.SurfaceModel,
            ["q3map_lightstyle"] = ShaderKeyword.LightStyle,
            ["portal"] = ShaderKeyword.ForceInclude,
            ["fogvars"] = ShaderKeyword.ForceInclude,
            ["fogparms"] = ShaderKeyword.ForceInclude,
            ["skyfogvars"] = ShaderKeyword.ForceInclude,
            ["waterfogvars"] = ShaderKeyword.ForceInclude,
            ["lightgridmulamb"] = ShaderKeyword.ForceInclude,
            ["lightgridmuldir"] = ShaderKeyword.ForceInclude,
        }
        .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase)
        .GetAlternateLookup<ReadOnlySpan<char>>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanSkipShaderDirective(ReadOnlySpan<byte> line)
    {
        return (line[0] | 0x20) switch
        {
            'q' => !options.OnlySource && line.StartsWithIgnoreCase("qer_"u8),
            's' => line.StartsWithIgnoreCase("surfaceparm"u8) || line.StartsWithIgnoreCase("sort"u8),
            'c' => line.StartsWithIgnoreCase("cull"u8),
            'n' => line.StartsWithIgnoreCase("nopicmip"u8) || line.StartsWithIgnoreCase("nomipmaps"u8),
            't' => line.StartsWithIgnoreCase("tesssize"u8),
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
