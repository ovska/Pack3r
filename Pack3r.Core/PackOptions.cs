namespace Pack3r;

public class PackOptions
{
    public bool ShaderlistOnly { get; set; }
    public bool DevFiles { get; set; }
    public bool RequireAllAssets { get; set; }
    public bool Overwrite { get; set; }
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;
}
