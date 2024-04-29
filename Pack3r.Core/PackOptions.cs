using System.Diagnostics.CodeAnalysis;
using Pack3r.Logging;

namespace Pack3r;

public class PackOptions
{
    public required FileInfo MapFile { get; set; }
    public FileInfo? Pk3File { get; set; }

    [MemberNotNullWhen(false, nameof(Pk3File))]
    public bool DryRun { get; set; }

    public bool UseShaderlist { get; set; }

    public bool IncludeSource { get; set; }

    public bool RequireAllAssets { get; set; }

    public bool Overwrite { get; set; }

    public LogLevel LogLevel { get; set; } = LogLevel.Info;

    public string? Rename { get; set; }

    public bool LoadPk3s { get; set; }

    public required List<string> IgnoreSources { get; init; }

    public required List<string> ExcludeSources { get; init; }
}
