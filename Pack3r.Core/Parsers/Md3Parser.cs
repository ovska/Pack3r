using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Pack3r.Extensions;
using Pack3r.Logging;
using Pack3r.Models;

namespace Pack3r.Parsers;

public partial class Md3Parser(ILogger<Md3Parser> logger) : IReferenceParser
{
    public string Description => "model";

    public bool CanParse(ReadOnlyMemory<char> resource) => resource.EndsWithF(".md3") || resource.EndsWithF(".mdc");

    public async Task<ResourceList?> Parse(
        IAsset asset,
        CancellationToken cancellationToken)
    {
        using var memoryStream = Global.StreamManager.GetStream(
            tag: "Md3ParseFile",
            requiredSize: 1024 * 64,
            asContiguousBuffer: true);

        await using (var fileStream = asset.OpenRead(isAsync: true))
        {
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
        }

        if (!memoryStream.TryGetBuffer(out ArraySegment<byte> buffer))
            buffer = memoryStream.ToArray();

        if (Impl(asset.FullPath, buffer, out var resources, out var error))
        {
            return resources;
        }
        else
        {
            logger.Warn($"Failed to parse MD3 shader '{asset.FullPath}': {error}");
            return null;
        }
    }

    private static bool Impl(
        string fileName,
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out ResourceList? resources,
        [NotNullWhen(false)] out string? error)
    {
        return fileName.GetExtension().Equals(".mdc", StringComparison.OrdinalIgnoreCase)
            ? Impl<MdcHeader, MdcSurface>(fileName, bytes, out resources, out error)
            : Impl<Md3Header, Md3Surface>(fileName, bytes, out resources, out error);
    }

    private static bool Impl<THeader, TSurface>(
        string path,
        ReadOnlySpan<byte> bytes,
        [NotNullWhen(true)] out ResourceList? resources,
        [NotNullWhen(false)] out string? error)
        where THeader : struct, IModelFormatHeader
        where TSurface : struct, IModelSurfaceHeader
    {
        if (Unsafe.SizeOf<THeader>() > bytes.Length)
        {
            resources = null;
            error = "Cannot read header, file is too small";
            return false;
        }

        THeader header = MemoryMarshal.Read<THeader>(bytes);

        Ident ident = header.Ident;
        if (!THeader.Magic.SequenceEqual(ident))
        {
            resources = null;
            error = $"Invalid {THeader.Name} ident, expected '{Encoding.ASCII.GetString(THeader.Magic)}' but got {ident}";
            return false;
        }

        if (header.Version != THeader.ExpectedVersion)
        {
            resources = null;
            error = $"Invalid version in header, expected {THeader.ExpectedVersion} but got {header.Version}";
            return false;
        }

        resources = [];

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
                if (shader.name.TryGetString(out string? shaderName))
                {
                    if (!string.IsNullOrEmpty(shaderName))
                    {
                        resources.Add(Resource.FromModel(shaderName, path));
                    }
                }
                else
                {
                    resources = null;
                    error = $"Invalid shader name on surf {i}, no null terminator found";
                    return false;
                }
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
