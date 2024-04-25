using Microsoft.Extensions.Options;

namespace Pack3r;

public sealed class PackOptions : IOptions<PackOptions>
{
    public bool ShaderlistOnly { get; set; }
    public bool DevFiles { get; set; }
    public bool RequireAllAssets { get; set; }
    public bool Overwrite { get; set; }

    PackOptions IOptions<PackOptions>.Value => this;
}
