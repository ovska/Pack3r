namespace Pack3r;

public class PackOptions
{
    public FileInfo MapFile { get; set; } = null!;
    public FileInfo Pk3File { get; set; } = null!;

    public bool ShaderlistOnly { get; set; }
    public bool DevFiles { get; set; }
    public bool RequireAllAssets { get; set; }
    public bool Overwrite { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
