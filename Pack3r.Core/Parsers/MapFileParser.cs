using System.Diagnostics;
using Pack3r.Extensions;
using Pack3r.IO;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

public interface IMapFileParser
{
    MapAssets ParseMapAssets(
        string path,
        CancellationToken cancellationToken);
}

public class MapFileParser(
    ILogger<MapFileParser> logger,
    ILineReader reader,
    PackOptions options)
    : IMapFileParser
{
    public MapAssets ParseMapAssets(
        string path,
        CancellationToken cancellationToken)
    {
        State state = State.None;
        char expect = '\0';

        // only the keys that are turned into resources, see IsResourceKey
        Dictionary<QString, (QString value, Line line)> entitydata = new(QString.Comparer);
        HashSet<QString> nonPrefixedShaders = new(QString.Comparer);
        var textureLookup = nonPrefixedShaders.GetAlternateLookup<ReadOnlySpan<char>>();
        ResourceList shaders = [];
        ResourceList resources = [];
        ResourceList referenceResources = [];
        Dictionary<Resource, List<ReferenceMiscModel>> miscModels = [];

        // checked but never kept, so copied into reused buffers instead of creating strings
        var currentEntity = new CharBuffer();
        var className = new CharBuffer();
        var previousTexture = new CharBuffer();
        bool hasTerrain = false;
        bool hasStyleLights = false;

        List<QString> unsupTerrains = [];

        int lineCount = 0;
        var timer = Stopwatch.StartNew();

        foreach (var line in reader.ReadRawLines(path))
        {
            lineCount = line.Index;

            if (line.RawSpan.StartsWith("//@$&", StringComparison.Ordinal))
            {
                // layers feature from netradiant-custom commit 1599d0a
                continue;
            }

            if (expect != '\0')
            {
                if (line.FirstChar == expect)
                {
                    expect = '\0';
                    continue;
                }

                logger.Fatal($"Expected '{(char)expect}' on line {line.Index}, actual value: {line.Raw}");
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
                if (line.RawSpan.StartsWith("// entity ", StringComparison.Ordinal))
                {
                    currentEntity.Set(line.Value["// entity ".Length..]);
                    state = State.Entity;
                    expect = '{';
                    continue;
                }
                else
                {
                    logger.Fatal($"Expected line {line.Index} in file '{path}' to contain entity ID, actual value: {line.Raw}");
                    throw new ControlledException();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (state == State.Entity)
            {
                if (line.FirstChar is '/' or '{')
                    continue;

                if (line.FirstChar == '"')
                {
                    line.ReadKeyValue(out var key, out var value);

                    if (key.Equals("classname", StringComparison.OrdinalIgnoreCase))
                    {
                        className.Set(value);
                    }
                    else if (key.Equals("terrain", StringComparison.OrdinalIgnoreCase))
                    {
                        hasTerrain = true;
                    }
                    else if (IsResourceKey(key))
                    {
                        entitydata[key.ToQString()] = (value.ToQString(), line.ToLine());
                    }
                }
                else if (line.FirstChar == 'b')
                {
                    if (line.RawSpan.SequenceEqual("brushDef"))
                    {
                        state = State.BrushDef;
                        expect = '{';
                        continue;
                    }
                }
                else if (line.FirstChar == 'p')
                {
                    if (line.RawSpan.SequenceEqual("patchDef2"))
                    {
                        state = State.PatchDef;
                        expect = '{';
                        continue;
                    }
                }

                continue;
            }

            if (state == State.BrushDef)
            {
                ReadOnlySpan<char> raw = line.RawSpan;
                int lastParen = raw.LastIndexOf(')');

                if (lastParen + 2 > raw.Length)
                {
                    logger.Fatal($"Malformed brush face definition in file '{path}' on line {line.Index}: {line.Raw}");
                    throw new ControlledException();
                }

                ReadOnlySpan<char> shaderPart = raw[(lastParen + 2)..];

                if (!CanSkip(shaderPart))
                {
                    int space = shaderPart.IndexOf(' ');

                    if (space > 1)
                    {
                        AddTexture(in line, shaderPart[..space]);
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
                AddTexture(in line, line.Value);
            }
        }

        if (unsupTerrains.Count > 0)
        {
            string entities = string.Join(", ", unsupTerrains);
            logger.Warn($"Shaders referenced by terrains are not supported, please include manually (on entities: {entities})");
        }

        logger.Debug($".map file ({lineCount:N0} lines) parsed successfully in {timer.ElapsedMilliseconds} ms");

        return new MapAssets
        {
            Shaders = shaders,
            Resources = resources,
            ReferenceResources = referenceResources,
            MiscModels = miscModels,
            HasStyleLights = hasStyleLights,
        };

        void AddTexture(in LineRef line, ReadOnlySpan<char> value)
        {
            // most faces have the same texture as the one before (85% on a real map), so skip those.
            // ordinal, as a map writes the same name the same way; a different casing just goes to the
            // case-insensitive set below
            if (value.SequenceEqual(previousTexture.Span))
                return;

            previousTexture.Set(value);

            // a string is only created for a texture not seen before
            if (textureLookup.Add(value))
            {
                shaders.Add(Resource.Shader($"textures/{value}", line.ToLine()));
            }
        }

        void HandleKeysAndClear()
        {
            foreach (var (_key, (value, line)) in entitydata)
            {
                var key = _key.Span;

                // _remap handled elsewhere

                if (key.EqualsF("_fog"))
                {
                    shaders.Add(new Resource(value, isShader: true, in line));
                }
                else if (key.EqualsF("_celshader"))
                {
                    shaders.Add(new Resource($"textures/{value}", isShader: true, in line));
                }
                else if (key.StartsWithF("model"))
                {
                    if (key.Length == 5)
                    {
                        bool isMiscModel = className.Span.Equals("misc_model", StringComparison.OrdinalIgnoreCase);
                        var res = new Resource(value, isShader: false, in line, sourceOnly: isMiscModel);

                        if (options.OnlySource || !isMiscModel)
                        {
                            referenceResources.Add(res);
                            resources.Add(res);
                        }

                        if (!miscModels.TryGetValue(res, out var list))
                            miscModels[res] = list = [];

                        list.Add(new ReferenceMiscModel(entitydata, in line));
                    }
                    else if (key.Length == 6 && key[5] == '2')
                    {
                        var res = Resource.File(value, in line);
                        referenceResources.Add(res);
                        resources.Add(res);
                    }
                }
                else if (key.EqualsF("skin") || key.EqualsF("_skin"))
                {
                    var res = Resource.File(value, in line);
                    resources.Add(res);
                    referenceResources.Add(res);
                }
                else if (key.EqualsF("noise") || (key.EqualsF("sound") && className.Span.Equals("dlight", StringComparison.OrdinalIgnoreCase)))
                {
                    if (!value.Span.EqualsF("NOSOUND"))
                        resources.Add(Resource.File(value, in line));
                }
                else if (key.EqualsF("shader"))
                {
                    // terrains require some extra trickery
                    QPath val = value;
                    if (hasTerrain && !value.Span.StartsWithF("textures/"))
                    {
                        val = $"textures/{value}".AsMemory();
                        unsupTerrains.Add(currentEntity.Span.ToQString());
                    }

                    shaders.Add(Resource.Shader(val, in line));
                }
                else if (key.StartsWithF("targetShader"))
                {
                    if (key.EqualsF("targetShaderName") ||
                        key.EqualsF("targetShaderNewName"))
                    {
                        shaders.Add(Resource.Shader(value, in line));
                    }
                }
                else if (key.EqualsF("sun"))
                {
                    shaders.Add(Resource.Shader(value, in line));
                }
                else if (!hasStyleLights && key.EqualsF("style") && className.Span.Equals("light", StringComparison.OrdinalIgnoreCase))
                {
                    hasStyleLights = true;
                }
                // "music" ignored, doesn't work in etjump
            }

            entitydata.Clear();
            className.Clear();
            hasTerrain = false;
        }
    }

    /// <summary>
    /// Whether the entity key is used by <c>HandleKeysAndClear</c> or <see cref="ReferenceMiscModel"/>, others
    /// (origin, angle, targetname etc.) are never turned into strings.
    /// </summary>
    private static bool IsResourceKey(ReadOnlySpan<char> key)
    {
        return key.StartsWith("model", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("_remap", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("targetShader", StringComparison.OrdinalIgnoreCase)
            || key.Equals("shader", StringComparison.OrdinalIgnoreCase)
            || key.Equals("noise", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sound", StringComparison.OrdinalIgnoreCase)
            || key.Equals("skin", StringComparison.OrdinalIgnoreCase)
            || key.Equals("_skin", StringComparison.OrdinalIgnoreCase)
            || key.Equals("_fog", StringComparison.OrdinalIgnoreCase)
            || key.Equals("_celshader", StringComparison.OrdinalIgnoreCase)
            || key.Equals("sun", StringComparison.OrdinalIgnoreCase)
            || key.Equals("style", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether the shader is one of the most common ones and should be skipped.
    /// </summary>
    /// <param name="span"><c>pgm/holo 0 0 0</c></param>
    private static bool CanSkip(ReadOnlySpan<char> span)
    {
        if (span.Length > 12 &&
            span[0] == 'c' &&
            span[6] == '/' &&
            span.StartsWith("common/", StringComparison.Ordinal))
        {
            span = span["common/".Length..];

            return span[0] switch
            {
                'c' when span.StartsWith("caulk ", StringComparison.Ordinal) => true,
                'n' when span.StartsWith("nodraw ", StringComparison.Ordinal) => true,
                't' when span.StartsWith("trigger ", StringComparison.Ordinal) => true,
                _ => false
            };
        }

        return false;
    }

    /// <summary>A reused buffer for a value that's only compared, not kept.</summary>
    private sealed class CharBuffer
    {
        private char[] _buffer = new char[64];
        private int _length;

        public ReadOnlySpan<char> Span => _buffer.AsSpan(0, _length);

        public void Set(ReadOnlySpan<char> value)
        {
            if (value.Length > _buffer.Length)
                _buffer = new char[Math.Max(value.Length, _buffer.Length * 2)];

            value.CopyTo(_buffer);
            _length = value.Length;
        }

        public void Clear() => _length = 0;
    }
}

file enum State : byte
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
