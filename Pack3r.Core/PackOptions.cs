using System.Diagnostics.CodeAnalysis;
using Pack3r.Logging;

namespace Pack3r;

public class PackOptions
{
    public required FileInfo MapFile { get; set; }
    public FileInfo? Pk3File { get; set; }

    [MemberNotNullWhen(false, nameof(Pk3File))]
    public bool DryRun { get; set; }

    public bool ShaderDebug { get; set; }

    public bool ReferenceDebug { get; set; }

    public bool OnlySource { get; set; }

    public bool RequireAllAssets { get; set; }

    public bool Overwrite { get; set; }

    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    public string? Rename { get; set; }

    public bool LoadPk3s { get; set; }

    public List<string> UnscannedSources { get; init; } = null!;

    public List<string> UnpackedSources { get; init; } = null!;

    public List<string> ModFolders { get; init; } = null!;
} 
