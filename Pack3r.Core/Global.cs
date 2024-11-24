using Microsoft.IO;
using Pack3r.Extensions;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pack3r;

internal static class Global
{
    public const int MAX_QPATH = 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureQPathLength(string path)
    {
        if (path.Length > MAX_QPATH)
            ThrowQPathTooLong(path.AsMemory());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureQPathLength(ReadOnlyMemory<char> path)
    {
        if (path.Length > MAX_QPATH)
            ThrowQPathTooLong(path);
    }

    [DoesNotReturn]
    public static void ThrowQPathTooLong(ReadOnlyMemory<char> path)
    {
        throw new InvalidDataException($"QPath is too long ({path.Length}, max 64): '{path.ToString().NormalizePath()}'");
    }

    public static readonly object ConsoleLock = new();

    public static readonly RecyclableMemoryStreamManager StreamManager = new(new()
    {
        AggressiveBufferReturn = true,
        GenerateCallStacks = Debugger.IsAttached,
        ThrowExceptionOnToArray = false,
    });

    public static string Version => typeof(Global).Assembly.GetName().Version?.ToString(3) ?? "?.?.?";

    public static string Disclaimer { get; } = $"// Modified by Pack3r {Version}";

    public static ParallelOptions ParallelOptions(CancellationToken cancellationToken)
    {
        return new ParallelOptions
        {
            MaxDegreeOfParallelism = Debugger.IsAttached ? 1 : -1,
            CancellationToken = cancellationToken,
        };
    }
}
