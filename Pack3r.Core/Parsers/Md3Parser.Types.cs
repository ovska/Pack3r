using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Pack3r.Parsers;

public partial class Md3Parser
{
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

        public override readonly string ToString() => throw new NotSupportedException();

        public readonly bool TryGetString([NotNullWhen(true)] out string? value)
        {
            scoped ReadOnlySpan<byte> span = this;
            int nullterm = span.IndexOf((byte)'\0');

            if (nullterm == -1)
            {
                value = null;
                return false;
            }

            value = Encoding.ASCII.GetString(span[..nullterm]);
            return true;
        }
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