using CommunityToolkit.Diagnostics;
using CommunityToolkit.HighPerformance;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pack3r.Extensions;
using Pack3r.IO;
using ROMC = System.ReadOnlyMemory<char>;

namespace Pack3r.Core.Parsers;

public interface IMapFileParser
{
    Task<MapAssets> ParseMapAssets(
        string path,
        CancellationToken cancellationToken);
}

public class MapFileParser(
    ILogger<MapFileParser> logger,
    ILineReader reader,
    IOptions<PackOptions> options)
    : IMapFileParser
{
    private const StringComparison cmp = StringComparison.OrdinalIgnoreCase;
    private readonly bool _devFiles = options.Value.DevFiles;

    public async Task<MapAssets> ParseMapAssets(
        string path,
        CancellationToken cancellationToken)
    {
        State state = State.None;
        char expect = default;

        Dictionary<ROMC, ROMC> entitydata = new(ROMCharComparer.Instance);
        HashSet<ROMC> shaders = new(ROMCharComparer.Instance);
        HashSet<ROMC> resources = new(ROMCharComparer.Instance);

        ROMC currentEntity = default;
        bool hasStyleLights = false;

        await foreach (var line in reader.ReadLines(path, new LineOptions(KeepRaw: true), cancellationToken))
        {
            if (expect != default)
            {
                if (line.FirstChar == expect)
                {
                    expect = default;
                    continue;
                }

                ThrowHelper.ThrowInvalidDataException(
                    $"Expected '{expect}' on line {line.Index}, actual value: {line.Raw}");
            }

            if (line.FirstChar == '}')
            {
                state = state switch
                {
                    State.Entity => State.None,
                    State.AfterDef => State.Entity,
                    State.BrushDef => State.AfterDef,
                    State.PatchDef => State.AfterDef,
                    _ => ThrowHelper.ThrowInvalidOperationException<State>(
                        $"Invalid .map file, dangling closing bracket on line {line.Index}!")
                };

                if (state == State.None)
                {
                    HandleKeysAndClear();
                }

                continue;
            }

            if (state == State.None)
            {
                Expect("// entity ", in line, out currentEntity);
                state = State.Entity;
                expect = '{';
                continue;
            }

            if (state == State.Entity)
            {
                if (line.FirstChar == '/' && line.FirstChar == '{')
                    continue;

                if (line.FirstChar == '"')
                {
                    var (key, value) = line.ReadKeyValue();
                    entitydata[key] = value;
                }
                else if (line.FirstChar == 'b')
                {
                    if (line.Raw.Equals("brushDef"))
                    {
                        state = State.BrushDef;
                        expect = '{';
                        continue;
                    }
                }
                else if (line.FirstChar == 'p')
                {
                    if (line.Raw.Equals("patchDef2"))
                    {
                        state = State.PatchDef;
                        expect = '{';
                        continue;
                    }
                }
            }

            if (state == State.BrushDef)
            {
                int lastParen = line.Raw.LastIndexOf(')');
                ROMC shaderPart = line.Raw.AsMemory(lastParen + 2);

                if (!CanSkip(shaderPart))
                {
                    int space = line.Raw.AsSpan(lastParen + 2).IndexOf(' ');

                    if (space > 1)
                    {
                        shaders.Add(line.Raw.AsMemory(lastParen + 2, space));
                    }
                    else
                    {
                        ThrowHelper.ThrowInvalidOperationException(
                            $"Malformed brush face definition on line {line.Index}: {line.Raw}");
                    }
                }

                continue;
            }

            if (state == State.PatchDef)
            {
                // skip patch cruft
                if (line.FirstChar is '(' or ')')
                {
                    continue;
                }

                // only non-paren starting line in a patchDef should be the texture
                shaders.Add(line.Value);
            }
        }

        return new MapAssets
        {
            Shaders = shaders,
            Resources = resources,
            HasStyleLights = hasStyleLights,
        };

        void HandleKeysAndClear()
        {
            foreach (var (key, value) in entitydata)
            {
                var span = key.Span;

                if (span.IsEmpty)
                    continue;

                if (span[0] == '_' && span.Length >= 4 && span[1] != 's')
                {
                    if (span.StartsWith("_remap", cmp))
                    {
                        var ranges = value.Split(';');

                        if (ranges.Count >= 2)
                            shaders.Add(value[ranges[1]]);
                    }
                    else if (span.Equals("_fog", cmp))
                    {
                        shaders.Add(value);
                    }
                    else if (span.Equals("_celshader", cmp))
                    {
                        shaders.Add($"textures/{value}".AsMemory());
                    }
                }
                else if (span.StartsWith("model", cmp))
                {
                    if (span.Length == 5)
                    {
                        if (_devFiles || !IsClassName("misc_model"))
                        {
                            resources.Add(value);
                        }
                    }
                    else if (span.Length == 6 && span[5] == '2')
                    {
                        resources.Add(value);
                    }
                }
                else if (span.Equals("skin", cmp) || span.Equals("_skin", cmp))
                {
                    logger.LogWarning(
                        "Entity {entity} has a skin {value}, please check the files required by the skin manually",
                        currentEntity,
                        value);
                    resources.Add(value);
                }
                else if (span.Equals("noise", cmp)
                    || (span.Equals("sound", cmp) && IsClassName("dlight")))
                {
                    resources.Add(value);
                }
                else if (span.Equals("shader", cmp))
                {
                    // terrains require some extra trickery
                    ROMC val = value;
                    if (entitydata.ContainsKey("terrain".AsMemory()) &&
                        !value.Span.StartsWith("textures/", cmp))
                    {
                        val = $"textures/{value}".AsMemory();

                        logger.LogWarning(
                            "Entity {id} has a terrain shader '{value}', please manually ensure the shaders are included",
                            currentEntity,
                            value);
                    }

                    shaders.Add(val);
                }
                else if (span.StartsWith("targetShader", cmp))
                {
                    if (span.Equals("targetShaderName", cmp) ||
                        span.Equals("targetShaderNewName", cmp))
                    {
                        shaders.Add(value);
                    }
                }
                else if (span.Equals("sun", cmp))
                {
                    shaders.Add(value);
                }
                else if (!hasStyleLights && span.Equals("style", cmp) && IsClassName("light"))
                {
                    hasStyleLights = true;
                }
                // "music" ignored, doesn't work in etjump
            }

            entitydata.Clear();
        }

        bool IsClassName(string className)
        {
            return entitydata.GetValueOrDefault("classname".AsMemory()).Span.Equals(className, cmp);
        }
    }

    /// <summary>
    /// Whether the shader is one of the most common ones and should be skipped.
    /// </summary>
    /// <param name="shaderPart"><c>pgm/holo 0 0 0</c></param>
    private static bool CanSkip(ROMC shaderPart)
    {
        var span = shaderPart.Span;

        const string common = "common/";
        const string caulk = "caulk ";
        const string nodraw = "nodraw ";
        const string trigger = "trigger ";

        if (span.Length > 12 &&
            span.DangerousGetReference() == 'c' &&
            span.DangerousGetReferenceAt(6) == '/' &&
            span.StartsWith("common/"))
        {
            span = span[common.Length..];

            return span.DangerousGetReference() switch
            {
                'c' when span.StartsWith(caulk) => true,
                'n' when span.StartsWith(nodraw) => true,
                't' when span.StartsWith(trigger) => true,
                _ => false
            };
        }

        return false;
    }

    private static void Expect(string prefix, in Line line, out ROMC entityId)
    {
        if (!line.Value.Span.StartsWith(prefix))
        {
            ThrowHelper.ThrowInvalidDataException(
                $"Expected line {line.Index} to start with \"{prefix}\", actual value: {line.Raw}");
        }

        entityId = line.Value[prefix.Length..];
    }

    private enum State : byte
    {
        /// <summary>Top level, expecting entity</summary>
        None = 0,

        /// <summary>Entity header read, </summary>
        Entity = 1,

        /// <summary>BrushDef started</summary>
        BrushDef = 2,

        /// <summary>PatchDef started</summary>
        PatchDef = 3,

        /// <summary>BrushDef/PatchDef ended</summary>
        AfterDef = 4,
    }
}
