using Microsoft.IO;
using System.Diagnostics;

namespace Pack3r;

internal static class Global
{
    public static readonly object ConsoleLock = new();

    public static readonly RecyclableMemoryStreamManager StreamManager = new(new RecyclableMemoryStreamManager.Options
    {
        AggressiveBufferReturn = true,
        GenerateCallStacks = Debugger.IsAttached,
        ThrowExceptionOnToArray = false,
    });

    public static string GetVersion() => typeof(Global).Assembly.GetName().Version?.ToString(3) ?? "?.?.?";
}
