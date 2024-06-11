using Microsoft.IO;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pack3r;

internal static class Global
{
    public const int MAX_QPATH = 64;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnsureQPathLength(ReadOnlyMemory<char> path)
    {
        if (path.Length > MAX_QPATH)
            ThrowPathTooLong(path);

    }

    [DoesNotReturn]
    public static void ThrowPathTooLong(ReadOnlyMemory<char> path) => throw new InvalidDataException($"Path is too long (max 64): {path}");

    public static readonly object ConsoleLock = new();

    public static readonly RecyclableMemoryStreamManager StreamManager = new(new RecyclableMemoryStreamManager.Options
    {
        AggressiveBufferReturn = true,
        GenerateCallStacks = Debugger.IsAttached,
        ThrowExceptionOnToArray = false,
    });

    public static string GetVersion() => typeof(Global).Assembly.GetName().Version?.ToString(3) ?? "?.?.?";

    public static string Disclaimer() => $"// Modified by Pack3r {GetVersion()}";
}
