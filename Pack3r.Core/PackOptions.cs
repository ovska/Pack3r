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
    public string? ETJumpDir { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
