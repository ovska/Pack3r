﻿using System.Diagnostics;
using Pack3r.Extensions;

namespace Pack3r.Models;

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class RenamableResource
{
    public required string? Name { get; set; }

    public required string AbsolutePath
    {
        get => _absolutePath;
        set => _absolutePath = value.NormalizePath();
    }

    public required string ArchivePath
    {
        get => _archivePath;
        init => _archivePath = value.NormalizePath();
    }

    private string _absolutePath = "";
    private string _archivePath = "";

    public Func<string, PackOptions, string>? Convert { get; init; }

    internal string DebuggerDisplay => $"{{ RenamableResource '{Name}': {ArchivePath} (convert: {Convert is not null}) }}";
}
