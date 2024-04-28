using System.Diagnostics;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;
using ROMC = System.ReadOnlyMemory<char>;

namespace Pack3r.Parsers;

public interface IMapFileParser
{
    Task<MapAssets> ParseMapAssets(
        string path,
        CancellationToken cancellationToken);
}

public class MapFileParser(
    ILogger<MapFileParser> logger,
    ILineReader reader,
    PackOptions options)
    : IMapFileParser
{
    private readonly bool _devFiles = options.DevFiles;

    public async Task<MapAssets> ParseMapAssets(
        string path,
        CancellationToken cancellationToken)
    {
        State state = State.None;
        char expect = default;

        Dictionary<ROMC, ROMC> entitydata = new(ROMCharComparer.Instance);
        HashSet<ROMC> nonPrefixedShaders = new(ROMCharComparer.Instance);
        HashSet<ROMC> shaders = new(ROMCharComparer.Instance);
        HashSet<ROMC> resources = new(ROMCharComparer.Instance);

        ROMC currentEntity = default;
        bool hasStyleLights = false;

        List<ROMC> unsupSkins = [];
        List<ROMC> unsupTerrains = [];

        int lineCount = 0;
        var timer = Stopwatch.StartNew();

        await foreach (var line in reader.ReadLines(path, new LineOptions(KeepRaw: true), cancellationToken))
        {
            lineCount = line.Index;

            if (expect != default)
            {
                if (line.FirstChar == expect)
                {
                    expect = default;
                    continue;
                }

                logger.Fatal($"Expected '{expect}' on line {line.Index}, actual value: {line.Raw}");
                throw new ControlledException();
            }

            if (line.FirstChar == '}')
            {
                state = state switch
                {
                    State.Entity => State.None,
                    State.AfterDef => State.Entity,
                    State.BrushDef => State.AfterDef,
                    State.PatchDef => State.AfterDef,
                    _ => (State)byte.MaxValue,
                };

                if (state == (State)byte.MaxValue)
                {
                    logger.Fatal($"Invalid .map file, dangling closing bracket on line {line.Index}!");
                    throw new ControlledException();
                }

                if (state == State.None)
                {
                    HandleKeysAndClear();
                }

                continue;
            }

            if (state == State.None)
            {
                if (!line.Value.Span.StartsWith("// entity ", StringComparison.Ordinal))
                {
                    logger.Fatal($"Expected line {line.Index} in file '{path}' to contain entity ID, actual value: {line.Raw}");
                    throw new ControlledException();
                }

                currentEntity = line.Value["// entity ".Length..];
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
                        var withoutPrefix = line.Raw.AsMemory(lastParen + 2, space);

                        if (nonPrefixedShaders.Add(withoutPrefix))
                        {
                            shaders.Add($"textures/{line.Raw.AsSpan(lastParen + 2, space)}".AsMemory());
                        }
                    }
                    else
                    {
                        logger.Fatal($"Malformed brush face definition in file '{path}' on line {line.Index}: {line.Raw}");
                        throw new ControlledException();
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

                if (nonPrefixedShaders.Add(line.Value))
                {
                    shaders.Add($"textures/{line.Value}".AsMemory());
                }
            }
        }

        if (unsupSkins.Count > 0)
        {
            string entities = string.Join(", ", unsupSkins);
            logger.Warn($"Resources referenced by skins are not supported, please include manually (on entities: {entities})");
        }

        if (unsupTerrains.Count > 0)
        {
            string entities = string.Join(", ", unsupTerrains);
            logger.Warn($"Shaders referenced by terrains are not supported, please include manually (on entities: {entities})");
        }

        logger.System($".map file ({lineCount} lines) parsed successfully in {timer.ElapsedMilliseconds} ms");

        return new MapAssets
        {
            Shaders = shaders,
            Resources = resources,
            HasStyleLights = hasStyleLights,
        };

        void HandleKeysAndClear()
        {
            foreach (var (_key, value) in entitydata)
            {
                var key = _key.Span;

                if (key.IsEmpty)
                    continue;

                if (key[0] == '_' && key.Length >= 4 && key[1] != 's')
                {
                    if (key.StartsWithF("_remap"))
                    {
                        var ranges = value.Split(';');

                        if (ranges.Count >= 2)
                            shaders.Add(value[ranges[1]]);
                    }
                    else if (key.EqualsF("_fog"))
                    {
                        shaders.Add(value);
                    }
                    else if (key.EqualsF("_celshader"))
                    {
                        shaders.Add($"textures/{value}".AsMemory());
                    }
                }
                else if (key.StartsWithF("model"))
                {
                    if (key.Length == 5)
                    {
                        if (_devFiles || !IsClassName("misc_model"))
                        {
                            resources.Add(value);
                        }
                    }
                    else if (key.Length == 6 && key[5] == '2')
                    {
                        resources.Add(value);
                    }
                }
                else if (key.EqualsF("skin") || key.EqualsF("_skin"))
                {
                    unsupSkins.Add(currentEntity);
                    resources.Add(value);
                }
                else if (key.EqualsF("noise") || (key.EqualsF("sound") && IsClassName("dlight")))
                {
                    if (!value.Span.EqualsF("NOSOUND"))
                        resources.Add(value);
                }
                else if (key.EqualsF("shader"))
                {
                    // terrains require some extra trickery
                    ROMC val = value;
                    if (entitydata.ContainsKey("terrain".AsMemory()) &&
                        !value.Span.StartsWithF("textures/"))
                    {
                        val = $"textures/{value}".AsMemory();
                        unsupTerrains.Add(currentEntity);
                    }

                    shaders.Add(val);
                }
                else if (key.StartsWithF("targetShader"))
                {
                    if (key.EqualsF("targetShaderName") ||
                        key.EqualsF("targetShaderNewName"))
                    {
                        shaders.Add(value);
                    }
                }
                else if (key.EqualsF("sun"))
                {
                    shaders.Add(value);
                }
                else if (!hasStyleLights && key.EqualsF("style") && IsClassName("light"))
                {
                    hasStyleLights = true;
                }
                // "music" ignored, doesn't work in etjump
            }

            entitydata.Clear();
        }

        bool IsClassName(string className)
        {
            return entitydata.GetValueOrDefault("classname".AsMemory()).EqualsF(className);
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
            span[0] == 'c' &&
            span[6] == '/' &&
            span.StartsWith("common/"))
        {
            span = span[common.Length..];

            return span[0] switch
            {
                'c' when span.StartsWith(caulk) => true,
                'n' when span.StartsWith(nodraw) => true,
                't' when span.StartsWith(trigger) => true,
                _ => false
            };
        }

        return false;
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
