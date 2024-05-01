﻿using System.Diagnostics;
using Pack3r.IO;

namespace Pack3r.Models;

[DebuggerDisplay("Shader '{Name}' in {GetAbsolutePath()}")]
public sealed class Shader(
    ReadOnlyMemory<char> name,
    string relativePath,
    AssetSource source)
    : IEquatable<Shader>
{
    public string DestinationPath { get; } = relativePath;
    public AssetSource Source { get; } = source;

    public ReadOnlyMemory<char> Name { get; } = name;

    /// <summary>References to textures, models, videos etc</summary>
    public List<ReadOnlyMemory<char>> Resources { get; } = [];

    /// <summary>References to other shaders</summary>
    public List<ReadOnlyMemory<char>> Shaders { get; } = [];

    /// <summary>Shader generates stylelights</summary>
    public bool HasLightStyles { get; set; }

    /// <summary>Shader includes references to any files needed in pk3</summary>
    public bool NeededInPk3 => Resources.Count > 0 || Shaders.Count > 0 || ImplicitMapping.HasValue;

    public string GetAbsolutePath()
    {
        var path = Path.Combine(Source.RootPath, DestinationPath);
        return OperatingSystem.IsWindows() ? path.Replace('\\', '/') : path;
    }

    /// <summary>
    /// Shader name used to resolve the texture used, texture name with or without extension.
    /// </summary>
    public ReadOnlyMemory<char>? ImplicitMapping { get; set; }

    public bool Equals(Shader? other)
    {
        return ReferenceEquals(this, other);
    }

    public override int GetHashCode()
    {
        throw new NotSupportedException("Shader equality not implemented");
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as Shader);
    }
}
