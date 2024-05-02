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
    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".md3");

    public async Task<HashSet<Resource>?> Parse(
        string path,
        CancellationToken cancellationToken)
    {
        byte[] file = await File.ReadAllBytesAsync(path, cancellationToken);

        if (Impl(file, out var shaders, out var error))
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

        if (Impl(buffer, out var shaders, out var error))
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
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out HashSet<Resource>? shadersHashSet,
        [NotNullWhen(false)] out string? error)
    {
        if (Unsafe.SizeOf<Md3Header>() > bytes.Length)
        {
            shadersHashSet = null;
            error = "Cannot read header, file is too small";
            return false;
        }

        Md3Header header = MemoryMarshal.Read<Md3Header>(bytes);

        if (!header.ident.IsValid)
        {
            shadersHashSet = null;
            error = $"Invalid md3 ident, expected 'IDP3' but got {header.ident}";
            return false;
        }

        if (header.version != 15)
        {
            shadersHashSet = null;
            error = $"Invalid version in header, expected 15 but got {header.version}";
            return false;
        }

        shadersHashSet = [];

        int surfaceOffset = header.ofs_surfaces;

        for (int i = 0; i < header.num_surfaces; i++)
        {
            Md3Surface surface = MemoryMarshal.Read<Md3Surface>(bytes[surfaceOffset..]);

            if (!surface.ident.IsValid)
            {
                error = $"Invalid ident on surface {i + 1} at byte {surfaceOffset}, expected 'IDP3' but got {surface.ident}";
                return false;
            }

            int shaderOffset = surfaceOffset + surface.ofs_shaders;
            ReadOnlySpan<byte> shaderSlice = bytes.Slice(shaderOffset, surface.num_shaders * Unsafe.SizeOf<MD3Shader>());

            foreach (var shader in MemoryMarshal.Cast<byte, MD3Shader>(shaderSlice))
            {
                shadersHashSet.Add(new Resource(shader.name.ToString().AsMemory(), IsShader: true));
            }

            surfaceOffset += surface.ofs_end;
        }

        error = null;
        return true;
    }

    [InlineArray(4)]
    private struct Md3Ident
    {
        public byte _elem0;

        public readonly bool IsValid => "IDP3"u8.SequenceEqual(this);
        public override readonly string ToString() => Encoding.ASCII.GetString(this);
    }

    [InlineArray(64)]
    private struct Md3Name
    {
        public byte _elem0;
        public override readonly string ToString() => Encoding.ASCII.GetString(((ReadOnlySpan<byte>)this).TrimEnd((byte)0));
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Md3Header
    {
        public Md3Ident ident;
        public int version;
        public Md3Name name;
        public int flags;
        public int num_frames;
        public int num_tags;
        public int num_surfaces;
        public int num_skins;
        public int ofs_frames;
        public int ofs_tags;
        public int ofs_surfaces;
        public int ofs_eof;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Md3Surface
    {
        public Md3Ident ident;
        public Md3Name name;
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
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct MD3Shader
    {
        public Md3Name name;
        public int index;
    }
}
