using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

public partial class Md3Parser(ILogger<Md3Parser> logger) : IReferenceParser
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
}
