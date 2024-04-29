using System.Diagnostics.CodeAnalysis;
using Pack3r.Logging;

namespace Pack3r;

public class PackOptions
{
    public required FileInfo MapFile { get; set; }
    public FileInfo? Pk3File { get; set; }

    [MemberNotNullWhen(false, nameof(Pk3File))]
    public bool DryRun { get; set; }
    public bool ShaderlistOnly { get; set; }
    public bool DevFiles { get; set; }
    public bool RequireAllAssets { get; set; }
    public bool Overwrite { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
    public string? Rename { get; set; }

    public bool Pure { get; set; }
    public bool LoadPk3s { get; set; }

    public List<string> IgnoreSources { get; set; } = [];
    public List<string> ExcludedSources { get; set; } = ["pak0.pk3"];
}
