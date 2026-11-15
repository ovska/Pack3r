using System.Diagnostics;
using System.Text;
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
        byte expect = 0;

        // only the keys that are turned into resources, see IsResourceKey
        Dictionary<QString, (QString value, Line line)> entitydata = new(QString.Comparer);
        HashSet<QString> nonPrefixedShaders = new(QString.Comparer);
        var textureLookup = nonPrefixedShaders.GetAlternateLookup<ReadOnlySpan<char>>();
        ResourceList shaders = [];
        ResourceList resources = [];
        ResourceList referenceResources = [];
        Dictionary<Resource, List<ReferenceMiscModel>> miscModels = [];

        // checked but never kept, so copied into reused buffers instead of creating strings
        var currentEntity = new ByteBuffer();
        var className = new ByteBuffer();
        var previousTexture = new ByteBuffer();
        bool hasTerrain = false;
        bool hasStyleLights = false;

        List<QString> unsupTerrains = [];

        int lineCount = 0;
        var timer = Stopwatch.StartNew();

        foreach (var line in reader.ReadRawLines(path))
        {
            lineCount = line.Index;

            if (line.RawSpan.StartsWith("//@$&"u8))
            {
                // layers feature from netradiant-custom commit 1599d0a
                continue;
            }

            if (expect != 0)
            {
                if (line.FirstChar == expect)
                {
                    expect = 0;
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
                if (line.RawSpan.StartsWith("// entity "u8))
                {
                    currentEntity.Set(line.Value["// entity ".Length..]);
                    state = State.Entity;
                    expect = (byte)'{';
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
                if (line.FirstChar is (byte)'/' or (byte)'{')
                    continue;

                if (line.FirstChar == '"')
                {
                    line.ReadKeyValue(out var key, out var value);

                    if (key.EqualsIgnoreCase("classname"u8))
                    {
                        className.Set(value);
                    }
                    else if (key.EqualsIgnoreCase("terrain"u8))
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
                    if (line.RawSpan.SequenceEqual("brushDef"u8))
                    {
                        state = State.BrushDef;
                        expect = (byte)'{';
                        continue;
                    }
                }
                else if (line.FirstChar == 'p')
                {
                    if (line.RawSpan.SequenceEqual("patchDef2"u8))
                    {
                        state = State.PatchDef;
                        expect = (byte)'{';
                        continue;
                    }
                }

                continue;
            }

            if (state == State.BrushDef)
            {
                ReadOnlySpan<byte> raw = line.RawSpan;
                int lastParen = raw.LastIndexOf((byte)')');

                if (lastParen + 2 > raw.Length)
                {
                    logger.Fatal($"Malformed brush face definition in file '{path}' on line {line.Index}: {line.Raw}");
                    throw new ControlledException();
                }

                ReadOnlySpan<byte> shaderPart = raw[(lastParen + 2)..];

                if (!CanSkip(shaderPart))
                {
                    int space = shaderPart.IndexOf((byte)' ');

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
                if (line.FirstChar is (byte)'(' or (byte)')')
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

        void AddTexture(in LineRef line, ReadOnlySpan<byte> value)
        {
            // most faces have the same texture as the one before (85% on a real map), so skip those.
            // ordinal, as a map writes the same name the same way; a different casing just goes to the
            // case-insensitive set below
            if (value.SequenceEqual(previousTexture.Span))
                return;

            previousTexture.Set(value);

            // decoded on the stack for the lookup; a string is only created for a texture not seen before
            Span<char> chars = stackalloc char[256];
            ReadOnlySpan<char> name = value.ToChars(chars);

            if (textureLookup.Add(name))
            {
                shaders.Add(Resource.Shader($"textures/{name}", line.ToLine()));
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
                        bool isMiscModel = className.EqualsIgnoreCase("misc_model"u8);
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
                else if (key.EqualsF("noise") || (key.EqualsF("sound") && className.EqualsIgnoreCase("dlight"u8)))
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
                else if (!hasStyleLights && key.EqualsF("style") && className.EqualsIgnoreCase("light"u8))
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
    private static bool IsResourceKey(ReadOnlySpan<byte> key)
    {
        return key.StartsWithIgnoreCase("model"u8)
            || key.StartsWithIgnoreCase("_remap"u8)
            || key.StartsWithIgnoreCase("targetShader"u8)
            || key.EqualsIgnoreCase("shader"u8)
            || key.EqualsIgnoreCase("noise"u8)
            || key.EqualsIgnoreCase("sound"u8)
            || key.EqualsIgnoreCase("skin"u8)
            || key.EqualsIgnoreCase("_skin"u8)
            || key.EqualsIgnoreCase("_fog"u8)
            || key.EqualsIgnoreCase("_celshader"u8)
            || key.EqualsIgnoreCase("sun"u8)
            || key.EqualsIgnoreCase("style"u8);
    }

    /// <summary>
    /// Whether the shader is one of the most common ones and should be skipped.
    /// </summary>
    /// <param name="span"><c>pgm/holo 0 0 0</c></param>
    private static bool CanSkip(ReadOnlySpan<byte> span)
    {
        if (span.Length > 12 &&
            span[0] == 'c' &&
            span[6] == '/' &&
            span.StartsWith("common/"u8))
        {
            span = span["common/".Length..];

            return span[0] switch
            {
                (byte)'c' when span.StartsWith("caulk "u8) => true,
                (byte)'n' when span.StartsWith("nodraw "u8) => true,
                (byte)'t' when span.StartsWith("trigger "u8) => true,
                _ => false
            };
        }

        return false;
    }

    /// <summary>A reused buffer for a value that's only compared, not kept.</summary>
    private sealed class ByteBuffer
    {
        private byte[] _buffer = new byte[64];
        private int _length;

        public ReadOnlySpan<byte> Span => _buffer.AsSpan(0, _length);

        public void Set(ReadOnlySpan<byte> value)
        {
            if (value.Length > _buffer.Length)
                _buffer = new byte[Math.Max(value.Length, _buffer.Length * 2)];

            value.CopyTo(_buffer);
            _length = value.Length;
        }

        public void Clear() => _length = 0;

        public bool EqualsIgnoreCase(ReadOnlySpan<byte> other) => Ascii.EqualsIgnoreCase(Span, other);
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
