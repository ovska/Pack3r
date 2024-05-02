using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

public class Md3Parser(ILogger<Md3Parser> logger) : IReferenceParser
{
    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".md3") || resource.EndsWithF(".mdc");

    public async Task<HashSet<Resource>?> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] file = await File.ReadAllBytesAsync(path, cancellationToken);

        if (Impl(Path.GetExtension(path.AsSpan()), file, out var shaders, out var error))
        {
            return shaders;
        }
        else
        {
            logger.Warn($"Failed to parse MD3 shader '{path}': {error}");
            return null;
        }
    }

    public async Task<HashSet<Resource>?> Parse(
        ZipArchiveEntry entry,
        string archivePath,
        CancellationToken cancellationToken)
    {
        MemoryStream memoryStream = new(capacity: 4096);

        await using (var stream = entry.Open())
        {
            await stream.CopyToAsync(stream, cancellationToken);
        }

        if (!memoryStream.TryGetBuffer(out ArraySegment<byte> buffer))
            buffer = memoryStream.ToArray();

        if (Impl(Path.GetExtension(entry.FullName.AsSpan()), buffer, out var shaders, out var error))
        {
            return shaders;
        }
        else
        {
            logger.Warn($"Failed to parse MD3 shader '{archivePath}/{entry.FullName}': {error}");
            return null;
        }
    }

    private static bool Impl(
        ReadOnlySpan<char> fileName,
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out HashSet<Resource>? shadersHashSet,
        [NotNullWhen(false)] out string? error)
    {
        if (Path.GetExtension(fileName).Equals(".mdc", StringComparison.OrdinalIgnoreCase))
        return Impl<MdcHeader, MdcSurface>(bytes, out shadersHashSet, out error);

            return Impl<Md3Header, Md3Surface>(bytes, out shadersHashSet, out error);
    }

    private static bool Impl<THeader, TSurface>(
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out HashSet<Resource>? shadersHashSet,
        [NotNullWhen(false)] out string? error)
        where THeader : struct, IModelFormatHeader
        where TSurface : struct, IModelSurfaceHeader
    {
        if (Unsafe.SizeOf<THeader>() > bytes.Length)
        {
            shadersHashSet = null;
            error = "Cannot read header, file is too small";
            return false;
        }

        THeader header = MemoryMarshal.Read<THeader>(bytes);

        Ident ident = header.Ident;
        if (!THeader.Magic.SequenceEqual(ident))
        {
            shadersHashSet = null;
            error = $"Invalid {THeader.Name} ident, expected '{Encoding.ASCII.GetString(THeader.Magic)}' but got {ident}";
            return false;
        }

        if (header.Version != THeader.ExpectedVersion)
        {
            shadersHashSet = null;
            error = $"Invalid version in header, expected {THeader.ExpectedVersion} but got {header.Version}";
            return false;
        }

        shadersHashSet = [];

        int surfaceOffset = header.SurfaceOffset;

        for (int i = 0; i < header.SurfaceCount; i++)
        {
            TSurface surface = MemoryMarshal.Read<TSurface>(bytes[surfaceOffset..]);

            var surfaceIdent = surface.Ident;
            if (!TSurface.Magic.SequenceEqual(surfaceIdent))
            {
                error = $"Invalid ident on surface {i + 1} at byte {surfaceOffset}, expected '{string.Join(',', TSurface.Magic.ToArray())}' but got {surfaceIdent}";
                return false;
            }

            int shaderOffset = surfaceOffset + surface.ShaderOffset;
            ReadOnlySpan<byte> shaderSlice = bytes.Slice(shaderOffset, surface.ShaderCount * Unsafe.SizeOf<Md3Shader>());

            foreach (var shader in MemoryMarshal.Cast<byte, Md3Shader>(shaderSlice))
            {
                string shaderName = shader.name.ToString().Replace('\\', '/');
                bool isShader = shaderName.GetExtension().IsEmpty;
                shadersHashSet.Add(new Resource(shaderName.AsMemory(), IsShader: isShader));
            }

            surfaceOffset += surface.EndOffset;
        }

        error = null;
        return true;
    }

    private interface IModelFormatHeader
    {
        static abstract int ExpectedVersion { get; }
        static abstract string Name { get; }
        static abstract ReadOnlySpan<byte> Magic { get; }

        int Version { get; }
        Ident Ident { get; }
        int SurfaceOffset { get; }
        int SurfaceCount { get; }
    }

    private interface IModelSurfaceHeader
    {
        static abstract ReadOnlySpan<byte> Magic { get; }

        Ident Ident { get; }
        int ShaderOffset { get; }
        int ShaderCount { get; }
        int EndOffset { get; }
    }

    [InlineArray(4)]
    private struct Ident
    {
        public byte _elem0;

        public override readonly string ToString() => Encoding.ASCII.GetString(this);
    }

    [InlineArray(64)]
    private struct Q3Name
    {
        public byte _elem0;
        public override readonly string ToString() => Encoding.ASCII.GetString(((ReadOnlySpan<byte>)this).TrimEnd((byte)0));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Md3Header : IModelFormatHeader
    {
        public Ident ident;
        public int version;
        public Q3Name name;
        public int flags;
        public int num_frames;
        public int num_tags;
        public int num_surfaces;
        public int num_skins;
        public int ofs_frames;
        public int ofs_tags;
        public int ofs_surfaces;
        public int ofs_eof;

        public static int ExpectedVersion => 15;
        public static string Name => "MD3";
        public static ReadOnlySpan<byte> Magic => "IDP3"u8;
        readonly int IModelFormatHeader.Version => version;
        readonly Ident IModelFormatHeader.Ident => ident;
        readonly int IModelFormatHeader.SurfaceOffset => ofs_surfaces;
        readonly int IModelFormatHeader.SurfaceCount => num_surfaces;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MdcHeader : IModelFormatHeader
    {
        public Ident ident;
        public int version;
        public Q3Name name;
        public int flags;
        public int num_frames;
        public int num_tags;
        public int num_surfaces;
        public int num_skins;
        public int ofs_frames;
        public int ofs_tags;
        public int ofs_tag_infos;
        public int ofs_surfaces;
        public int ofs_eof;

        public static int ExpectedVersion => 2;
        public static string Name => "MDC";
        public static ReadOnlySpan<byte> Magic => "IDPC"u8;
        readonly int IModelFormatHeader.Version => version;
        readonly Ident IModelFormatHeader.Ident => ident;
        readonly int IModelFormatHeader.SurfaceOffset => ofs_surfaces;
        readonly int IModelFormatHeader.SurfaceCount => num_surfaces;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Md3Surface : IModelSurfaceHeader
    {
        public Ident ident;
        public Q3Name name;
        public int flags;
        public int num_frames;
        public int num_shaders;
        public int num_vertices;
        public int num_triangles;
        public int ofs_triangles;
        public int ofs_shaders;
        public int ofs_textCoords;
        public int ofs_vertices;
        public int ofs_end;

        public static ReadOnlySpan<byte> Magic => "IDP3"u8;

        readonly Ident IModelSurfaceHeader.Ident => ident;
        readonly int IModelSurfaceHeader.ShaderOffset => ofs_shaders;
        readonly int IModelSurfaceHeader.ShaderCount => num_shaders;
        readonly int IModelSurfaceHeader.EndOffset => ofs_end;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MdcSurface : IModelSurfaceHeader
    {
        public Ident ident;
        public Q3Name name;
        public int flags;
        public int num_comp_frames;
        public int num_base_frames;
        public int num_shaders;
        public int num_vertices;
        public int num_triangles;
        public int ofs_triangles;
        public int ofs_shaders;
        public int ofs_textCoords;
        public int ofs_base_vertices;
        public int ofs_comp_vertices;
        public int ofs_base_frame_indices;
        public int ofs_comp_frame_indices;
        public int ofs_end;

        public static ReadOnlySpan<byte> Magic => [7, 0, 0, 0];

        readonly Ident IModelSurfaceHeader.Ident => ident;
        readonly int IModelSurfaceHeader.ShaderOffset => ofs_shaders;
        readonly int IModelSurfaceHeader.ShaderCount => num_shaders;
        readonly int IModelSurfaceHeader.EndOffset => ofs_end;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Md3Shader
    {
        public Q3Name name;
        public int index;
    }
}
